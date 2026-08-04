namespace MailCalMCPSharp.Services.Models;

/// <summary>Provider-neutral email address.</summary>
public sealed record EmailAddress(string? Name, string Address);

/// <summary>Provider-neutral mail folder / Gmail label.</summary>
public sealed record MailFolder(string Id, string Name, int? UnreadCount = null, int? TotalCount = null);

/// <summary>Lightweight attachment metadata (content is not inlined into list/read responses).</summary>
public sealed record AttachmentInfo(string Id, string Name, string? ContentType, long? Size, bool IsInline);

/// <summary>Provider-neutral email message, shaped identically for Outlook and Gmail.</summary>
public sealed record EmailMessage
{
    public required string Id { get; init; }
    public string? ThreadId { get; init; }
    public string? Subject { get; init; }
    public EmailAddress? From { get; init; }
    public IReadOnlyList<EmailAddress> To { get; init; } = Array.Empty<EmailAddress>();
    public IReadOnlyList<EmailAddress> Cc { get; init; } = Array.Empty<EmailAddress>();
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = Array.Empty<EmailAddress>();
    public DateTimeOffset? ReceivedAt { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public bool IsRead { get; init; }
    public bool HasAttachments { get; init; }
    public string? Preview { get; init; }

    /// <summary>Body text. Present on read (full/truncated), omitted on list responses.</summary>
    public string? Body { get; init; }
    public string? BodyContentType { get; init; }
    public bool BodyTruncated { get; init; }

    public IReadOnlyList<AttachmentInfo> Attachments { get; init; } = Array.Empty<AttachmentInfo>();
    public string? WebLink { get; init; }
}

/// <summary>A page of messages plus a continuation cursor and truncation flag.</summary>
public sealed record MailPage
{
    public IReadOnlyList<EmailMessage> Messages { get; init; } = Array.Empty<EmailMessage>();
    public string? NextPageToken { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>Fields for composing a draft or sending inline.</summary>
public sealed record OutgoingMessage
{
    public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Bcc { get; init; } = Array.Empty<string>();
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public bool BodyIsHtml { get; init; }
}

/// <summary>Result of creating a draft.</summary>
public sealed record DraftResult(string DraftId, string? MessageId, string? WebLink);

/// <summary>Result of a send.</summary>
public sealed record SendResult(string? MessageId, bool Sent);
