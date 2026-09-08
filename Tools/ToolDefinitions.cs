using Azure.AI.Agents.Persistent;

namespace EmailTriageAgent.Tools;

/// <summary>
/// Central registry of all tool definitions.
/// 
/// PATTERN: Keep tool *definitions* (JSON schema) separate from tool *handlers*
/// (actual execution logic). This mirrors CQRS — the schema is the command spec,
/// the handler is the command processor.
/// </summary>
public static class ToolDefinitions
{
    // ─── Gmail Tools ──────────────────────────────────────────────────────────

    /// <summary>Fetches unread emails from the Gmail inbox.</summary>
    public static FunctionToolDefinition FetchUnreadEmails => new(
        name: "fetch_unread_emails",
        description: "Fetch unread emails from Gmail inbox. Returns a list of emails with id, from, subject, body snippet, and received timestamp.",
        parameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "max_results": {
                    "type": "integer",
                    "description": "Maximum number of unread emails to fetch. Defaults to 3.",
                    "default": 3
                },
                "label": {
                    "type": "string",
                    "description": "Gmail label to filter by, e.g. INBOX, IMPORTANT. Defaults to INBOX.",
                    "default": "INBOX"
                }
            },
            "required": []
        }
        """)
    );

    /// <summary>Marks a specific email as read.</summary>
    public static FunctionToolDefinition MarkAsRead => new(
        name: "mark_email_as_read",
        description: "Mark a specific Gmail email as read by its message ID.",
        parameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "email_id": {
                    "type": "string",
                    "description": "The Gmail message ID to mark as read."
                }
            },
            "required": ["email_id"]
        }
        """)
    );

    /// <summary>Creates a draft reply in Gmail.</summary>
    public static FunctionToolDefinition CreateDraftReply => new(
        name: "create_draft_reply",
        description: "Create a draft reply in Gmail for a given email. Does NOT send — only saves as draft for human review.",
        parameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "email_id": {
                    "type": "string",
                    "description": "The Gmail message ID to reply to."
                },
                "reply_body": {
                    "type": "string",
                    "description": "The body text of the draft reply."
                }
            },
            "required": ["email_id", "reply_body"]
        }
        """)
    );

    // ─── Google Calendar Tools ─────────────────────────────────────────────────

    /// <summary>Creates a focused work block on Google Calendar.</summary>
    public static FunctionToolDefinition CreateCalendarBlock => new(
        name: "create_calendar_block",
        description: "Create a focused work block on Google Calendar to handle a specific email follow-up. Use for Critical or High urgency emails that require dedicated time.",
        parameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "email_id": {
                    "type": "string",
                    "description": "The Gmail message ID this calendar block follows up on. Used to prevent duplicate blocks on retry."
                },
                "title": {
                    "type": "string",
                    "description": "Event title, e.g. 'Follow up: Project Deadline Email'"
                },
                "description": {
                    "type": "string",
                    "description": "Event description with context from the email."
                },
                "start_time": {
                    "type": "string",
                    "description": "Start time in ISO 8601 format, e.g. 2026-06-19T09:00:00+05:30"
                },
                "end_time": {
                    "type": "string",
                    "description": "End time in ISO 8601 format."
                }
            },
            "required": ["email_id", "title", "description", "start_time", "end_time"]
        }
        """)
    );

    // ─── Triage Tool ──────────────────────────────────────────────────────────

    /// <summary>
    /// The agent calls this to record its triage decision for one email.
    /// This is the agent's "write side" — it emits a structured triage result
    /// that the application can persist or display.
    /// </summary>
    public static FunctionToolDefinition RecordTriageDecision => new(
        name: "record_triage_decision",
        description: "Record the triage decision for a specific email. Call this once per email after analysing it.",
        parameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "email_id": {
                    "type": "string",
                    "description": "The Gmail message ID being triaged."
                },
                "urgency": {
                    "type": "string",
                    "enum": ["Critical", "High", "Medium", "Low"],
                    "description": "Urgency classification for this email."
                },
                "reason": {
                    "type": "string",
                    "description": "One sentence explaining the urgency classification."
                },
                "should_block_calendar": {
                    "type": "boolean",
                    "description": "Whether a calendar focus block should be created for this email."
                }
            },
            "required": ["email_id", "urgency", "reason", "should_block_calendar"]
        }
        """)
    );

    /// <summary>Returns all tool definitions as a list for agent registration.</summary>
    public static List<ToolDefinition> All => new()
    {
        FetchUnreadEmails,
        MarkAsRead,
        CreateDraftReply,
        CreateCalendarBlock,
        RecordTriageDecision
    };
}