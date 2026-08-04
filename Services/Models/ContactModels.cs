namespace MailCalMCPSharp.Services.Models;

// Contacts are modelled in v1 so the provider abstraction is complete, but the tools are
// deferred to v2 (see MailCalOptions.EnableContacts).

/// <summary>Provider-neutral contact.</summary>
public sealed record Contact
{
    public required string Id { get; init; }
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public IReadOnlyList<string> Emails { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Phones { get; init; } = Array.Empty<string>();
    public string? Company { get; init; }
    public string? JobTitle { get; init; }
}

/// <summary>Fields for creating or updating a contact. Null fields are left unchanged on update.</summary>
public sealed record ContactInput
{
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public IReadOnlyList<string> Emails { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Phones { get; init; } = Array.Empty<string>();
    public string? Company { get; init; }
    public string? JobTitle { get; init; }
}

/// <summary>A page of contacts plus a continuation cursor and truncation flag.</summary>
public sealed record ContactPage
{
    public IReadOnlyList<Contact> Contacts { get; init; } = Array.Empty<Contact>();
    public string? NextPageToken { get; init; }
    public bool Truncated { get; init; }
}
