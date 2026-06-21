using System.Text.Json;
using EmailTriageAgent.Models;
using EmailTriageAgent.Services;

namespace EmailTriageAgent.Tools;

/// <summary>
/// Executes the actual logic for each tool the agent can call.
/// Now fully wired — Gmail for email ops, Google Calendar for blocks.
/// </summary>
public class ToolHandlers
{
    private readonly GmailApiService _gmail;
    private readonly GoogleCalendarService _calendar;
    private readonly List<TriageResult> _triageResults = new();
    private readonly List<CalendarBlock> _calendarBlocks = new();
    private readonly Dictionary<string, Email> _emailCache = new();

    public ToolHandlers(GmailApiService gmail, GoogleCalendarService calendar)
    {
        _gmail = gmail;
        _calendar = calendar;
    }

    public IReadOnlyList<TriageResult> TriageResults => _triageResults;
    public IReadOnlyList<CalendarBlock> CalendarBlocks => _calendarBlocks;

    // ─── Tool: fetch_unread_emails ─────────────────────────────────────────

    public async Task<string> FetchUnreadEmailsAsync(int maxResults = 10, string label = "INBOX")
    {
        var json = await _gmail.FetchUnreadEmailsAsync(maxResults, label);
        var emails = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? new();

        foreach (var e in emails)
        {
            var id = e.GetProperty("id").GetString()!;
            var from = e.GetProperty("from").GetString()!;
            var subject = e.GetProperty("subject").GetString()!;
            var body = e.GetProperty("body").GetString()!;
            var recvAt = e.GetProperty("received_at").GetString()!;
            _emailCache[id] = new Email(id, from, subject, body, recvAt);
        }

        return json;
    }

    // ─── Tool: mark_email_as_read ──────────────────────────────────────────

    public async Task<string> MarkEmailAsReadAsync(string emailId)
        => await _gmail.MarkEmailAsReadAsync(emailId);

    // ─── Tool: create_draft_reply ──────────────────────────────────────────

    public async Task<string> CreateDraftReplyAsync(string emailId, string replyBody)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [Tool] create_draft_reply → {emailId}");
        Console.WriteLine($"         Preview: \"{replyBody[..Math.Min(80, replyBody.Length)]}...\"");
        Console.ResetColor();

        return await _gmail.CreateDraftReplyAsync(emailId, replyBody);
    }

    // ─── Tool: create_calendar_block ──────────────────────────────────────
    // ✅ Now wired to real Google Calendar API

    public async Task<string> CreateCalendarBlockAsync(
        string title, string description, string startTime, string endTime)
    {
        Console.WriteLine($"  [Tool] create_calendar_block → \"{title}\"");

        // ✅ Real Google Calendar API call
        var result = await _calendar.CreateCalendarBlockAsync(title, description, startTime, endTime);

        // Also record locally for the session summary
        _calendarBlocks.Add(new CalendarBlock(
            EmailId: "unknown",
            Title: title,
            Description: description,
            StartTime: startTime,
            EndTime: endTime
        ));

        return result;
    }

    // ─── Tool: record_triage_decision ─────────────────────────────────────

    public Task<string> RecordTriageDecisionAsync(
        string emailId, string urgency, string reason, bool shouldBlockCalendar)
    {
        Console.WriteLine($"  [Tool] record_triage_decision → {emailId} [{urgency}]");

        var email = _emailCache.GetValueOrDefault(emailId);
        var level = Enum.Parse<UrgencyLevel>(urgency, ignoreCase: true);

        if (email is not null)
        {
            _triageResults.Add(new TriageResult(
                Email: email,
                Urgency: level,
                Reason: reason,
                DraftReply: null,
                ShouldBlock: shouldBlockCalendar
            ));
        }

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            success = true,
            email_id = emailId,
            urgency,
            recorded = true
        }));
    }

    // ─── Tool dispatcher ──────────────────────────────────────────────────

    public async Task<string> DispatchAsync(string toolName, string argumentsJson)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;

        return toolName switch
        {
            "fetch_unread_emails" => await FetchUnreadEmailsAsync(
                maxResults: args.TryGetProperty("max_results", out var mr) ? mr.GetInt32() : 10,
                label: args.TryGetProperty("label", out var lb) ? lb.GetString()! : "INBOX"
            ),

            "mark_email_as_read" => await MarkEmailAsReadAsync(
                emailId: args.GetProperty("email_id").GetString()!
            ),

            "create_draft_reply" => await CreateDraftReplyAsync(
                emailId: args.GetProperty("email_id").GetString()!,
                replyBody: args.GetProperty("reply_body").GetString()!
            ),

            "create_calendar_block" => await CreateCalendarBlockAsync(
                title: args.GetProperty("title").GetString()!,
                description: args.GetProperty("description").GetString()!,
                startTime: args.GetProperty("start_time").GetString()!,
                endTime: args.GetProperty("end_time").GetString()!
            ),

            "record_triage_decision" => await RecordTriageDecisionAsync(
                emailId: args.GetProperty("email_id").GetString()!,
                urgency: args.GetProperty("urgency").GetString()!,
                reason: args.GetProperty("reason").GetString()!,
                shouldBlockCalendar: args.GetProperty("should_block_calendar").GetBoolean()
            ),

            _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" })
        };
    }
}
