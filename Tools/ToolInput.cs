using System.Globalization;

namespace MailCalMCPSharp.Tools;

/// <summary>Small helpers for normalizing MCP-friendly string inputs into typed values.</summary>
internal static class ToolInput
{
    /// <summary>Split a comma/semicolon-separated list into trimmed, non-empty items.</summary>
    public static IReadOnlyList<string> List(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>Parse an ISO-8601 date/time (assumed UTC when unqualified). Throws a clear error on bad input.</summary>
    public static DateTimeOffset Date(string value, string paramName)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto;
        }
        throw new ArgumentException($"'{paramName}' must be an ISO-8601 date/time (e.g. 2026-08-04T09:00:00Z). Got: '{value}'.", paramName);
    }

    /// <summary>Parse an optional ISO-8601 date/time; null/blank returns null.</summary>
    public static DateTimeOffset? OptionalDate(string? value, string paramName)
        => string.IsNullOrWhiteSpace(value) ? null : Date(value, paramName);
}
