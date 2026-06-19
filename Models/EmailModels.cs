namespace EmailTriageAgent.Models;

// ─── Urgency levels the agent assigns to each email ───────────────────────────

public enum UrgencyLevel
{
    Critical,   // Needs response within the hour
    High,       // Needs response today
    Medium,     // Can wait 1–2 days
    Low         // FYI / newsletter / no action needed
}

// ─── Raw email as returned by Gmail tool ──────────────────────────────────────

public record Email(
    string Id,
    string From,
    string Subject,
    string Body,
    string ReceivedAt
);

// ─── Agent's triage decision for one email ────────────────────────────────────

public record TriageResult(
    Email Email,
    UrgencyLevel Urgency,
    string Reason,          // One-line explanation of urgency decision
    string? DraftReply,     // Populated for Critical / High emails
    bool   ShouldBlock      // True if a calendar block is recommended
);

// ─── Calendar block request produced by the agent ─────────────────────────────

public record CalendarBlock(
    string EmailId,
    string Title,
    string Description,
    string StartTime,       // ISO 8601
    string EndTime          // ISO 8601
);

// ─── Full triage session output ───────────────────────────────────────────────

public record TriageSession(
    string              ThreadId,
    List<TriageResult>  Results,
    List<CalendarBlock> CalendarBlocks,
    DateTime            TriagedAt
);
