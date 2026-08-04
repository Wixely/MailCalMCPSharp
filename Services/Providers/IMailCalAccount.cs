using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Providers;

/// <summary>
/// One configured account, exposing the domain providers behind a provider-neutral surface.
/// Outlook and Gmail each implement this; adding a provider means adding one implementation,
/// not new tools.
/// </summary>
public interface IMailCalAccount
{
    string Alias { get; }
    MailCalProvider Provider { get; }
    ProviderCapabilities Capabilities { get; }

    IMailProvider Mail { get; }
    ICalendarProvider Calendar { get; }
    IContactsProvider Contacts { get; }
    IRulesProvider Rules { get; }
}

/// <summary>Email operations. All calls act on the account's primary mailbox.</summary>
public interface IMailProvider
{
    Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken ct);
    Task<EmailMessage> ReadAsync(string messageId, int maxBodyChars, CancellationToken ct);
    Task<MailPage> ListAsync(string? folderId, string? pageToken, int pageSize, CancellationToken ct);
    Task<MailPage> SearchAsync(string query, int pageSize, CancellationToken ct);
    Task<DraftResult> CreateDraftAsync(OutgoingMessage message, CancellationToken ct);
    Task<SendResult> SendAsync(OutgoingMessage? message, string? draftId, CancellationToken ct);
    Task DeleteAsync(string messageId, bool permanent, CancellationToken ct);
    Task MoveAsync(string messageId, string destinationFolderId, CancellationToken ct);

    /// <summary>Send a message at a future time, where the provider supports native deferred delivery (Outlook does; Gmail does not).</summary>
    Task<SendResult> ScheduleSendAsync(OutgoingMessage message, DateTimeOffset sendAt, CancellationToken ct);
}

/// <summary>Calendar operations.</summary>
public interface ICalendarProvider
{
    Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken ct);
    Task<EventPage> ReadAsync(string? calendarId, DateTimeOffset start, DateTimeOffset end, string? pageToken, int pageSize, CancellationToken ct);
    Task<CalendarEvent> GetEventAsync(string? calendarId, string eventId, CancellationToken ct);
    Task<EventPage> SearchAsync(string? calendarId, string query, DateTimeOffset? start, DateTimeOffset? end, int pageSize, CancellationToken ct);
    Task<CalendarEvent> CreateEventAsync(string? calendarId, EventInput input, CancellationToken ct);
    Task<CalendarEvent> UpdateEventAsync(string? calendarId, string eventId, EventInput input, CancellationToken ct);
    Task DeleteEventAsync(string? calendarId, string eventId, CancellationToken ct);
    Task RespondEventAsync(string? calendarId, string eventId, EventResponse response, string? comment, CancellationToken ct);
}

/// <summary>Contacts operations.</summary>
public interface IContactsProvider
{
    Task<ContactPage> ListAsync(string? pageToken, int pageSize, CancellationToken ct);
    Task<Contact> GetAsync(string contactId, CancellationToken ct);
    Task<Contact> AddAsync(ContactInput input, CancellationToken ct);
    Task<Contact> EditAsync(string contactId, ContactInput input, CancellationToken ct);
    Task DeleteAsync(string contactId, CancellationToken ct);
}

/// <summary>Inbox rules (Outlook message rules) / filters (Gmail).</summary>
public interface IRulesProvider
{
    Task<IReadOnlyList<MailRule>> ListRulesAsync(CancellationToken ct);
    Task<MailRule> CreateRuleAsync(MailRuleInput input, CancellationToken ct);
    Task DeleteRuleAsync(string ruleId, CancellationToken ct);
}
