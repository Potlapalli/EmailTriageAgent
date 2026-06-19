using EmailTriageAgent.Models;

namespace EmailTriageAgent.Services;

/// <summary>
/// Renders the triage session results to the console.
/// In production this could write to a dashboard, database, or send a Slack summary.
/// </summary>
public static class ResultsDisplay
{
    public static void PrintSession(TriageSession session)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           EMAIL TRIAGE SESSION COMPLETE                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine($"  Thread ID  : {session.ThreadId}");
        Console.WriteLine($"  Triaged At : {session.TriagedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  Emails     : {session.Results.Count} processed");
        Console.WriteLine($"  Cal Blocks : {session.CalendarBlocks.Count} created");
        Console.WriteLine();

        // ── Urgency breakdown ─────────────────────────────────────────────────
        PrintSectionHeader("URGENCY BREAKDOWN");

        var groups = session.Results
            .GroupBy(r => r.Urgency)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var color = group.Key switch
            {
                UrgencyLevel.Critical => ConsoleColor.Red,
                UrgencyLevel.High     => ConsoleColor.Yellow,
                UrgencyLevel.Medium   => ConsoleColor.Cyan,
                UrgencyLevel.Low      => ConsoleColor.DarkGray,
                _                     => ConsoleColor.White
            };

            Console.ForegroundColor = color;
            Console.WriteLine($"  [{group.Key,-8}] {group.Count()} email(s)");
            Console.ResetColor();
        }

        Console.WriteLine();

        // ── Per-email detail ──────────────────────────────────────────────────
        PrintSectionHeader("EMAIL DETAILS");

        foreach (var result in session.Results.OrderBy(r => r.Urgency))
        {
            var badge = result.Urgency switch
            {
                UrgencyLevel.Critical => "🔴 CRITICAL",
                UrgencyLevel.High     => "🟡 HIGH    ",
                UrgencyLevel.Medium   => "🔵 MEDIUM  ",
                UrgencyLevel.Low      => "⚪ LOW     ",
                _                     => "❓ UNKNOWN "
            };

            Console.WriteLine($"  {badge} | From: {result.Email.From}");
            Console.WriteLine($"           Subject: {result.Email.Subject}");
            Console.WriteLine($"           Reason : {result.Reason}");

            if (result.DraftReply is not null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"           ✓ Draft reply created");
                Console.ResetColor();
            }

            if (result.ShouldBlock)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"           📅 Calendar block scheduled");
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        // ── Calendar blocks ───────────────────────────────────────────────────
        if (session.CalendarBlocks.Any())
        {
            PrintSectionHeader("CALENDAR BLOCKS CREATED");

            foreach (var block in session.CalendarBlocks)
            {
                Console.WriteLine($"  📅 {block.Title}");
                Console.WriteLine($"     {block.StartTime} → {block.EndTime}");
                Console.WriteLine($"     {block.Description[..Math.Min(80, block.Description.Length)]}...");
                Console.WriteLine();
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ─────────────────────────────────────────────────────────");
        Console.WriteLine("  All drafts saved in Gmail. No emails were sent automatically.");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintSectionHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"  ── {title} " + new string('─', Math.Max(0, 50 - title.Length)));
        Console.ResetColor();
    }
}
