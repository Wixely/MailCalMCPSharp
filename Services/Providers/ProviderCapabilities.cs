namespace MailCalMCPSharp.Services.Providers;

/// <summary>
/// What a given provider account can actually do. Tools consult these flags and return a clean
/// "not supported by &lt;provider&gt;" message instead of failing obscurely when a capability is
/// absent (e.g. Gmail has no native scheduled-send API, calendar full-text search differs).
/// </summary>
public sealed record ProviderCapabilities
{
    /// <summary>Server-side message search (Graph $search / Gmail q).</summary>
    public bool MailSearch { get; init; } = true;

    /// <summary>Native deferred/scheduled delivery exposed by the provider API.</summary>
    public bool ScheduledSend { get; init; }

    /// <summary>Server-side inbox rules / filters management.</summary>
    public bool MailRules { get; init; }

    /// <summary>Text search over calendar events (vs. time-window listing only).</summary>
    public bool CalendarTextSearch { get; init; } = true;

    /// <summary>Contacts CRUD.</summary>
    public bool Contacts { get; init; } = true;

    /// <summary>Permanent (hard) delete of messages, distinct from move-to-trash.</summary>
    public bool PermanentDelete { get; init; } = true;
}
