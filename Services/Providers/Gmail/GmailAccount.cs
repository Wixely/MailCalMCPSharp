using System.Text;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util;
using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Auth;
using MailCalMCPSharp.Services.Models;
using MimeKit;
using CalData = Google.Apis.Calendar.v3.Data;
using GmailData = Google.Apis.Gmail.v1.Data;

namespace MailCalMCPSharp.Services.Providers.Gmail;

/// <summary>Gmail account backed by the Google Gmail + Calendar API client libraries.</summary>
public sealed class GmailAccount : IMailCalAccount, IMailProvider, ICalendarProvider, IContactsProvider
{
    private const string Me = "me";
    private static readonly string[] SummaryHeaders = { "Subject", "From", "To", "Cc", "Date" };

    private readonly AccountEntry _entry;
    private readonly GmailAuthenticator _authenticator;
    private readonly MailCalOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private GmailService? _gmail;
    private CalendarService? _calendar;

    public GmailAccount(AccountEntry entry, GmailAuthenticator authenticator, MailCalOptions options)
    {
        _entry = entry;
        _authenticator = authenticator;
        _options = options;
    }

    public string Alias => _entry.Alias;
    public MailCalProvider Provider => MailCalProvider.Gmail;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        MailSearch = true,
        ScheduledSend = false,  // Gmail send-later is not exposed by the API
        MailRules = true,
        CalendarTextSearch = true,
        Contacts = true,
        PermanentDelete = true,
    };

    public IMailProvider Mail => this;
    public ICalendarProvider Calendar => this;
    public IContactsProvider Contacts => this;

    private async Task<GmailService> GmailAsync(CancellationToken ct)
    {
        if (_gmail is not null)
        {
            return _gmail;
        }
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_gmail is null)
            {
                var cred = await _authenticator.GetCredentialAsync(ct).ConfigureAwait(false);
                _gmail = new GmailService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = cred,
                    ApplicationName = "MailCalMCPSharp",
                });
            }
            return _gmail;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<CalendarService> CalendarAsync(CancellationToken ct)
    {
        if (_calendar is not null)
        {
            return _calendar;
        }
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_calendar is null)
            {
                var cred = await _authenticator.GetCredentialAsync(ct).ConfigureAwait(false);
                _calendar = new CalendarService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = cred,
                    ApplicationName = "MailCalMCPSharp",
                });
            }
            return _calendar;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ---------------- Mail ----------------

    public async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken ct)
    {
        var svc = await GmailAsync(ct).ConfigureAwait(false);
        var resp = await svc.Users.Labels.List(Me).ExecuteAsync(ct).ConfigureAwait(false);
        return resp.Labels?.Select(l => new MailFolder(l.Id, l.Name)).ToList() ?? new List<MailFolder>();
    }

    public async Task<EmailMessage> ReadAsync(string messageId, int maxBodyChars, CancellationToken ct)
    {
        var svc = await GmailAsync(ct).ConfigureAwait(false);
        var req = svc.Users.Messages.Get(Me, messageId);
        req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var m = await req.ExecuteAsync(ct).ConfigureAwait(false);
        return MapFull(m, maxBodyChars);
    }

    public async Task<MailPage> ListAsync(string? folderId, string? pageToken, int pageSize, CancellationToken ct)
    {
        var svc = await GmailAsync(ct).ConfigureAwait(false);
        var req = svc.Users.Messages.List(Me);
        req.MaxResults = pageSize;
        req.PageToken = pageToken;
        req.LabelIds = new Repeatable<string>(new[] { string.IsNullOrWhiteSpace(folderId) ? "INBOX" : folderId });
        var resp = await req.ExecuteAsync(ct).ConfigureAwait(false);
        return await BuildPageAsync(svc, resp.Messages, resp.NextPageToken, ct).ConfigureAwait(false);
    }

    public async Task<MailPage> SearchAsync(string query, int pageSize, CancellationToken ct)
    {
        var svc = await GmailAsync(ct).ConfigureAwait(false);
        var req = svc.Users.Messages.List(Me);
        req.MaxResults = pageSize;
        req.Q = query;
        var resp = await req.ExecuteAsync(ct).ConfigureAwait(false);
        return await BuildPageAsync(svc, resp.Messages, resp.NextPageToken, ct).ConfigureAwait(false);
    }

    public async Task<DraftResult> CreateDraftAsync(OutgoingMessage message, CancellationToken ct)
    {
        var svc = await GmailAsync(ct).ConfigureAwait(false);
        var raw = BuildRawMessage(message);
        var draft = new GmailData.Draft { Message = new GmailData.Message { Raw = raw } };
        var created = await svc.Users.Drafts.Create(draft, Me).ExecuteAsync(ct).ConfigureAwait(false);
        return new DraftResult(created.Id, created.Message?.Id, null);
    }

    public async Task<SendResult> SendAsync(OutgoingMessage? message, string? draftId, CancellationToken ct)
    {
        var svc = await GmailAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(draftId))
        {
            var sent = await svc.Users.Drafts.Send(new GmailData.Draft { Id = draftId }, Me).ExecuteAsync(ct).ConfigureAwait(false);
            return new SendResult(sent.Id, true);
        }

        if (message is null)
        {
            throw new ArgumentException("Provide either a draftId or an inline message.", nameof(message));
        }

        var raw = BuildRawMessage(message);
        var result = await svc.Users.Messages.Send(new GmailData.Message { Raw = raw }, Me).ExecuteAsync(ct).ConfigureAwait(false);
        return new SendResult(result.Id, true);
    }

    public async Task DeleteAsync(string messageId, bool permanent, CancellationToken ct)
    {
        var svc = await GmailAsync(ct).ConfigureAwait(false);
        if (permanent)
        {
            await svc.Users.Messages.Delete(Me, messageId).ExecuteAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await svc.Users.Messages.Trash(Me, messageId).ExecuteAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task MoveAsync(string messageId, string destinationFolderId, CancellationToken ct)
    {
        var svc = await GmailAsync(ct).ConfigureAwait(false);
        var mod = new GmailData.ModifyMessageRequest
        {
            AddLabelIds = new[] { destinationFolderId },
            RemoveLabelIds = new[] { "INBOX" },
        };
        await svc.Users.Messages.Modify(mod, Me, messageId).ExecuteAsync(ct).ConfigureAwait(false);
    }

    // ---------------- Calendar ----------------

    public async Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken ct)
    {
        var svc = await CalendarAsync(ct).ConfigureAwait(false);
        var resp = await svc.CalendarList.List().ExecuteAsync(ct).ConfigureAwait(false);
        return resp.Items?.Select(c => new CalendarInfo(
            c.Id, c.Summary ?? c.Id, c.Primary ?? false,
            c.AccessRole is "owner" or "writer")).ToList() ?? new List<CalendarInfo>();
    }

    public async Task<EventPage> ReadAsync(string? calendarId, DateTimeOffset start, DateTimeOffset end, string? pageToken, int pageSize, CancellationToken ct)
    {
        var svc = await CalendarAsync(ct).ConfigureAwait(false);
        var req = svc.Events.List(string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId);
        req.TimeMinDateTimeOffset = start;
        req.TimeMaxDateTimeOffset = end;
        req.SingleEvents = true;
        req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        req.MaxResults = pageSize;
        req.PageToken = pageToken;
        var resp = await req.ExecuteAsync(ct).ConfigureAwait(false);
        return new EventPage
        {
            Events = resp.Items?.Select(MapEvent).ToList() ?? new List<CalendarEvent>(),
            NextPageToken = resp.NextPageToken,
        };
    }

    public async Task<CalendarEvent> GetEventAsync(string? calendarId, string eventId, CancellationToken ct)
    {
        var svc = await CalendarAsync(ct).ConfigureAwait(false);
        var e = await svc.Events.Get(string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId, eventId).ExecuteAsync(ct).ConfigureAwait(false);
        return MapEvent(e);
    }

    public async Task<EventPage> SearchAsync(string? calendarId, string query, DateTimeOffset? start, DateTimeOffset? end, int pageSize, CancellationToken ct)
    {
        var svc = await CalendarAsync(ct).ConfigureAwait(false);
        var req = svc.Events.List(string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId);
        req.Q = query;
        req.SingleEvents = true;
        req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        req.MaxResults = pageSize;
        if (start is { } s) req.TimeMinDateTimeOffset = s;
        if (end is { } e) req.TimeMaxDateTimeOffset = e;
        var resp = await req.ExecuteAsync(ct).ConfigureAwait(false);
        return new EventPage
        {
            Events = resp.Items?.Select(MapEvent).ToList() ?? new List<CalendarEvent>(),
            NextPageToken = resp.NextPageToken,
        };
    }

    public async Task<CalendarEvent> CreateEventAsync(string? calendarId, EventInput input, CancellationToken ct)
    {
        var svc = await CalendarAsync(ct).ConfigureAwait(false);
        var req = svc.Events.Insert(BuildEvent(new CalData.Event(), input), string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId);
        if (input.CreateOnlineMeeting)
        {
            req.ConferenceDataVersion = 1;
        }
        var created = await req.ExecuteAsync(ct).ConfigureAwait(false);
        return MapEvent(created);
    }

    public async Task<CalendarEvent> UpdateEventAsync(string? calendarId, string eventId, EventInput input, CancellationToken ct)
    {
        var svc = await CalendarAsync(ct).ConfigureAwait(false);
        var cal = string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId;
        var patch = BuildEvent(new CalData.Event(), input);
        var updated = await svc.Events.Patch(patch, cal, eventId).ExecuteAsync(ct).ConfigureAwait(false);
        return MapEvent(updated);
    }

    public async Task DeleteEventAsync(string? calendarId, string eventId, CancellationToken ct)
    {
        var svc = await CalendarAsync(ct).ConfigureAwait(false);
        await svc.Events.Delete(string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId, eventId).ExecuteAsync(ct).ConfigureAwait(false);
    }

    public async Task RespondEventAsync(string? calendarId, string eventId, EventResponse response, string? comment, CancellationToken ct)
    {
        var svc = await CalendarAsync(ct).ConfigureAwait(false);
        var cal = string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId;
        var e = await svc.Events.Get(cal, eventId).ExecuteAsync(ct).ConfigureAwait(false);
        var self = e.Attendees?.FirstOrDefault(a => a.Self == true);
        if (self is null)
        {
            throw new InvalidOperationException("You are not an attendee of this event, so it cannot be responded to.");
        }
        self.ResponseStatus = response switch
        {
            EventResponse.Accept => "accepted",
            EventResponse.Decline => "declined",
            EventResponse.Tentative => "tentative",
            _ => self.ResponseStatus,
        };
        if (!string.IsNullOrWhiteSpace(comment))
        {
            self.Comment = comment;
        }
        await svc.Events.Patch(new CalData.Event { Attendees = e.Attendees }, cal, eventId).ExecuteAsync(ct).ConfigureAwait(false);
    }

    // ---------------- Contacts (v2) ----------------

    public Task<ContactPage> ListAsync(string? pageToken, int pageSize, CancellationToken ct) => throw NotV1();
    public Task<Contact> GetAsync(string contactId, CancellationToken ct) => throw NotV1();
    public Task<Contact> AddAsync(ContactInput input, CancellationToken ct) => throw NotV1();
    public Task<Contact> EditAsync(string contactId, ContactInput input, CancellationToken ct) => throw NotV1();
    Task IContactsProvider.DeleteAsync(string contactId, CancellationToken ct) => throw NotV1();

    private static NotSupportedException NotV1() => new("Contacts are a v2 feature and are not enabled.");

    // ---------------- Helpers ----------------

    private async Task<MailPage> BuildPageAsync(GmailService svc, IList<GmailData.Message>? ids, string? nextToken, CancellationToken ct)
    {
        var messages = new List<EmailMessage>();
        foreach (var stub in ids ?? new List<GmailData.Message>())
        {
            var req = svc.Users.Messages.Get(Me, stub.Id);
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            req.MetadataHeaders = new Repeatable<string>(SummaryHeaders);
            var m = await req.ExecuteAsync(ct).ConfigureAwait(false);
            messages.Add(MapSummary(m));
        }
        return new MailPage { Messages = messages, NextPageToken = nextToken };
    }

    private static EmailMessage MapSummary(GmailData.Message m)
    {
        var headers = m.Payload?.Headers;
        return new EmailMessage
        {
            Id = m.Id,
            ThreadId = m.ThreadId,
            Subject = Header(headers, "Subject"),
            From = ParseOne(Header(headers, "From")),
            To = ParseMany(Header(headers, "To")),
            Cc = ParseMany(Header(headers, "Cc")),
            ReceivedAt = ParseDate(Header(headers, "Date")),
            SentAt = ParseDate(Header(headers, "Date")),
            IsRead = m.LabelIds?.Contains("UNREAD") != true,
            HasAttachments = false,
            Preview = m.Snippet,
        };
    }

    private static EmailMessage MapFull(GmailData.Message m, int maxBodyChars)
    {
        var headers = m.Payload?.Headers;
        var attachments = new List<AttachmentInfo>();
        var (body, type) = ExtractBody(m.Payload, attachments);
        var truncated = false;
        if (body is not null && maxBodyChars > 0 && body.Length > maxBodyChars)
        {
            body = body[..maxBodyChars];
            truncated = true;
        }

        return new EmailMessage
        {
            Id = m.Id,
            ThreadId = m.ThreadId,
            Subject = Header(headers, "Subject"),
            From = ParseOne(Header(headers, "From")),
            To = ParseMany(Header(headers, "To")),
            Cc = ParseMany(Header(headers, "Cc")),
            ReceivedAt = ParseDate(Header(headers, "Date")),
            SentAt = ParseDate(Header(headers, "Date")),
            IsRead = m.LabelIds?.Contains("UNREAD") != true,
            HasAttachments = attachments.Count > 0,
            Preview = m.Snippet,
            Body = body,
            BodyContentType = type,
            BodyTruncated = truncated,
            Attachments = attachments,
        };
    }

    private static string? Header(IList<GmailData.MessagePartHeader>? headers, string name) =>
        headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static (string? body, string? type) ExtractBody(GmailData.MessagePart? part, List<AttachmentInfo> attachments)
    {
        string? plain = null, html = null;

        void Walk(GmailData.MessagePart? p)
        {
            if (p is null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(p.Filename))
            {
                attachments.Add(new AttachmentInfo(
                    p.Body?.AttachmentId ?? string.Empty, p.Filename, p.MimeType, p.Body?.Size,
                    Header(p.Headers, "Content-Disposition")?.Contains("inline", StringComparison.OrdinalIgnoreCase) ?? false));
            }
            else if (p.Body?.Data is { } data)
            {
                if (p.MimeType == "text/plain") plain ??= DecodeB64Url(data);
                else if (p.MimeType == "text/html") html ??= DecodeB64Url(data);
            }

            if (p.Parts is not null)
            {
                foreach (var child in p.Parts)
                {
                    Walk(child);
                }
            }
        }

        Walk(part);
        return plain is not null ? (plain, "text/plain") : (html, html is null ? null : "text/html");
    }

    private string BuildRawMessage(OutgoingMessage message)
    {
        var mime = new MimeMessage();
        foreach (var to in message.To) mime.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in message.Cc) mime.Cc.Add(MailboxAddress.Parse(cc));
        foreach (var bcc in message.Bcc) mime.Bcc.Add(MailboxAddress.Parse(bcc));
        mime.Subject = message.Subject ?? string.Empty;
        mime.Body = new TextPart(message.BodyIsHtml ? "html" : "plain") { Text = message.Body ?? string.Empty };

        using var ms = new MemoryStream();
        mime.WriteTo(ms);
        return EncodeB64Url(ms.ToArray());
    }

    private CalendarEvent MapEvent(CalData.Event e) => new()
    {
        Id = e.Id,
        Subject = e.Summary,
        Body = e.Description,
        Location = e.Location,
        Start = e.Start?.DateTimeDateTimeOffset ?? ParseDate(e.Start?.Date),
        End = e.End?.DateTimeDateTimeOffset ?? ParseDate(e.End?.Date),
        IsAllDay = e.Start?.Date is not null,
        TimeZone = e.Start?.TimeZone,
        Organizer = e.Organizer is null ? null : new Models.EmailAddress(e.Organizer.DisplayName, e.Organizer.Email ?? string.Empty),
        Attendees = e.Attendees?.Select(a => new Models.Attendee(
            a.DisplayName, a.Email ?? string.Empty, a.ResponseStatus, a.Optional == true ? "Optional" : "Required")).ToList()
            ?? (IReadOnlyList<Models.Attendee>)Array.Empty<Models.Attendee>(),
        OnlineMeetingUrl = e.HangoutLink,
        Status = e.Status,
        IsCancelled = string.Equals(e.Status, "cancelled", StringComparison.OrdinalIgnoreCase),
        WebLink = e.HtmlLink,
    };

    private static CalData.Event BuildEvent(CalData.Event evt, EventInput input)
    {
        if (input.Subject is not null) evt.Summary = input.Subject;
        if (input.Body is not null) evt.Description = input.Body;
        if (input.Location is not null) evt.Location = input.Location;
        if (input.Start is { } s) evt.Start = ToEventDateTime(s, input.IsAllDay, input.TimeZone);
        if (input.End is { } e) evt.End = ToEventDateTime(e, input.IsAllDay, input.TimeZone);
        if (input.Attendees.Count > 0)
        {
            evt.Attendees = input.Attendees.Select(a => new CalData.EventAttendee { Email = a }).ToList();
        }
        if (input.CreateOnlineMeeting)
        {
            evt.ConferenceData = new CalData.ConferenceData
            {
                CreateRequest = new CalData.CreateConferenceRequest
                {
                    RequestId = Guid.NewGuid().ToString("n"),
                    ConferenceSolutionKey = new CalData.ConferenceSolutionKey { Type = "hangoutsMeet" },
                },
            };
        }
        return evt;
    }

    private static CalData.EventDateTime ToEventDateTime(DateTimeOffset value, bool allDay, string? timeZone) =>
        allDay
            ? new CalData.EventDateTime { Date = value.ToString("yyyy-MM-dd") }
            : new CalData.EventDateTime { DateTimeDateTimeOffset = value, TimeZone = timeZone };

    private static IReadOnlyList<Models.EmailAddress> ParseMany(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return Array.Empty<Models.EmailAddress>();
        }
        return InternetAddressList.Parse(header).Mailboxes
            .Select(m => new Models.EmailAddress(string.IsNullOrWhiteSpace(m.Name) ? null : m.Name, m.Address))
            .ToList();
    }

    private static Models.EmailAddress? ParseOne(string? header) => ParseMany(header).FirstOrDefault();

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.TryParse(value, out var d) ? d : null;

    private static string EncodeB64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string DecodeB64Url(string data)
    {
        var s = data.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
