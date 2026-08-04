using System.Globalization;
using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Auth;
using MailCalMCPSharp.Services.Models;
using Microsoft.Graph;
using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Me.Messages.Item.Move;
using Microsoft.Graph.Me.Events.Item.Accept;
using Microsoft.Graph.Me.Events.Item.Decline;
using Microsoft.Graph.Me.Events.Item.TentativelyAccept;
using Microsoft.Kiota.Abstractions.Authentication;
using GraphModels = Microsoft.Graph.Models;

namespace MailCalMCPSharp.Services.Providers.Outlook;

/// <summary>Outlook account backed by Microsoft Graph.</summary>
public sealed class OutlookAccount : IMailCalAccount, IMailProvider, ICalendarProvider, IContactsProvider
{
    private readonly AccountEntry _entry;
    private readonly IAuthenticator _authenticator;
    private readonly MailCalOptions _options;
    private readonly Lazy<GraphServiceClient> _graph;

    private static readonly string[] MessageListSelect =
    {
        "id", "conversationId", "subject", "from", "toRecipients", "ccRecipients",
        "receivedDateTime", "sentDateTime", "isRead", "hasAttachments", "bodyPreview", "webLink",
    };

    public OutlookAccount(AccountEntry entry, IAuthenticator authenticator, MailCalOptions options)
    {
        _entry = entry;
        _authenticator = authenticator;
        _options = options;
        _graph = new Lazy<GraphServiceClient>(() =>
            new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(
                new GraphTokenProvider(_authenticator.AcquireAccessTokenAsync))));
    }

    public string Alias => _entry.Alias;
    public MailCalProvider Provider => MailCalProvider.Outlook;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        MailSearch = true,
        ScheduledSend = true,
        MailRules = true,
        CalendarTextSearch = true,
        Contacts = true,
        PermanentDelete = true,
    };

    public IMailProvider Mail => this;
    public ICalendarProvider Calendar => this;
    public IContactsProvider Contacts => this;

    private GraphServiceClient Graph => _graph.Value;

    // ---------------- Mail ----------------

    public async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken ct)
    {
        var resp = await Graph.Me.MailFolders.GetAsync(rc =>
        {
            rc.QueryParameters.Top = 100;
        }, ct).ConfigureAwait(false);

        return resp?.Value?.Select(f => new MailFolder(
            f.Id ?? string.Empty,
            f.DisplayName ?? string.Empty,
            f.UnreadItemCount,
            f.TotalItemCount)).ToList() ?? new List<MailFolder>();
    }

    public async Task<EmailMessage> ReadAsync(string messageId, int maxBodyChars, CancellationToken ct)
    {
        var msg = await Graph.Me.Messages[messageId].GetAsync(rc =>
        {
            rc.QueryParameters.Select = MessageListSelect.Append("body").ToArray();
        }, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Message '{messageId}' not found.");

        var attachments = new List<AttachmentInfo>();
        if (msg.HasAttachments == true)
        {
            var att = await Graph.Me.Messages[messageId].Attachments.GetAsync(cancellationToken: ct).ConfigureAwait(false);
            foreach (var a in att?.Value ?? new List<GraphModels.Attachment>())
            {
                attachments.Add(new AttachmentInfo(a.Id ?? string.Empty, a.Name ?? string.Empty, a.ContentType, a.Size, a.IsInline ?? false));
            }
        }

        return MapMessage(msg, includeBody: true, maxBodyChars, attachments);
    }

    public async Task<MailPage> ListAsync(string? folderId, string? pageToken, int pageSize, CancellationToken ct)
    {
        var folder = string.IsNullOrWhiteSpace(folderId) ? "inbox" : folderId;
        var builder = Graph.Me.MailFolders[folder].Messages;

        var resp = string.IsNullOrWhiteSpace(pageToken)
            ? await builder.GetAsync(rc =>
            {
                rc.QueryParameters.Top = pageSize;
                rc.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                rc.QueryParameters.Select = MessageListSelect;
            }, ct).ConfigureAwait(false)
            : await builder.WithUrl(pageToken).GetAsync(cancellationToken: ct).ConfigureAwait(false);

        return MapPage(resp?.Value, resp?.OdataNextLink);
    }

    public async Task<MailPage> SearchAsync(string query, int pageSize, CancellationToken ct)
    {
        var resp = await Graph.Me.Messages.GetAsync(rc =>
        {
            rc.QueryParameters.Search = $"\"{query}\"";
            rc.QueryParameters.Top = pageSize;
            rc.QueryParameters.Select = MessageListSelect;
        }, ct).ConfigureAwait(false);

        return MapPage(resp?.Value, resp?.OdataNextLink);
    }

    public async Task<DraftResult> CreateDraftAsync(OutgoingMessage message, CancellationToken ct)
    {
        var created = await Graph.Me.Messages.PostAsync(BuildMessage(message), cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Draft creation returned no message.");
        return new DraftResult(created.Id ?? string.Empty, created.Id, created.WebLink);
    }

    public async Task<SendResult> SendAsync(OutgoingMessage? message, string? draftId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(draftId))
        {
            await Graph.Me.Messages[draftId].Send.PostAsync(cancellationToken: ct).ConfigureAwait(false);
            return new SendResult(draftId, true);
        }

        if (message is null)
        {
            throw new ArgumentException("Provide either a draftId or an inline message.", nameof(message));
        }

        await Graph.Me.SendMail.PostAsync(new SendMailPostRequestBody
        {
            Message = BuildMessage(message),
            SaveToSentItems = true,
        }, cancellationToken: ct).ConfigureAwait(false);
        return new SendResult(null, true);
    }

    public async Task DeleteAsync(string messageId, bool permanent, CancellationToken ct)
    {
        if (permanent)
        {
            await Graph.Me.Messages[messageId].DeleteAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        else
        {
            await Graph.Me.Messages[messageId].Move.PostAsync(new MovePostRequestBody { DestinationId = "deleteditems" }, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    public async Task MoveAsync(string messageId, string destinationFolderId, CancellationToken ct)
    {
        await Graph.Me.Messages[messageId].Move.PostAsync(new MovePostRequestBody { DestinationId = destinationFolderId }, cancellationToken: ct).ConfigureAwait(false);
    }

    // ---------------- Calendar ----------------

    public async Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken ct)
    {
        var resp = await Graph.Me.Calendars.GetAsync(cancellationToken: ct).ConfigureAwait(false);
        return resp?.Value?.Select(c => new CalendarInfo(
            c.Id ?? string.Empty, c.Name ?? string.Empty, c.IsDefaultCalendar ?? false, c.CanEdit ?? false)).ToList()
            ?? new List<CalendarInfo>();
    }

    public async Task<EventPage> ReadAsync(string? calendarId, DateTimeOffset start, DateTimeOffset end, string? pageToken, int pageSize, CancellationToken ct)
    {
        var startStr = start.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        var endStr = end.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

        GraphModels.EventCollectionResponse? resp;
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            resp = string.IsNullOrWhiteSpace(pageToken)
                ? await Graph.Me.CalendarView.GetAsync(rc =>
                {
                    rc.QueryParameters.StartDateTime = startStr;
                    rc.QueryParameters.EndDateTime = endStr;
                    rc.QueryParameters.Top = pageSize;
                    rc.QueryParameters.Orderby = new[] { "start/dateTime" };
                }, ct).ConfigureAwait(false)
                : await Graph.Me.CalendarView.WithUrl(pageToken).GetAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        else
        {
            resp = string.IsNullOrWhiteSpace(pageToken)
                ? await Graph.Me.Calendars[calendarId].CalendarView.GetAsync(rc =>
                {
                    rc.QueryParameters.StartDateTime = startStr;
                    rc.QueryParameters.EndDateTime = endStr;
                    rc.QueryParameters.Top = pageSize;
                    rc.QueryParameters.Orderby = new[] { "start/dateTime" };
                }, ct).ConfigureAwait(false)
                : await Graph.Me.Calendars[calendarId].CalendarView.WithUrl(pageToken).GetAsync(cancellationToken: ct).ConfigureAwait(false);
        }

        var events = resp?.Value?.Select(MapEvent).ToList() ?? new List<CalendarEvent>();
        return new EventPage { Events = events, NextPageToken = resp?.OdataNextLink };
    }

    public async Task<CalendarEvent> GetEventAsync(string? calendarId, string eventId, CancellationToken ct)
    {
        var ev = string.IsNullOrWhiteSpace(calendarId)
            ? await Graph.Me.Events[eventId].GetAsync(cancellationToken: ct).ConfigureAwait(false)
            : await Graph.Me.Calendars[calendarId].Events[eventId].GetAsync(cancellationToken: ct).ConfigureAwait(false);
        return ev is null ? throw new InvalidOperationException($"Event '{eventId}' not found.") : MapEvent(ev);
    }

    public async Task<EventPage> SearchAsync(string? calendarId, string query, DateTimeOffset? start, DateTimeOffset? end, int pageSize, CancellationToken ct)
    {
        var resp = await Graph.Me.Events.GetAsync(rc =>
        {
            rc.QueryParameters.Search = $"\"{query}\"";
            rc.QueryParameters.Top = pageSize;
        }, ct).ConfigureAwait(false);
        var events = resp?.Value?.Select(MapEvent)
            .Where(e => (start is null || e.End >= start) && (end is null || e.Start <= end))
            .ToList() ?? new List<CalendarEvent>();
        return new EventPage { Events = events, NextPageToken = resp?.OdataNextLink };
    }

    public async Task<CalendarEvent> CreateEventAsync(string? calendarId, EventInput input, CancellationToken ct)
    {
        var evt = BuildEvent(new GraphModels.Event(), input);
        var created = string.IsNullOrWhiteSpace(calendarId)
            ? await Graph.Me.Events.PostAsync(evt, cancellationToken: ct).ConfigureAwait(false)
            : await Graph.Me.Calendars[calendarId].Events.PostAsync(evt, cancellationToken: ct).ConfigureAwait(false);
        return created is null ? throw new InvalidOperationException("Event creation returned nothing.") : MapEvent(created);
    }

    public async Task<CalendarEvent> UpdateEventAsync(string? calendarId, string eventId, EventInput input, CancellationToken ct)
    {
        var patch = BuildEvent(new GraphModels.Event(), input, forUpdate: true);
        var updated = string.IsNullOrWhiteSpace(calendarId)
            ? await Graph.Me.Events[eventId].PatchAsync(patch, cancellationToken: ct).ConfigureAwait(false)
            : await Graph.Me.Calendars[calendarId].Events[eventId].PatchAsync(patch, cancellationToken: ct).ConfigureAwait(false);
        return updated is null ? await GetEventAsync(calendarId, eventId, ct) : MapEvent(updated);
    }

    public async Task DeleteEventAsync(string? calendarId, string eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            await Graph.Me.Events[eventId].DeleteAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        else
        {
            await Graph.Me.Calendars[calendarId].Events[eventId].DeleteAsync(cancellationToken: ct).ConfigureAwait(false);
        }
    }

    public async Task RespondEventAsync(string? calendarId, string eventId, EventResponse response, string? comment, CancellationToken ct)
    {
        switch (response)
        {
            case EventResponse.Accept:
                await Graph.Me.Events[eventId].Accept.PostAsync(new AcceptPostRequestBody { Comment = comment, SendResponse = true }, cancellationToken: ct).ConfigureAwait(false);
                break;
            case EventResponse.Decline:
                await Graph.Me.Events[eventId].Decline.PostAsync(new DeclinePostRequestBody { Comment = comment, SendResponse = true }, cancellationToken: ct).ConfigureAwait(false);
                break;
            case EventResponse.Tentative:
                await Graph.Me.Events[eventId].TentativelyAccept.PostAsync(new TentativelyAcceptPostRequestBody { Comment = comment, SendResponse = true }, cancellationToken: ct).ConfigureAwait(false);
                break;
        }
    }

    // ---------------- Contacts (v2) ----------------

    public Task<ContactPage> ListAsync(string? pageToken, int pageSize, CancellationToken ct) => throw NotV1();
    public Task<Contact> GetAsync(string contactId, CancellationToken ct) => throw NotV1();
    public Task<Contact> AddAsync(ContactInput input, CancellationToken ct) => throw NotV1();
    public Task<Contact> EditAsync(string contactId, ContactInput input, CancellationToken ct) => throw NotV1();
    Task IContactsProvider.DeleteAsync(string contactId, CancellationToken ct) => throw NotV1();

    private static NotSupportedException NotV1() => new("Contacts are a v2 feature and are not enabled.");

    // ---------------- Mapping ----------------

    private MailPage MapPage(List<GraphModels.Message>? messages, string? nextLink) => new()
    {
        Messages = messages?.Select(m => MapMessage(m, includeBody: false, 0, null)).ToList() ?? new List<EmailMessage>(),
        NextPageToken = nextLink,
    };

    private EmailMessage MapMessage(GraphModels.Message m, bool includeBody, int maxBodyChars, IReadOnlyList<AttachmentInfo>? attachments)
    {
        string? body = null;
        var truncated = false;
        if (includeBody && m.Body?.Content is { } content)
        {
            if (maxBodyChars > 0 && content.Length > maxBodyChars)
            {
                body = content[..maxBodyChars];
                truncated = true;
            }
            else
            {
                body = content;
            }
        }

        return new EmailMessage
        {
            Id = m.Id ?? string.Empty,
            ThreadId = m.ConversationId,
            Subject = m.Subject,
            From = MapAddress(m.From),
            To = MapAddresses(m.ToRecipients),
            Cc = MapAddresses(m.CcRecipients),
            ReceivedAt = m.ReceivedDateTime,
            SentAt = m.SentDateTime,
            IsRead = m.IsRead ?? false,
            HasAttachments = m.HasAttachments ?? false,
            Preview = m.BodyPreview,
            Body = body,
            BodyContentType = includeBody ? m.Body?.ContentType?.ToString() : null,
            BodyTruncated = truncated,
            Attachments = attachments ?? Array.Empty<AttachmentInfo>(),
            WebLink = m.WebLink,
        };
    }

    private static Models.EmailAddress? MapAddress(GraphModels.Recipient? r) =>
        r?.EmailAddress is { } e ? new Models.EmailAddress(e.Name, e.Address ?? string.Empty) : null;

    private static IReadOnlyList<Models.EmailAddress> MapAddresses(List<GraphModels.Recipient>? recipients) =>
        recipients?.Where(r => r.EmailAddress is not null)
            .Select(r => new Models.EmailAddress(r.EmailAddress!.Name, r.EmailAddress!.Address ?? string.Empty))
            .ToList() ?? (IReadOnlyList<Models.EmailAddress>)Array.Empty<Models.EmailAddress>();

    private static GraphModels.Message BuildMessage(OutgoingMessage message) => new()
    {
        Subject = message.Subject,
        Body = new GraphModels.ItemBody
        {
            ContentType = message.BodyIsHtml ? GraphModels.BodyType.Html : GraphModels.BodyType.Text,
            Content = message.Body,
        },
        ToRecipients = BuildRecipients(message.To),
        CcRecipients = BuildRecipients(message.Cc),
        BccRecipients = BuildRecipients(message.Bcc),
    };

    private static List<GraphModels.Recipient> BuildRecipients(IReadOnlyList<string> addresses) =>
        addresses.Select(a => new GraphModels.Recipient { EmailAddress = new GraphModels.EmailAddress { Address = a } }).ToList();

    private CalendarEvent MapEvent(GraphModels.Event e) => new()
    {
        Id = e.Id ?? string.Empty,
        Subject = e.Subject,
        Body = e.Body?.Content,
        BodyIsHtml = e.Body?.ContentType == GraphModels.BodyType.Html,
        Location = e.Location?.DisplayName,
        Start = ParseGraphDateTime(e.Start),
        End = ParseGraphDateTime(e.End),
        IsAllDay = e.IsAllDay ?? false,
        TimeZone = e.Start?.TimeZone,
        Organizer = MapAddress(e.Organizer),
        Attendees = e.Attendees?.Where(a => a.EmailAddress is not null).Select(a => new Models.Attendee(
            a.EmailAddress!.Name, a.EmailAddress!.Address ?? string.Empty, a.Status?.Response?.ToString(), a.Type?.ToString())).ToList()
            ?? (IReadOnlyList<Models.Attendee>)Array.Empty<Models.Attendee>(),
        OnlineMeetingUrl = e.OnlineMeeting?.JoinUrl ?? e.OnlineMeetingUrl,
        Status = e.ResponseStatus?.Response?.ToString(),
        IsCancelled = e.IsCancelled ?? false,
        WebLink = e.WebLink,
    };

    private static GraphModels.Event BuildEvent(GraphModels.Event evt, EventInput input, bool forUpdate = false)
    {
        if (input.Subject is not null) evt.Subject = input.Subject;
        if (input.Body is not null)
        {
            evt.Body = new GraphModels.ItemBody
            {
                ContentType = input.BodyIsHtml ? GraphModels.BodyType.Html : GraphModels.BodyType.Text,
                Content = input.Body,
            };
        }
        if (input.Location is not null) evt.Location = new GraphModels.Location { DisplayName = input.Location };
        if (input.Start is { } s) evt.Start = ToGraphDateTime(s, input.TimeZone);
        if (input.End is { } en) evt.End = ToGraphDateTime(en, input.TimeZone);
        if (!forUpdate || input.IsAllDay) evt.IsAllDay = input.IsAllDay;
        if (input.Attendees.Count > 0)
        {
            evt.Attendees = input.Attendees.Select(a => new GraphModels.Attendee
            {
                EmailAddress = new GraphModels.EmailAddress { Address = a },
                Type = GraphModels.AttendeeType.Required,
            }).ToList();
        }
        if (input.CreateOnlineMeeting)
        {
            evt.IsOnlineMeeting = true;
            evt.OnlineMeetingProvider = GraphModels.OnlineMeetingProviderType.TeamsForBusiness;
        }
        return evt;
    }

    private static GraphModels.DateTimeTimeZone ToGraphDateTime(DateTimeOffset value, string? timeZone) => new()
    {
        DateTime = value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone,
    };

    private static DateTimeOffset? ParseGraphDateTime(GraphModels.DateTimeTimeZone? dt)
    {
        if (dt?.DateTime is not { } s)
        {
            return null;
        }
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
