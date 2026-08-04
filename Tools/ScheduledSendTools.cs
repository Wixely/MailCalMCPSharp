using System.ComponentModel;
using System.Text.Json;
using MailCalMCPSharp.Services;
using MailCalMCPSharp.Services.Models;
using ModelContextProtocol.Server;

namespace MailCalMCPSharp.Tools;

/// <summary>
/// Scheduled (deferred) send, where the provider supports it natively. Outlook does (Exchange
/// deferred delivery); Gmail does not, so the tool returns a clear "not supported by Gmail".
/// </summary>
[McpServerToolType]
public sealed class ScheduledSendTools
{
    [McpServerTool(Name = "mail_schedule_send"),
     Description("Send an email at a future time using the provider's native deferred delivery (Outlook only; Gmail is not supported and returns a clear message). Blocked in read-only mode.")]
    public static async Task<string> ScheduleSend(
        AccountRegistry svc,
        [Description("Comma-separated recipient addresses.")] string to,
        [Description("Subject line.")] string subject,
        [Description("Message body.")] string body,
        [Description("When to send, ISO-8601 (e.g. 2026-08-05T09:00:00Z). Must be in the future.")] string sendAt,
        [Description("Comma-separated CC addresses.")] string? cc = null,
        [Description("Comma-separated BCC addresses.")] string? bcc = null,
        [Description("If true, body is treated as HTML; otherwise plain text.")] bool bodyIsHtml = false,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        svc.EnsureScheduledSendEnabled();
        svc.EnsureWriteAllowed("mail_schedule_send");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.ScheduledSend, "scheduled send");

        var when = ToolInput.Date(sendAt, nameof(sendAt));
        if (when <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("sendAt must be in the future.", nameof(sendAt));
        }

        var message = new OutgoingMessage
        {
            To = ToolInput.List(to),
            Cc = ToolInput.List(cc),
            Bcc = ToolInput.List(bcc),
            Subject = subject,
            Body = body,
            BodyIsHtml = bodyIsHtml,
        };
        var result = await acct.Mail.ScheduleSendAsync(message, when, ct);
        return JsonSerializer.Serialize(new { result, scheduledFor = when }, JsonOpts.Default);
    }
}
