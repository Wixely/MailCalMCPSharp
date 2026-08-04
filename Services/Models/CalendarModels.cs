namespace MailCalMCPSharp.Services.Models;

/// <summary>Provider-neutral calendar descriptor.</summary>
public sealed record CalendarInfo(string Id, string Name, bool IsDefault, bool CanEdit);

/// <summary>Provider-neutral event attendee.</summary>
public sealed record Attendee(string? Name, string Address, string? ResponseStatus, string? Type);

/// <summary>Provider-neutral calendar event, shaped identically for Outlook and Gmail.</summary>
public sealed record CalendarEvent
{
    public required string Id { get; init; }
    public string? CalendarId { get; init; }
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public bool BodyIsHtml { get; init; }
    public string? Location { get; init; }
    public DateTimeOffset? Start { get; init; }
    public DateTimeOffset? End { get; init; }
    public bool IsAllDay { get; init; }
    public string? TimeZone { get; init; }
    public EmailAddress? Organizer { get; init; }
    public IReadOnlyList<Attendee> Attendees { get; init; } = Array.Empty<Attendee>();
    public string? OnlineMeetingUrl { get; init; }
    public string? Status { get; init; }
    public bool IsCancelled { get; init; }
    public string? WebLink { get; init; }
}

/// <summary>A page of events plus a continuation cursor and truncation flag.</summary>
public sealed record EventPage
{
    public IReadOnlyList<CalendarEvent> Events { get; init; } = Array.Empty<CalendarEvent>();
    public string? NextPageToken { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>Fields for creating or updating an event. Null fields are left unchanged on update.</summary>
public sealed record EventInput
{
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public bool BodyIsHtml { get; init; }
    public string? Location { get; init; }
    public DateTimeOffset? Start { get; init; }
    public DateTimeOffset? End { get; init; }
    public bool IsAllDay { get; init; }
    public string? TimeZone { get; init; }
    public IReadOnlyList<string> Attendees { get; init; } = Array.Empty<string>();
    public bool CreateOnlineMeeting { get; init; }
}

/// <summary>How an invitee responds to an event.</summary>
public enum EventResponse
{
    Accept,
    Decline,
    Tentative,
}
