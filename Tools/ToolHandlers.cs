using System.Text.Json;
using EmailTriageAgent.Models;

namespace EmailTriageAgent.Tools;

/// <summary>
/// Executes the actual logic for each tool the agent can call.
///
/// PATTERN: This is the "command handler" layer — analogous to the write side
/// in CQRS. Each method receives parsed arguments and returns a JSON string
/// that gets fed back to the agent as the tool result.
///
/// In production these would call real APIs (Gmail, Google Calendar).
/// For learning/development, they return realistic simulated data.
/// Swap out the method bodies when you wire up real credentials.
/// </summary>
public class ToolHandlers
{
    // Collected triage decisions emitted by the agent during the session
    private readonly List<TriageResult>   _triageResults   = new();
    private readonly List<CalendarBlock>  _calendarBlocks  = new();

    // Email cache — populated when fetch_unread_emails is first called
    private readonly Dictionary<string, Email> _emailCache = new();

    // ─── Read-only access for the session runner ───────────────────────────

    public IReadOnlyList<TriageResult>   TriageResults   => _triageResults;
    public IReadOnlyList<CalendarBlock>  CalendarBlocks  => _calendarBlocks;

    // ─── Tool: fetch_unread_emails ─────────────────────────────────────────

    public string FetchUnreadEmails(int maxResults = 10, string label = "INBOX")
    {
        Console.WriteLine($"  [Tool] fetch_unread_emails(max={maxResults}, label={label})");

        // ── PRODUCTION: Replace this block with real Gmail API call ──────────
        // var gmailService = new GmailService(...);
        // var request = gmailService.Users.Messages.List("me");
        // request.LabelIds = label; request.Q = "is:unread";
        // var result = await request.ExecuteAsync();
        // ── END PRODUCTION ───────────────────────────────────────────────────

        // Simulated realistic email data for development/learning
        var emails = new[]
        {
            new
            {
                id          = "msg_001",
                from        = "cto@company.com",
                subject     = "URGENT: Production outage — payment service down",
                body        = "We have a P0 incident. The payment service has been down for 20 minutes. All hands needed. Please join the war room immediately: meet.google.com/xyz",
                received_at = DateTime.UtcNow.AddMinutes(-15).ToString("o")
            },
            new
            {
                id          = "msg_002",
                from        = "pm@company.com",
                subject     = "Q3 roadmap review — your input needed by EOD",
                body        = "Hey, we're finalising the Q3 roadmap. Could you review the attached doc and add your team's capacity? Deadline is 5pm today so we can present tomorrow.",
                received_at = DateTime.UtcNow.AddHours(-2).ToString("o")
            },
            new
            {
                id          = "msg_003",
                from        = "recruiter@talent.io",
                subject     = "Exciting Senior Engineer opportunity at fintech startup",
                body        = "Hi, I came across your profile and think you'd be a great fit for a Series B fintech startup. They're offering competitive comp. Would you be open to a quick call?",
                received_at = DateTime.UtcNow.AddHours(-3).ToString("o")
            },
            new
            {
                id          = "msg_004",
                from        = "alice@company.com",
                subject     = "PR review request: CQRS event store refactor",
                body        = "Hey, I've pushed the CQRS event store refactor we discussed. It's a big change so would really appreciate your review before I merge. No rush but ideally this week.",
                received_at = DateTime.UtcNow.AddHours(-5).ToString("o")
            },
            new
            {
                id          = "msg_005",
                from        = "noreply@newsletter.dev",
                subject     = "This week in .NET — Azure updates, C# 13 features",
                body        = "Welcome to this week's .NET digest! Top stories: Azure Functions v4 improvements, C# 13 params collections, new Aspire workload updates...",
                received_at = DateTime.UtcNow.AddHours(-6).ToString("o")
            }
        };

        // Cache emails for later tool calls
        foreach (var e in emails)
            _emailCache[e.id] = new Email(e.id, e.from, e.subject, e.body, e.received_at);

        return JsonSerializer.Serialize(emails.Take(maxResults));
    }

    // ─── Tool: mark_email_as_read ──────────────────────────────────────────

    public string MarkEmailAsRead(string emailId)
    {
        Console.WriteLine($"  [Tool] mark_email_as_read(id={emailId})");

        // ── PRODUCTION: Replace with Gmail API modify call ───────────────────
        // await gmailService.Users.Messages.Modify(new ModifyMessageRequest
        // {
        //     RemoveLabelIds = new[] { "UNREAD" }
        // }, "me", emailId).ExecuteAsync();
        // ── END PRODUCTION ───────────────────────────────────────────────────

        return JsonSerializer.Serialize(new { success = true, email_id = emailId, message = "Marked as read" });
    }

    // ─── Tool: create_draft_reply ──────────────────────────────────────────

    public string CreateDraftReply(string emailId, string replyBody)
    {
        Console.WriteLine($"  [Tool] create_draft_reply(id={emailId})");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"         Draft preview: \"{replyBody[..Math.Min(80, replyBody.Length)]}...\"");
        Console.ResetColor();

        // ── PRODUCTION: Replace with Gmail drafts.create API call ────────────
        // var draft = new Draft { Message = new Message { ... } };
        // await gmailService.Users.Drafts.Create(draft, "me").ExecuteAsync();
        // ── END PRODUCTION ───────────────────────────────────────────────────

        var draftId = $"draft_{Guid.NewGuid().ToString()[..8]}";
        return JsonSerializer.Serialize(new { success = true, draft_id = draftId, email_id = emailId });
    }

    // ─── Tool: create_calendar_block ──────────────────────────────────────

    public string CreateCalendarBlock(string title, string description, string startTime, string endTime)
    {
        Console.WriteLine($"  [Tool] create_calendar_block(title=\"{title}\")");

        // ── PRODUCTION: Replace with Google Calendar events.insert ───────────
        // var calEvent = new Event { Summary = title, Description = description,
        //     Start = new EventDateTime { DateTime = DateTime.Parse(startTime) },
        //     End   = new EventDateTime { DateTime = DateTime.Parse(endTime) } };
        // await calendarService.Events.Insert(calEvent, "primary").ExecuteAsync();
        // ── END PRODUCTION ───────────────────────────────────────────────────

        var eventId = $"evt_{Guid.NewGuid().ToString()[..8]}";
        _calendarBlocks.Add(new CalendarBlock(
            EmailId:     "unknown", // linked in record_triage_decision
            Title:       title,
            Description: description,
            StartTime:   startTime,
            EndTime:     endTime
        ));

        return JsonSerializer.Serialize(new { success = true, event_id = eventId, title, start_time = startTime });
    }

    // ─── Tool: record_triage_decision ─────────────────────────────────────

    public string RecordTriageDecision(
        string emailId,
        string urgency,
        string reason,
        bool   shouldBlockCalendar)
    {
        Console.WriteLine($"  [Tool] record_triage_decision(id={emailId}, urgency={urgency})");

        var email   = _emailCache.GetValueOrDefault(emailId);
        var level   = Enum.Parse<UrgencyLevel>(urgency, ignoreCase: true);

        if (email is not null)
        {
            _triageResults.Add(new TriageResult(
                Email:       email,
                Urgency:     level,
                Reason:      reason,
                DraftReply:  null,   // populated separately via create_draft_reply
                ShouldBlock: shouldBlockCalendar
            ));
        }

        return JsonSerializer.Serialize(new { success = true, email_id = emailId, urgency, recorded = true });
    }

    // ─── Tool dispatcher ──────────────────────────────────────────────────

    /// <summary>
    /// Routes a tool call from the agent to the correct handler.
    /// Called by the agent run loop whenever RunStatus == RequiresAction.
    /// </summary>
    public string Dispatch(string toolName, string argumentsJson)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;

        return toolName switch
        {
            "fetch_unread_emails" => FetchUnreadEmails(
                maxResults: args.TryGetProperty("max_results", out var mr) ? mr.GetInt32() : 10,
                label:      args.TryGetProperty("label",       out var lb) ? lb.GetString()! : "INBOX"
            ),

            "mark_email_as_read" => MarkEmailAsRead(
                emailId: args.GetProperty("email_id").GetString()!
            ),

            "create_draft_reply" => CreateDraftReply(
                emailId:   args.GetProperty("email_id").GetString()!,
                replyBody: args.GetProperty("reply_body").GetString()!
            ),

            "create_calendar_block" => CreateCalendarBlock(
                title:       args.GetProperty("title").GetString()!,
                description: args.GetProperty("description").GetString()!,
                startTime:   args.GetProperty("start_time").GetString()!,
                endTime:     args.GetProperty("end_time").GetString()!
            ),

            "record_triage_decision" => RecordTriageDecision(
                emailId:              args.GetProperty("email_id").GetString()!,
                urgency:              args.GetProperty("urgency").GetString()!,
                reason:               args.GetProperty("reason").GetString()!,
                shouldBlockCalendar:  args.GetProperty("should_block_calendar").GetBoolean()
            ),

            _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" })
        };
    }
}
