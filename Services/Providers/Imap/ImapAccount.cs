using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Services.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using ModelMailFolder = MailCalMCPSharp.Services.Models.MailFolder;

namespace MailCalMCPSharp.Services.Providers.Imap;

/// <summary>
/// Generic IMAP (read) + SMTP (send) mailbox. Email only — calendar, contacts, rules, and
/// scheduled send are not part of the IMAP/SMTP protocols and report as unsupported. Clients are
/// opened per operation (connect → authenticate → work → disconnect), which keeps the account
/// stateless and safe for occasional MCP calls.
/// </summary>
public sealed class ImapAccount : IMailCalAccount, IMailProvider, ICalendarProvider, IContactsProvider, IRulesProvider
{
    private readonly AccountEntry _entry;
    private readonly ImapSettings _s;
    private readonly string _password;
    private readonly MailCalOptions _options;

    public ImapAccount(AccountEntry entry, MailCalOptions options)
    {
        _entry = entry;
        _s = entry.Imap;
        _options = options;
        _password = MailCalMCPSharp.Services.AccountRegistry.ResolveSecret(_s.Password);
    }

    public string Alias => _entry.Alias;
    public MailCalProvider Provider => MailCalProvider.Imap;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        Calendar = false,
        MailSearch = true,
        CalendarTextSearch = false,
        ScheduledSend = false,
        MailRules = false,
        Contacts = false,
        PermanentDelete = true,
    };

    public IMailProvider Mail => this;
    public ICalendarProvider Calendar => this;
    public IContactsProvider Contacts => this;
    public IRulesProvider Rules => this;

    // ---------------- connections ----------------

    private static SecureSocketOptions SecurityFor(string? mode) => mode?.ToLowerInvariant() switch
    {
        "ssl" => SecureSocketOptions.SslOnConnect,
        "starttls" => SecureSocketOptions.StartTls,
        "none" => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto,
    };

    private async Task<ImapClient> OpenImapAsync(CancellationToken ct)
    {
        var client = new ImapClient();
        await client.ConnectAsync(_s.ImapHost, _s.ImapPort, SecurityFor(_s.Security), ct).ConfigureAwait(false);
        await client.AuthenticateAsync(_s.Username, _password, ct).ConfigureAwait(false);
        return client;
    }

    private async Task<SmtpClient> OpenSmtpAsync(CancellationToken ct)
    {
        var client = new SmtpClient();
        await client.ConnectAsync(_s.SmtpHost, _s.SmtpPort, SecurityFor(_s.Security), ct).ConfigureAwait(false);
        await client.AuthenticateAsync(_s.Username, _password, ct).ConfigureAwait(false);
        return client;
    }

    // ---------------- Mail ----------------

    public async Task<IReadOnlyList<ModelMailFolder>> ListFoldersAsync(CancellationToken ct)
    {
        using var imap = await OpenImapAsync(ct).ConfigureAwait(false);
        var result = new List<ModelMailFolder> { new(imap.Inbox.FullName, imap.Inbox.Name) };
        foreach (var ns in imap.PersonalNamespaces)
        {
            var root = imap.GetFolder(ns);
            foreach (var f in await root.GetSubfoldersAsync(false, ct).ConfigureAwait(false))
            {
                if (!string.Equals(f.FullName, imap.Inbox.FullName, StringComparison.Ordinal))
                {
                    result.Add(new ModelMailFolder(f.FullName, f.Name));
                }
            }
        }
        await imap.DisconnectAsync(true, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<EmailMessage> ReadAsync(string messageId, int maxBodyChars, CancellationToken ct)
    {
        var (folderName, uid) = Decode(messageId);
        using var imap = await OpenImapAsync(ct).ConfigureAwait(false);
        var folder = await imap.GetFolderAsync(folderName, ct).ConfigureAwait(false);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
        var message = await folder.GetMessageAsync(uid, ct).ConfigureAwait(false);
        await imap.DisconnectAsync(true, ct).ConfigureAwait(false);

        var isHtml = message.TextBody is null && message.HtmlBody is not null;
        var raw = message.TextBody ?? message.HtmlBody;
        var truncated = false;
        if (raw is not null && maxBodyChars > 0 && raw.Length > maxBodyChars)
        {
            raw = raw[..maxBodyChars];
            truncated = true;
        }

        var attachments = message.Attachments.Select(a =>
        {
            var name = (a as MimePart)?.FileName ?? a.ContentDisposition?.FileName ?? a.ContentType.Name ?? "attachment";
            var inline = string.Equals(a.ContentDisposition?.Disposition, "inline", StringComparison.OrdinalIgnoreCase);
            return new AttachmentInfo(name, name, a.ContentType.MimeType, null, inline);
        }).ToList();

        return new EmailMessage
        {
            Id = messageId,
            Subject = message.Subject,
            From = MapAddress(message.From.Mailboxes.FirstOrDefault()),
            To = MapAddresses(message.To.Mailboxes),
            Cc = MapAddresses(message.Cc.Mailboxes),
            ReceivedAt = message.Date,
            SentAt = message.Date,
            HasAttachments = attachments.Count > 0,
            Body = raw,
            BodyContentType = raw is null ? null : (isHtml ? "text/html" : "text/plain"),
            BodyTruncated = truncated,
            Attachments = attachments,
        };
    }

    public async Task<MailPage> ListAsync(string? folderId, string? pageToken, int pageSize, CancellationToken ct)
    {
        using var imap = await OpenImapAsync(ct).ConfigureAwait(false);
        var folder = string.IsNullOrWhiteSpace(folderId) ? imap.Inbox : await imap.GetFolderAsync(folderId, ct).ConfigureAwait(false);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

        var total = folder.Count;
        var offset = ParseOffset(pageToken);
        var max = total - 1 - offset;
        var messages = new List<EmailMessage>();
        string? next = null;
        if (max >= 0)
        {
            var min = Math.Max(0, max - pageSize + 1);
            var summaries = await folder.FetchAsync(min, max, MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, ct).ConfigureAwait(false);
            messages = summaries.OrderByDescending(s => s.Index).Select(s => MapSummary(folder.FullName, s)).ToList();
            var newOffset = offset + summaries.Count;
            next = min > 0 ? newOffset.ToString() : null;
        }
        await imap.DisconnectAsync(true, ct).ConfigureAwait(false);
        return new MailPage { Messages = messages, NextPageToken = next };
    }

    public async Task<MailPage> SearchAsync(string query, int pageSize, CancellationToken ct)
    {
        using var imap = await OpenImapAsync(ct).ConfigureAwait(false);
        var folder = imap.Inbox;
        await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
        var q = SearchQuery.SubjectContains(query).Or(SearchQuery.BodyContains(query)).Or(SearchQuery.FromContains(query));
        var uids = await folder.SearchAsync(q, ct).ConfigureAwait(false);
        var take = uids.Skip(Math.Max(0, uids.Count - pageSize)).ToList();
        var messages = new List<EmailMessage>();
        if (take.Count > 0)
        {
            var summaries = await folder.FetchAsync(take, MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, ct).ConfigureAwait(false);
            messages = summaries.OrderByDescending(s => s.Index).Select(s => MapSummary(folder.FullName, s)).ToList();
        }
        await imap.DisconnectAsync(true, ct).ConfigureAwait(false);
        return new MailPage { Messages = messages };
    }

    public async Task<DraftResult> CreateDraftAsync(OutgoingMessage message, CancellationToken ct)
    {
        using var imap = await OpenImapAsync(ct).ConfigureAwait(false);
        var drafts = await GetSpecialAsync(imap, SpecialFolder.Drafts, ct, "Drafts").ConfigureAwait(false);
        var uid = await drafts.AppendAsync(BuildMime(message), MessageFlags.Draft, ct).ConfigureAwait(false);
        await imap.DisconnectAsync(true, ct).ConfigureAwait(false);
        var id = uid.HasValue ? Encode(drafts.FullName, uid.Value) : string.Empty;
        return new DraftResult(id, null, null);
    }

    public async Task<SendResult> SendAsync(OutgoingMessage? message, string? draftId, CancellationToken ct)
    {
        MimeMessage mime;
        if (!string.IsNullOrWhiteSpace(draftId))
        {
            var (folderName, uid) = Decode(draftId);
            using var imap = await OpenImapAsync(ct).ConfigureAwait(false);
            var folder = await imap.GetFolderAsync(folderName, ct).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
            mime = await folder.GetMessageAsync(uid, ct).ConfigureAwait(false);
            await imap.DisconnectAsync(true, ct).ConfigureAwait(false);
        }
        else
        {
            mime = BuildMime(message ?? throw new ArgumentException("Provide either a draftId or an inline message.", nameof(message)));
        }

        using var smtp = await OpenSmtpAsync(ct).ConfigureAwait(false);
        await smtp.SendAsync(mime, ct).ConfigureAwait(false);
        await smtp.DisconnectAsync(true, ct).ConfigureAwait(false);
        return new SendResult(mime.MessageId, true);
    }

    public async Task DeleteAsync(string messageId, bool permanent, CancellationToken ct)
    {
        var (folderName, uid) = Decode(messageId);
        using var imap = await OpenImapAsync(ct).ConfigureAwait(false);
        var folder = await imap.GetFolderAsync(folderName, ct).ConfigureAwait(false);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct).ConfigureAwait(false);
        if (permanent)
        {
            await folder.AddFlagsAsync(uid, MessageFlags.Deleted, true, ct).ConfigureAwait(false);
            await folder.ExpungeAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var trash = await GetSpecialAsync(imap, SpecialFolder.Trash, ct, "Trash", "Deleted Items", "Deleted Messages").ConfigureAwait(false);
            await folder.MoveToAsync(uid, trash, ct).ConfigureAwait(false);
        }
        await imap.DisconnectAsync(true, ct).ConfigureAwait(false);
    }

    public async Task MoveAsync(string messageId, string destinationFolderId, CancellationToken ct)
    {
        var (folderName, uid) = Decode(messageId);
        using var imap = await OpenImapAsync(ct).ConfigureAwait(false);
        var source = await imap.GetFolderAsync(folderName, ct).ConfigureAwait(false);
        var dest = await imap.GetFolderAsync(destinationFolderId, ct).ConfigureAwait(false);
        await source.OpenAsync(FolderAccess.ReadWrite, ct).ConfigureAwait(false);
        await source.MoveToAsync(uid, dest, ct).ConfigureAwait(false);
        await imap.DisconnectAsync(true, ct).ConfigureAwait(false);
    }

    public Task<SendResult> ScheduleSendAsync(OutgoingMessage message, DateTimeOffset sendAt, CancellationToken ct) =>
        throw Unsupported("scheduled send");

    // ---------------- Unsupported domains ----------------

    Task<IReadOnlyList<CalendarInfo>> ICalendarProvider.ListCalendarsAsync(CancellationToken ct) => throw Unsupported("calendar");
    Task<EventPage> ICalendarProvider.ReadAsync(string? c, DateTimeOffset s, DateTimeOffset e, string? p, int n, CancellationToken ct) => throw Unsupported("calendar");
    Task<CalendarEvent> ICalendarProvider.GetEventAsync(string? c, string id, CancellationToken ct) => throw Unsupported("calendar");
    Task<EventPage> ICalendarProvider.SearchAsync(string? c, string q, DateTimeOffset? s, DateTimeOffset? e, int n, CancellationToken ct) => throw Unsupported("calendar");
    Task<CalendarEvent> ICalendarProvider.CreateEventAsync(string? c, EventInput i, CancellationToken ct) => throw Unsupported("calendar");
    Task<CalendarEvent> ICalendarProvider.UpdateEventAsync(string? c, string id, EventInput i, CancellationToken ct) => throw Unsupported("calendar");
    Task ICalendarProvider.DeleteEventAsync(string? c, string id, CancellationToken ct) => throw Unsupported("calendar");
    Task ICalendarProvider.RespondEventAsync(string? c, string id, EventResponse r, string? cm, CancellationToken ct) => throw Unsupported("calendar");

    Task<ContactPage> IContactsProvider.ListAsync(string? p, int n, CancellationToken ct) => throw Unsupported("contacts");
    Task<Contact> IContactsProvider.GetAsync(string id, CancellationToken ct) => throw Unsupported("contacts");
    Task<Contact> IContactsProvider.AddAsync(ContactInput i, CancellationToken ct) => throw Unsupported("contacts");
    Task<Contact> IContactsProvider.EditAsync(string id, ContactInput i, CancellationToken ct) => throw Unsupported("contacts");
    Task IContactsProvider.DeleteAsync(string id, CancellationToken ct) => throw Unsupported("contacts");

    Task<IReadOnlyList<MailRule>> IRulesProvider.ListRulesAsync(CancellationToken ct) => throw Unsupported("mail rules");
    Task<MailRule> IRulesProvider.CreateRuleAsync(MailRuleInput i, CancellationToken ct) => throw Unsupported("mail rules");
    Task IRulesProvider.DeleteRuleAsync(string id, CancellationToken ct) => throw Unsupported("mail rules");

    private NotSupportedException Unsupported(string feature) =>
        new($"IMAP/SMTP account '{Alias}' supports email only; {feature} is not available.");

    // ---------------- helpers ----------------

    private static async Task<IMailFolder> GetSpecialAsync(ImapClient imap, SpecialFolder special, CancellationToken ct, params string[] fallbackNames)
    {
        var folder = imap.GetFolder(special);
        if (folder is not null)
        {
            return folder;
        }
        foreach (var name in fallbackNames)
        {
            try
            {
                return await imap.GetFolderAsync(name, ct).ConfigureAwait(false);
            }
            catch (FolderNotFoundException)
            {
                // try next
            }
        }
        throw new InvalidOperationException($"Could not locate the {special} folder on the server.");
    }

    private MimeMessage BuildMime(OutgoingMessage message)
    {
        var mime = new MimeMessage();
        var fromAddress = string.IsNullOrWhiteSpace(_s.FromAddress) ? _s.Username : _s.FromAddress;
        mime.From.Add(new MailboxAddress(_s.DisplayName ?? string.Empty, fromAddress));
        foreach (var to in message.To) mime.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in message.Cc) mime.Cc.Add(MailboxAddress.Parse(cc));
        foreach (var bcc in message.Bcc) mime.Bcc.Add(MailboxAddress.Parse(bcc));
        mime.Subject = message.Subject ?? string.Empty;
        mime.Body = new TextPart(message.BodyIsHtml ? "html" : "plain") { Text = message.Body ?? string.Empty };
        return mime;
    }

    private EmailMessage MapSummary(string folderName, IMessageSummary s) => new()
    {
        Id = Encode(folderName, s.UniqueId),
        Subject = s.Envelope?.Subject,
        From = MapAddress(s.Envelope?.From.Mailboxes.FirstOrDefault()),
        To = MapAddresses(s.Envelope?.To.Mailboxes),
        Cc = MapAddresses(s.Envelope?.Cc.Mailboxes),
        ReceivedAt = s.Envelope?.Date ?? s.InternalDate,
        SentAt = s.Envelope?.Date,
        IsRead = s.Flags?.HasFlag(MessageFlags.Seen) ?? false,
    };

    private static Models.EmailAddress? MapAddress(MailboxAddress? m) =>
        m is null ? null : new Models.EmailAddress(string.IsNullOrWhiteSpace(m.Name) ? null : m.Name, m.Address);

    private static IReadOnlyList<Models.EmailAddress> MapAddresses(IEnumerable<MailboxAddress>? mailboxes) =>
        mailboxes?.Select(m => new Models.EmailAddress(string.IsNullOrWhiteSpace(m.Name) ? null : m.Name, m.Address)).ToList()
        ?? (IReadOnlyList<Models.EmailAddress>)Array.Empty<Models.EmailAddress>();

    private static string Encode(string folder, UniqueId uid) => $"{folder}|{uid.Id}";

    private static (string folder, UniqueId uid) Decode(string id)
    {
        var idx = id.LastIndexOf('|');
        if (idx <= 0 || !uint.TryParse(id[(idx + 1)..], out var n))
        {
            throw new ArgumentException($"Invalid IMAP message id '{id}'. Expected '<folder>|<uid>'.", nameof(id));
        }
        return (id[..idx], new UniqueId(n));
    }

    private static int ParseOffset(string? pageToken) =>
        int.TryParse(pageToken, out var n) && n >= 0 ? n : 0;
}
