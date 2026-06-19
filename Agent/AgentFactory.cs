using Azure.AI.Agents.Persistent;
using Azure.Identity;
using EmailTriageAgent.Tools;

namespace EmailTriageAgent.Agent;

/// <summary>
/// Manages the lifecycle of the persistent Azure AI agent.
///
/// KEY CONCEPT: An agent is a reusable resource stored in Azure.
/// Create it ONCE (bootstrap), then reference it by ID on every run.
/// Think of the agent_id like a microservice endpoint — configure once, call many times.
/// </summary>
public class AgentFactory
{
    private readonly PersistentAgentsClient _client;

    private const string AgentName = "email-triage-agent";

    private const string SystemPrompt = """
        You are an expert email triage assistant. Your job is to help a software engineer
        manage their inbox efficiently by analysing unread emails and prioritising them.

        ## Your workflow — follow this exactly, in order:

        1. Call fetch_unread_emails to retrieve the inbox.

        2. For EACH email, analyse it and call record_triage_decision with:
           - Urgency: Critical (needs response <1hr), High (today), Medium (1-2 days), Low (no action)
           - A one-sentence reason for the urgency level
           - Whether a calendar focus block is needed

        3. For Critical and High urgency emails:
           a. Call create_draft_reply with a professional, concise draft response.
           b. If the email requires dedicated follow-up work, call create_calendar_block
              to schedule a focused work session for the NEXT BUSINESS DAY morning.

        4. After processing ALL emails, provide a concise summary:
           - How many emails in each urgency category
           - What drafts were created
           - What calendar blocks were scheduled

        ## Urgency guidelines:
        - Critical: Production incidents, security issues, urgent requests from leadership
        - High: Deadlines today/tomorrow, important stakeholder requests, PR reviews needed soon
        - Medium: Regular work requests, meetings to schedule, non-urgent questions
        - Low: Newsletters, recruiting, FYI threads, social notifications

        ## Tone for draft replies:
        - Professional but direct
        - Acknowledge the email, state next steps clearly
        - Keep replies under 100 words unless more context is necessary
        - Never promise specific delivery dates — use "I'll look into this and get back to you"

        Always process all emails before writing the final summary.
        """;

    public AgentFactory(string projectEndpoint)
    {
        _client = new PersistentAgentsClient(
            projectEndpoint,
            new DefaultAzureCredential()
        );
    }

    public PersistentAgentsClient Client => _client;

    /// <summary>
    /// Gets an existing agent by ID, or creates a new one if the ID is null/empty.
    /// 
    /// Usage pattern:
    ///   First run:  agentId = null → creates agent → prints ID to save
    ///   Later runs: agentId = "asst_xxx" → retrieves existing agent
    /// </summary>
    public async Task<PersistentAgent> GetOrCreateAgentAsync(string? agentId = null)
    {
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            Console.WriteLine($"[AgentFactory] Using existing agent: {agentId}");
            var getResponse = await _client.Administration.GetAgentAsync(agentId);
            return getResponse.Value;
        }

        Console.WriteLine("[AgentFactory] Creating new agent in Azure AI Foundry...");

        var response = await _client.Administration.CreateAgentAsync(
            model:        Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME") ?? "gpt-4o",
            name:         AgentName,
            instructions: SystemPrompt,
            tools:        ToolDefinitions.All
        );

        var agent = response.Value;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[AgentFactory] Agent created successfully!");
        Console.WriteLine($"[AgentFactory] *** Save this agent ID: {agent.Id} ***");
        Console.WriteLine($"[AgentFactory] Set AGENT_ID={agent.Id} in your .env to reuse it.");
        Console.ResetColor();

        return agent;
    }

    /// <summary>
    /// Deletes an agent from Azure. Use during cleanup or when you want to 
    /// recreate with updated instructions/tools.
    /// </summary>
    public async Task DeleteAgentAsync(string agentId)
    {
        await _client.Administration.DeleteAgentAsync(agentId);
        Console.WriteLine($"[AgentFactory] Agent {agentId} deleted.");
    }
}
