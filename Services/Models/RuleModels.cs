namespace MailCalMCPSharp.Services.Models;

/// <summary>
/// Provider-neutral inbox rule / filter. Maps to Outlook message rules (Graph) and Gmail
/// filters. A pragmatic common subset of conditions and actions is modelled.
/// </summary>
public sealed record MailRule
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public bool IsEnabled { get; init; } = true;
    /// <summary>Human-readable summary of the rule's conditions and actions.</summary>
    public string? Description { get; init; }
}

/// <summary>Fields for creating a rule/filter. At least one condition and one action are required.</summary>
public sealed record MailRuleInput
{
    /// <summary>Display name (Outlook requires one; Gmail filters have no name).</summary>
    public string? Name { get; init; }

    // Conditions
    public string? FromContains { get; init; }
    public string? SubjectContains { get; init; }

    // Actions
    public string? MoveToFolderId { get; init; }
    public bool MarkAsRead { get; init; }
    public bool Delete { get; init; }
}
