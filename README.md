# Email Triage Agent — C# / Azure AI Foundry

A learning project demonstrating how to build a multi-tool AI agent
using Azure AI Foundry Agent Service and the .NET SDK.

The agent reads your Gmail inbox, classifies emails by urgency,
drafts replies for urgent emails, and blocks time on Google Calendar
for anything needing focused follow-up.

---

## Project Structure

```
EmailTriageAgent/
├── Program.cs                        # Entry point — bootstrap / run / delete modes
├── EmailTriageAgent.csproj
├── .env.example                      # Config template — copy to .env
│
└── src/
    ├── Models/
    │   └── EmailModels.cs            # Domain types: Email, TriageResult, etc.
    │
    ├── Tools/
    │   ├── ToolDefinitions.cs        # JSON schemas the agent sees (what it CAN call)
    │   └── ToolHandlers.cs           # Actual execution logic (what HAPPENS when called)
    │
    ├── Agent/
    │   ├── AgentFactory.cs           # Creates / retrieves the persistent agent
    │   └── AgentRunner.cs            # The agent run loop (poll → tool call → result)
    │
    └── Services/
        └── ResultsDisplay.cs         # Console output formatter
```

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
- An [Azure AI Foundry](https://ai.azure.com) project with a GPT-4o deployment
- RBAC role: **Azure AI User** on your Foundry project

---

## Setup & First Run

### 1. Clone and restore

```bash
git clone <your-repo>
cd EmailTriageAgent
dotnet restore
```

### 2. Configure

```bash
cp .env.example .env
# Edit .env with your PROJECT_ENDPOINT and MODEL_DEPLOYMENT_NAME
```

### 3. Authenticate

```bash
az login
```

### 4. Bootstrap — create the agent in Azure (run ONCE)

```bash
dotnet run -- bootstrap
```

This creates the agent in your Azure AI Foundry project and prints an agent ID.
Copy that ID into your `.env` file:

```
AGENT_ID=asst_abc123xyz
```

### 5. Run a triage session

```bash
dotnet run
```

---

## How It Works

### The Agent Loop

```
User Message
     │
     ▼
┌─────────────────────────────────────────────────┐
│              Azure AI Foundry                   │
│                                                 │
│  Thread (conversation) ──► Run (execution)      │
│                                                 │
│  Status: Queued                                 │
│       → InProgress                              │
│       → RequiresAction  ──► Your App            │
│             │               Executes Tool       │
│             │               Returns Result      │
│             ◄──────────────────────────────     │
│       → InProgress (continues)                  │
│       → Completed                               │
└─────────────────────────────────────────────────┘
     │
     ▼
  TriageSession result
```

### Tool Flow for This Agent

```
fetch_unread_emails        (reads Gmail)
        │
        ▼ (for each email)
record_triage_decision     (classifies urgency)
        │
        ├── if Critical/High ──► create_draft_reply    (saves Gmail draft)
        │
        └── if needs focus  ──► create_calendar_block  (adds Calendar event)
```

---


## Extending This Project

Good next steps for learning:

1. **Add a Slack notification tool** — post the triage summary to a channel
2. **Persist results to Cosmos DB** — treat each `TriageSession` as an event
3. **Add a web UI** — wrap the runner in an ASP.NET Core minimal API
