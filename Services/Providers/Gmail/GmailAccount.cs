using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Auth;
using MailCalMCPSharp.Services.Models;

namespace MailCalMCPSharp.Services.Providers.Gmail;

/// <summary>
/// Gmail account backed by the Google API client libraries (Gmail + Calendar; People in v2).
/// v1-skeleton: wiring and capabilities are in place; data methods are stubbed and will be
/// implemented against the Gmail/Calendar services built from
/// <see cref="IAuthenticator.AcquireAccessTokenAsync"/>.
/// </summary>
public sealed class GmailAccount : IMailCalAccount, IMailProvider, ICalendarProvider, IContactsProvider
{
    private readonly AccountEntry _entry;
    private readonly IAuthenticator _authenticator;
    private readonly MailCalOptions _options;

    public GmailAccount(AccountEntry entry, IAuthenticator authenticator, MailCalOptions options)
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
        ScheduledSend = false,     // Gmail send-later is not exposed by the API — tool reports "not supported"
        MailRules = true,          // Gmail filters (v2 tool)
        CalendarTextSearch = true, // Google Calendar q= text search
        Contacts = true,           // People API (v2 tool)
        PermanentDelete = true,
    };

    public IMailProvider Mail => this;
    public ICalendarProvider Calendar => this;
    public IContactsProvider Contacts => this;

    private static T NotYet<T>(string op) =>
        throw new NotImplementedException($"Gmail.{op} is not implemented in the v1 skeleton yet.");

    // ---- IMailProvider ----
    public Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken ct) => NotYet<Task<IReadOnlyList<MailFolder>>>(nameof(ListFoldersAsync));
    public Task<EmailMessage> ReadAsync(string messageId, int maxBodyChars, CancellationToken ct) => NotYet<Task<EmailMessage>>(nameof(ReadAsync));
    public Task<MailPage> ListAsync(string? folderId, string? pageToken, int pageSize, CancellationToken ct) => NotYet<Task<MailPage>>(nameof(ListAsync));
    public Task<MailPage> SearchAsync(string query, int pageSize, CancellationToken ct) => NotYet<Task<MailPage>>(nameof(SearchAsync));
    public Task<DraftResult> CreateDraftAsync(OutgoingMessage message, CancellationToken ct) => NotYet<Task<DraftResult>>(nameof(CreateDraftAsync));
    public Task<SendResult> SendAsync(OutgoingMessage? message, string? draftId, CancellationToken ct) => NotYet<Task<SendResult>>(nameof(SendAsync));
    public Task DeleteAsync(string messageId, bool permanent, CancellationToken ct) => NotYet<Task>(nameof(DeleteAsync));
    public Task MoveAsync(string messageId, string destinationFolderId, CancellationToken ct) => NotYet<Task>(nameof(MoveAsync));

    // ---- ICalendarProvider ----
    public Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken ct) => NotYet<Task<IReadOnlyList<CalendarInfo>>>(nameof(ListCalendarsAsync));
    public Task<EventPage> ReadAsync(string? calendarId, DateTimeOffset start, DateTimeOffset end, string? pageToken, int pageSize, CancellationToken ct) => NotYet<Task<EventPage>>(nameof(ReadAsync));
    public Task<CalendarEvent> GetEventAsync(string? calendarId, string eventId, CancellationToken ct) => NotYet<Task<CalendarEvent>>(nameof(GetEventAsync));
    public Task<EventPage> SearchAsync(string? calendarId, string query, DateTimeOffset? start, DateTimeOffset? end, int pageSize, CancellationToken ct) => NotYet<Task<EventPage>>(nameof(SearchAsync));
    public Task<CalendarEvent> CreateEventAsync(string? calendarId, EventInput input, CancellationToken ct) => NotYet<Task<CalendarEvent>>(nameof(CreateEventAsync));
    public Task<CalendarEvent> UpdateEventAsync(string? calendarId, string eventId, EventInput input, CancellationToken ct) => NotYet<Task<CalendarEvent>>(nameof(UpdateEventAsync));
    public Task DeleteEventAsync(string? calendarId, string eventId, CancellationToken ct) => NotYet<Task>(nameof(DeleteEventAsync));
    public Task RespondEventAsync(string? calendarId, string eventId, EventResponse response, string? comment, CancellationToken ct) => NotYet<Task>(nameof(RespondEventAsync));

    // ---- IContactsProvider (v2) ----
    public Task<ContactPage> ListAsync(string? pageToken, int pageSize, CancellationToken ct) => NotYet<Task<ContactPage>>(nameof(ListAsync));
    public Task<Contact> GetAsync(string contactId, CancellationToken ct) => NotYet<Task<Contact>>(nameof(GetAsync));
    public Task<Contact> AddAsync(ContactInput input, CancellationToken ct) => NotYet<Task<Contact>>(nameof(AddAsync));
    public Task<Contact> EditAsync(string contactId, ContactInput input, CancellationToken ct) => NotYet<Task<Contact>>(nameof(EditAsync));
    public Task DeleteAsync(string contactId, CancellationToken ct) => NotYet<Task>(nameof(DeleteAsync));
}
