using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Text.Json;

namespace EmailTriageAgent.Services;

/// <summary>
/// Wraps the real Google Calendar API.
///
/// IMPORTANT: Uses the SAME client_secret.json as GmailService.
/// IMPORTANT: Uses a SEPARATE token store (calendar_token/) so Gmail
///            and Calendar OAuth tokens don't overwrite each other.
///
/// Scope requested:
///   CalendarEvents → create/read/update events on primary calendar
/// </summary>
public class GoogleCalendarService
{
    private readonly CalendarService _calendar;
    private const string CalendarId = "primary";

    private GoogleCalendarService(CalendarService calendar)
    {
        _calendar = calendar;
    }

    // ─── Factory: authenticate and build the service ──────────────────────────

    public static async Task<GoogleCalendarService> CreateAsync(
        string clientSecretPath,
        string tokenStorePath = "calendar_token")
    {
        await using var stream = new FileStream(clientSecretPath, FileMode.Open, FileAccess.Read);

        // Separate token store from Gmail so they don't conflict
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecretsStream:   stream,
            scopes: new[]
            {
                CalendarService.Scope.CalendarEvents  // create + read events
            },
            user:                  "user",
            taskCancellationToken: CancellationToken.None,
            dataStore:             new FileDataStore(tokenStorePath, fullPath: false)
        );

        var calendar = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "EmailTriageAgent"
        });

        Console.WriteLine("[Calendar] Authenticated successfully.");
        return new GoogleCalendarService(calendar);
    }

    // ─── Tool: create_calendar_block ──────────────────────────────────────────

    public async Task<string> CreateCalendarBlockAsync(
        string title,
        string description,
        string startTime,   // ISO 8601 e.g. "2026-06-22T09:00:00"
        string endTime)     // ISO 8601 e.g. "2026-06-22T09:30:00"
    {
        Console.WriteLine($"  [Calendar] Creating event: \"{title}\"");

        // Parse the times — agent sends ISO 8601 strings
        // If parsing fails, default to next business day 9-9:30am
        var start = TryParseDateTime(startTime) ?? NextBusinessDayMorning(9, 0);
        var end   = TryParseDateTime(endTime)   ?? NextBusinessDayMorning(9, 30);

        var calEvent = new Event
        {
            Summary     = title,
            Description = description,

            Start = new EventDateTime
            {
                DateTime = start,
                TimeZone = GetLocalTimeZoneId()
            },
            End = new EventDateTime
            {
                DateTime = end,
                TimeZone = GetLocalTimeZoneId()
            },

            // Mark as focus time so Google Calendar shows it differently
            Transparency = "opaque",   // "busy" block

            // Add a reminder 10 minutes before
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides  = new List<EventReminder>
                {
                    new EventReminder { Method = "popup", Minutes = 10 }
                }
            }
        };

        var created = await _calendar.Events
            .Insert(calEvent, CalendarId)
            .ExecuteAsync();

        Console.WriteLine($"  [Calendar] Event created: {created.Id}");
        Console.WriteLine($"  [Calendar] Link: {created.HtmlLink}");

        return JsonSerializer.Serialize(new
        {
            success    = true,
            event_id   = created.Id,
            title      = created.Summary,
            start_time = created.Start.DateTime?.ToString("o"),
            end_time   = created.End.DateTime?.ToString("o"),
            link       = created.HtmlLink
        });
    }

    // ─── Tool: list upcoming events (bonus — useful for checking conflicts) ───

    public async Task<string> GetUpcomingEventsAsync(int maxResults = 5)
    {
        Console.WriteLine($"  [Calendar] Fetching next {maxResults} events...");

        var request = _calendar.Events.List(CalendarId);
        request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow;
        request.MaxResults            = maxResults;
        request.SingleEvents          = true;
        request.OrderBy               = EventsResource.ListRequest.OrderByEnum.StartTime;

        var result = await request.ExecuteAsync();

        var events = result.Items?.Select(e => new
        {
            id      = e.Id,
            title   = e.Summary,
            start   = e.Start?.DateTime?.ToString("o") ?? e.Start?.Date,
            end     = e.End?.DateTime?.ToString("o")   ?? e.End?.Date
        }) ?? Enumerable.Empty<object>();

        return JsonSerializer.Serialize(events);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static DateTime? TryParseDateTime(string value)
    {
        if (DateTime.TryParse(value, out var dt))
            return dt;
        return null;
    }

    /// <summary>
    /// Returns next Monday–Friday at the given hour:minute in local time.
    /// Used as a safe fallback when the agent sends an unparseable datetime.
    /// </summary>
    private static DateTime NextBusinessDayMorning(int hour, int minute)
    {
        var date = DateTime.Today.AddDays(1);
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            date = date.AddDays(1);
        return date.AddHours(hour).AddMinutes(minute);
    }

    /// <summary>
    /// Returns a Google Calendar-compatible timezone ID for the local machine.
    /// Falls back to UTC if mapping isn't found.
    /// </summary>
    private static string GetLocalTimeZoneId()
    {
        var local = TimeZoneInfo.Local;

        // Windows uses different IDs than IANA — Google Calendar needs IANA
        // Try to convert Windows ID to IANA (works on .NET 6+)
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var ianaId))
            return ianaId;

        // Already IANA (Linux/macOS) or fallback
        return local.Id.Contains('/') ? local.Id : "UTC";
    }
}
