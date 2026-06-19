using DotNetEnv;
using EmailTriageAgent.Agent;
using EmailTriageAgent.Services;

 //─────────────────────────────────────────────────────────────────────────────
 // Email Triage Agent — Entry Point

 // TWO MODES:
 //   dotnet run              → run triage (uses AGENT_ID from .env if set)
 //   dotnet run -- bootstrap → create a new agent and print its ID
 //   dotnet run -- delete    → delete the agent stored in AGENT_ID
 //─────────────────────────────────────────────────────────────────────────────


Env.TraversePath().Load();


var mode = args.FirstOrDefault()?.ToLower() ?? "run";
var endpoint = Environment.GetEnvironmentVariable("PROJECT_ENDPOINT")
               ?? throw new InvalidOperationException(
                   "PROJECT_ENDPOINT is not set. " +
                   "Copy .env.example to .env and fill in your Azure AI Foundry project endpoint.");

var factory = new AgentFactory(endpoint);

switch (mode)
{
    // ── Bootstrap: create the agent once, save the ID ─────────────────────────
    case "bootstrap":
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  BOOTSTRAP: Creating agent in Azure...");
        Console.WriteLine("═══════════════════════════════════════════");
        var newAgent = await factory.GetOrCreateAgentAsync(agentId: null);
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✅ Done! Add this to your .env file:");
        Console.WriteLine($"     AGENT_ID={newAgent.Id}");
        Console.ResetColor();
        break;

    // ── Delete: clean up the agent from Azure ─────────────────────────────────
    case "delete":
        var agentToDelete = Environment.GetEnvironmentVariable("AGENT_ID")
                            ?? throw new InvalidOperationException("AGENT_ID not set in .env");
        Console.Write($"Delete agent {agentToDelete}? (yes/no): ");
        if (Console.ReadLine()?.Trim().ToLower() == "yes")
            await factory.DeleteAgentAsync(agentToDelete);
        break;

    // ── Run: execute a triage session ─────────────────────────────────────────
    default:
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  EMAIL TRIAGE AGENT");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine();

        // Reuse existing agent or create on-the-fly if AGENT_ID is not set
        var agentId = Environment.GetEnvironmentVariable("AGENT_ID");
        var agent = await factory.GetOrCreateAgentAsync(agentId);

        var runner = new AgentRunner(factory.Client);

        try
        {
            var session = await runner.RunTriageAsync(agent.Id);
            ResultsDisplay.PrintSession(session);
        }
        catch (TimeoutException tex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Triage timed out: {tex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
        catch (InvalidOperationException iex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Agent run failed: {iex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
        break;
}
