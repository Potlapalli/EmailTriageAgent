using DotNetEnv;
using EmailTriageAgent.Agent;
using EmailTriageAgent.Services;
using EmailTriageAgent.Tools;

Env.TraversePath().Load();

var mode = args.FirstOrDefault()?.ToLower() ?? "run";
var endpoint = Environment.GetEnvironmentVariable("PROJECT_ENDPOINT")
               ?? throw new InvalidOperationException("PROJECT_ENDPOINT not set.");

var clientSecretPath = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET_PATH")
                       ?? "client_secret.json";

var factory = new AgentFactory(endpoint);

switch (mode)
{
    case "bootstrap":
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  BOOTSTRAP: Creating agent in Azure...");
        Console.WriteLine("═══════════════════════════════════════════");
        var newAgent = await factory.GetOrCreateAgentAsync(agentId: null);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n  ✅ Add this to your .env:");
        Console.WriteLine($"     AGENT_ID={newAgent.Id}");
        Console.ResetColor();
        break;

    case "delete":
        var toDelete = Environment.GetEnvironmentVariable("AGENT_ID")
                       ?? throw new InvalidOperationException("AGENT_ID not set.");
        Console.Write($"Delete agent {toDelete}? (yes/no): ");
        if (Console.ReadLine()?.Trim().ToLower() == "yes")
            await factory.DeleteAgentAsync(toDelete);
        break;

    default:
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine("  EMAIL TRIAGE AGENT  (Gmail + Calendar connected)");
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine();

        // ── Step 1: Authenticate Gmail ─────────────────────────────────────
        // First run: browser opens for consent
        // Later runs: loads token silently from gmail_token/
        Console.WriteLine("[Setup] Connecting to Gmail...");
        var gmail = await GmailApiService.CreateAsync(
            clientSecretPath: clientSecretPath,
            tokenStorePath: "gmail_token"
        );

        // ── Step 2: Authenticate Google Calendar ───────────────────────────
        // Uses same client_secret.json, separate token store (calendar_token/)
        // First run: browser opens again for Calendar consent
        // Later runs: loads silently
        Console.WriteLine("[Setup] Connecting to Google Calendar...");
        var calendar = await GoogleCalendarService.CreateAsync(
            clientSecretPath: clientSecretPath,
            tokenStorePath: "calendar_token"
        );

        // ── Step 3: Wire both into tool handlers ───────────────────────────
        var handlers = new ToolHandlers(gmail, calendar);

        // ── Step 4: Get or reuse the Azure AI agent ────────────────────────
        var agentId = Environment.GetEnvironmentVariable("AGENT_ID");
        var agent = await factory.GetOrCreateAgentAsync(agentId);

        // ── Step 5: Run the triage session ────────────────────────────────
        var runner = new AgentRunner(factory.Client, handlers);

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
