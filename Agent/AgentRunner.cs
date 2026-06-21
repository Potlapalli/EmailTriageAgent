using Azure.AI.Agents.Persistent;
using EmailTriageAgent.Models;
using EmailTriageAgent.Tools;

namespace EmailTriageAgent.Agent;

/// <summary>
/// Orchestrates a single triage session.
/// Updated to use async DispatchAsync now that tool handlers call real APIs.
/// </summary>
public class AgentRunner
{
    private readonly PersistentAgentsClient _client;
    private readonly ToolHandlers _handlers;

    private const int PollIntervalMs = 1000;
    private const int MaxPollIterations = 120;

    public AgentRunner(PersistentAgentsClient client, ToolHandlers handlers)
    {
        _client = client;
        _handlers = handlers;
    }

    public async Task<TriageSession> RunTriageAsync(string agentId)
    {
        var threadResponse = await _client.Threads.CreateThreadAsync();
        var thread = threadResponse.Value;
        Console.WriteLine($"[Runner] Thread created: {thread.Id}");

        await _client.Messages.CreateMessageAsync(
            threadId: thread.Id,
            role: MessageRole.User,
            content: "Please triage my inbox. Fetch my 3 most recent unread emails only," + 
                     "mark them as read" +
                     "classify urgency, draft replies for anything urgent, and block time" + 
                     "on my calendar for anything that needs focused follow-up work."
        );

        var runResponse = await _client.Runs.CreateRunAsync(thread.Id, agentId);
        var run = runResponse.Value;
        Console.WriteLine($"[Runner] Run started: {run.Id}");

        int iteration = 0;
        while (!IsTerminalStatus(run.Status))
        {
            if (++iteration > MaxPollIterations)
                throw new TimeoutException($"Agent run timed out after {MaxPollIterations} polls.");

            await Task.Delay(PollIntervalMs);

            var pollResponse = await _client.Runs.GetRunAsync(thread.Id, run.Id);
            run = pollResponse.Value;
            Console.WriteLine($"[Runner] Status: {run.Status} (poll #{iteration})");

            if (run.Status == RunStatus.RequiresAction &&
                run.RequiredAction is SubmitToolOutputsAction toolAction)
            {
                run = await ExecuteToolCallsAsync(run, toolAction);
            }
        }

        if (run.Status == RunStatus.Failed)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Runner] Run failed: {run.LastError?.Message}");
            Console.ResetColor();
            throw new InvalidOperationException($"Agent run failed: {run.LastError?.Message}");
        }

        await PrintFinalResponseAsync(thread.Id);

        return new TriageSession(
            ThreadId: thread.Id,
            Results: _handlers.TriageResults.ToList(),
            CalendarBlocks: _handlers.CalendarBlocks.ToList(),
            TriagedAt: DateTime.UtcNow
        );
    }

    private async Task<ThreadRun> ExecuteToolCallsAsync(
        ThreadRun run,
        SubmitToolOutputsAction toolAction)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[Runner] Agent requesting {toolAction.ToolCalls.Count} tool call(s)...");
        Console.ResetColor();

        var outputs = new List<ToolOutput>();

        foreach (var call in toolAction.ToolCalls)
        {
            if (call is not RequiredFunctionToolCall fn) continue;

            string result;
            try
            {
                // ✅ Now async — real Gmail API calls happen here
                result = await _handlers.DispatchAsync(fn.Name, fn.Arguments);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [Tool ERROR] {fn.Name}: {ex.Message}");
                Console.ResetColor();
                result = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
            }

            outputs.Add(new ToolOutput(call.Id, result));
        }

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
            threadId: threadId,
            order: ListSortOrder.Descending
        );

        await foreach (var message in messages)
        {
            if (message.Role != MessageRole.Agent) continue;
            foreach (var content in message.ContentItems)
            {
                if (content is MessageTextContent text)
                    Console.WriteLine(text.Text);
            }
            break;
        }

        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    private static bool IsTerminalStatus(RunStatus status) =>
        status == RunStatus.Completed ||
        status == RunStatus.Failed ||
        status == RunStatus.Cancelled ||
        status == RunStatus.Expired;
}
