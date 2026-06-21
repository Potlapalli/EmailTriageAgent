using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using MimeKit;
using System.Text;
using System.Text.Json;
using EmailTriageAgent.Models;

namespace EmailTriageAgent.Services;

/// <summary>
/// Wraps the real Gmail API.
///
/// Authentication flow:
///   First run  → opens browser for Google OAuth2 consent → saves token to disk
///   Later runs → loads saved token silently (no browser needed)
///
/// Scopes requested:
///   GmailReadonly  → read + list messages
///   GmailModify    → mark as read, create drafts
/// </summary>
public class GmailApiService
{
    private readonly GmailService _gmail;
    private const string UserId = "me"; // "me" = authenticated user

    private GmailApiService(GmailService gmail)
    {
        _gmail = gmail;
    }

    // ─── Factory: authenticate and build the service ──────────────────────────

    public static async Task<GmailApiService> CreateAsync(
        string clientSecretPath,
        string tokenStorePath = "token_store")
    {
        // Load OAuth2 credentials from the downloaded JSON file
        await using var stream = new FileStream(clientSecretPath, FileMode.Open, FileAccess.Read);

        // GoogleWebAuthorizationBroker opens the browser on first run,
        // then caches the token in tokenStorePath for all future runs
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecretsStream: stream,
            scopes: new[]
            {
                GmailService.Scope.GmailReadonly,   // list + read emails
                GmailService.Scope.GmailModify      // mark read, create drafts
            },
            user:              "user",
            taskCancellationToken: CancellationToken.None,
            dataStore:         new FileDataStore(tokenStorePath, fullPath: false)
        );

        var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "EmailTriageAgent"
        });

        Console.WriteLine("[Gmail] Authenticated successfully.");
        return new GmailApiService(gmail);
    }

    // ─── Tool: fetch_unread_emails ────────────────────────────────────────────

    public async Task<string> FetchUnreadEmailsAsync(int maxResults = 3, string label = "INBOX")
    {
        Console.WriteLine($"  [Gmail] Fetching up to {maxResults} unread emails from {label}...");

        // Step 1: list unread message IDs
        var listRequest = _gmail.Users.Messages.List(UserId);
        listRequest.Q           = "is:unread";      // Gmail search query
        listRequest.LabelIds    = label;
        listRequest.MaxResults  = maxResults;

        var listResponse = await listRequest.ExecuteAsync();

        if (listResponse.Messages == null || !listResponse.Messages.Any())
        {
            Console.WriteLine("  [Gmail] No unread emails found.");
            return JsonSerializer.Serialize(Array.Empty<object>());
        }

        // Step 2: fetch full message for each ID
        var emails = new List<object>();
        foreach (var msg in listResponse.Messages)
        {
            var fullMsg = await _gmail.Users.Messages
                .Get(UserId, msg.Id)
                .ExecuteAsync();

            var subject    = GetHeader(fullMsg, "Subject");
            var from       = GetHeader(fullMsg, "From");
            var date       = GetHeader(fullMsg, "Date");
            var bodySnippet = fullMsg.Snippet ?? "";

            // For longer body, decode the payload parts
            var fullBody = ExtractBody(fullMsg);

            emails.Add(new
            {
                id          = fullMsg.Id,
                from,
                subject,
                body        = fullBody,
                received_at = date
            });
        }

        Console.WriteLine($"  [Gmail] Fetched {emails.Count} emails.");
        return JsonSerializer.Serialize(emails);
    }

    // ─── Tool: mark_email_as_read ─────────────────────────────────────────────

    public async Task<string> MarkEmailAsReadAsync(string emailId)
    {
        Console.WriteLine($"  [Gmail] Marking email {emailId} as read...");

        var request = new ModifyMessageRequest
        {
            RemoveLabelIds = new List<string> { "UNREAD" }
        };

        await _gmail.Users.Messages.Modify(request, UserId, emailId).ExecuteAsync();

        return JsonSerializer.Serialize(new { success = true, email_id = emailId, message = "Marked as read" });
    }

    // ─── Tool: create_draft_reply ─────────────────────────────────────────────

    public async Task<string> CreateDraftReplyAsync(string emailId, string replyBody)
    {
        Console.WriteLine($"  [Gmail] Creating draft reply for email {emailId}...");

        // Fetch the original email to extract To/Subject for threading
        var original  = await _gmail.Users.Messages.Get(UserId, emailId).ExecuteAsync();
        var toAddress  = GetHeader(original, "From");    // reply goes back to the sender
        var subject    = GetHeader(original, "Subject");
        var messageId  = GetHeader(original, "Message-Id");
        var references = GetHeader(original, "References");

        // Build a proper MIME message for threading
        var mimeMessage = new MimeMessage();
        mimeMessage.To.Add(MailboxAddress.Parse(toAddress));
        mimeMessage.Subject = subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? subject
            : $"Re: {subject}";

        // Set threading headers so Gmail groups it with the original thread
        if (!string.IsNullOrEmpty(messageId))
            mimeMessage.InReplyTo = messageId;
        if (!string.IsNullOrEmpty(references))
            mimeMessage.References.Add(references);

        mimeMessage.Body = new TextPart("plain") { Text = replyBody };

        // Encode to RFC 2822 format → base64url (required by Gmail API)
        using var memStream = new MemoryStream();
        await mimeMessage.WriteToAsync(memStream);
        var rawMessage = Convert.ToBase64String(memStream.ToArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var draft = new Draft
        {
            Message = new Message
            {
                Raw      = rawMessage,
                ThreadId = original.ThreadId   // keeps it in the same thread
            }
        };

        var created = await _gmail.Users.Drafts.Create(draft, UserId).ExecuteAsync();

        Console.WriteLine($"  [Gmail] Draft created: {created.Id}");
        return JsonSerializer.Serialize(new
        {
            success  = true,
            draft_id = created.Id,
            email_id = emailId
        });
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Extracts a header value from a Gmail message by name.</summary>
    private static string GetHeader(Message message, string name)
    {
        return message.Payload?.Headers?
            .FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? string.Empty;
    }

    /// <summary>
    /// Decodes the email body from the Gmail API payload.
    /// Gmail nests body parts differently for plain vs multipart messages.
    /// </summary>
    private static string ExtractBody(Message message)
    {
        var payload = message.Payload;
        if (payload == null) return string.Empty;

        // Simple plain text message
        if (payload.Body?.Data != null)
            return DecodeBase64Url(payload.Body.Data);

        // Multipart message — find the text/plain part
        if (payload.Parts != null)
        {
            var textPart = payload.Parts
                .FirstOrDefault(p => p.MimeType == "text/plain");

            if (textPart?.Body?.Data != null)
                return DecodeBase64Url(textPart.Body.Data);

            // Nested multipart (e.g. text/html only)
            var htmlPart = payload.Parts
                .FirstOrDefault(p => p.MimeType == "text/html");

            if (htmlPart?.Body?.Data != null)
                return $"[HTML email] {message.Snippet}";
        }

        // Fallback to snippet
        return message.Snippet ?? string.Empty;
    }

    /// <summary>Gmail API encodes body as base64url — decode it to UTF-8 string.</summary>
    private static string DecodeBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        // Pad to multiple of 4
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }
}
