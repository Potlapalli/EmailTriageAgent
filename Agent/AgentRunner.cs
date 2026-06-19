using Azure.AI.Agents.Persistent;
using EmailTriageAgent.Models;
using EmailTriageAgent.Tools;

namespace EmailTriageAgent.Agent;

/// <summary>
/// Orchestrates a single triage session:
///   1. Creates a thread (conversation session)
///   2. Sends the user prompt
///   3. Runs the agent loop — polls status, handles tool calls
///   4. Returns the structured TriageSession result
///
/// PATTERN: The run loop is essentially an event-driven state machine.
/// Each poll either advances the run or triggers a tool call.
/// This maps naturally to an event-sourced system: each state transition
/// is deterministic given the current run status.
///
///   Queued → InProgress → RequiresAction → (tool executed) → InProgress → Completed
/// </summary>
public class AgentRunner
{
    private readonly PersistentAgentsClient _client;
    private readonly ToolHandlers           _handlers;

    private const int PollIntervalMs    = 1000;
    private const int MaxPollIterations = 120; // 2 minute timeout

    public AgentRunner(PersistentAgentsClient client)
    {
        _client   = client;
        _handlers = new ToolHandlers();
    }

    public async Task<TriageSession> RunTriageAsync(string agentId)
    {
        // ── Step 1: Create a thread (one per triage session / user session) ──
        var threadResponse = await _client.Threads.CreateThreadAsync();
        var thread = threadResponse.Value;
        Console.WriteLine($"[Runner] Thread created: {thread.Id}");

        // ── Step 2: Add the user message that kicks off the triage ───────────
        await _client.Messages.CreateMessageAsync(
            threadId: thread.Id,
            role:     MessageRole.User,
            content:  "Please triage my inbox. Fetch my unread emails, classify urgency, " +
                      "draft replies for anything urgent, and block time on my calendar " +
                      "for anything that needs focused follow-up work."
        );

        // ── Step 3: Start the agent run ───────────────────────────────────────
        var runResponse = await _client.Runs.CreateRunAsync(thread.Id, agentId);
        var run = runResponse.Value;
        Console.WriteLine($"[Runner] Run started: {run.Id}");

        // ── Step 4: Poll loop — the heart of the agent ────────────────────────
        int iteration = 0;
        while (!IsTerminalStatus(run.Status))
        {
            if (++iteration > MaxPollIterations)
                throw new TimeoutException($"Agent run timed out after {MaxPollIterations} polls.");

            await Task.Delay(PollIntervalMs);
            run = await _client.Runs.GetRunAsync(thread.Id, run.Id);

            Console.WriteLine($"[Runner] Status: {run.Status} (poll #{iteration})");

            // ── Handle tool calls ─────────────────────────────────────────────
            if (run.Status == RunStatus.RequiresAction &&
                run.RequiredAction is SubmitToolOutputsAction toolAction)
            {
                run = await ExecuteToolCallsAsync(run, toolAction);
            }
        }

        // ── Step 5: Check outcome ─────────────────────────────────────────────
        if (run.Status == RunStatus.Failed)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Runner] Run failed: {run.LastError?.Message}");
            Console.ResetColor();
            throw new InvalidOperationException($"Agent run failed: {run.LastError?.Message}");
        }

        // ── Step 6: Print the agent's final text response ─────────────────────
        await PrintFinalResponseAsync(thread.Id);

        // ── Step 7: Return the structured session result ──────────────────────
        return new TriageSession(
            ThreadId:       thread.Id,
            Results:        _handlers.TriageResults.ToList(),
            CalendarBlocks: _handlers.CalendarBlocks.ToList(),
            TriagedAt:      DateTime.UtcNow
        );
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<ThreadRun> ExecuteToolCallsAsync(
        ThreadRun                    run,
        SubmitToolOutputsAction   toolAction)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[Runner] Agent requesting {toolAction.ToolCalls.Count} tool call(s)...");
        Console.ResetColor();

        var outputs = new List<ToolOutput>();

        foreach (var call in toolAction.ToolCalls)
        {
            if (call is not RequiredFunctionToolCall fn)
                continue;

            string result;
            try
            {
                result = _handlers.Dispatch(fn.Name, fn.Arguments);
            }
            catch (Exception ex)
            {
                // Return the error as the tool result — agent can handle gracefully
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [Tool ERROR] {fn.Name}: {ex.Message}");
                Console.ResetColor();
                result = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
            }

            outputs.Add(new ToolOutput(call.Id, result));
        }

        // Submit all tool results in one call — more efficient than one-by-one
        return await _client.Runs.SubmitToolOutputsToRunAsync(run, outputs);
    }

    private async Task PrintFinalResponseAsync(string threadId)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.WriteLine("  AGENT SUMMARY");
        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.ResetColor();

        var messages = _client.Messages.GetMessagesAsync(
            threadId:  threadId,
            order:     ListSortOrder.Descending
        );

        await foreach (var message in messages)
        {
            if (message.Role != MessageRole.Agent) continue;

            foreach (var content in message.ContentItems)
            {
                if (content is MessageTextContent text)
                    Console.WriteLine(text.Text);
            }
            break; // Only print the last agent message
        }

        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    private static bool IsTerminalStatus(RunStatus status) =>
        status == RunStatus.Completed  ||
        status == RunStatus.Failed     ||
        status == RunStatus.Cancelled  ||
        status == RunStatus.Expired;
}
