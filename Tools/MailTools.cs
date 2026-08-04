using System.ComponentModel;
using System.Text.Json;
using MailCalMCPSharp.Services;
using MailCalMCPSharp.Services.Models;
using ModelContextProtocol.Server;

namespace MailCalMCPSharp.Tools;

/// <summary>
/// Provider-agnostic email tools. Each takes an optional <c>account</c> alias and routes through
/// the registry to Outlook or Gmail. Read tools are always available; write/delete tools pass the
/// read-only gate first. (Rules and scheduled-send tools are deferred to v2.)
/// </summary>
[McpServerToolType]
public sealed class MailTools
{
    [McpServerTool(Name = "mail_list_folders"),
     Description("List mailbox folders (Outlook) or labels (Gmail) for an account.")]
    public static async Task<string> ListFolders(
        AccountRegistry svc,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        var acct = svc.Resolve(account);
        var folders = await acct.Mail.ListFoldersAsync(ct);
        return JsonSerializer.Serialize(folders, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_read"),
     Description("Read a single email message by id, including headers and body (body truncated to MailCal:MaxBodyChars).")]
    public static async Task<string> Read(
        AccountRegistry svc,
        [Description("Provider message id.")] string messageId,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        var acct = svc.Resolve(account);
        var message = await acct.Mail.ReadAsync(messageId, svc.Options.MaxBodyChars, ct);
        return JsonSerializer.Serialize(message, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_list"),
     Description("List messages in a folder (most recent first), paged. Omit folder for the inbox.")]
    public static async Task<string> List(
        AccountRegistry svc,
        [Description("Folder/label id. Omit for the inbox.")] string? folderId = null,
        [Description("Continuation token from a previous page.")] string? pageToken = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        var acct = svc.Resolve(account);
        var page = await acct.Mail.ListAsync(folderId, pageToken, svc.Options.DefaultPageSize, ct);
        return JsonSerializer.Serialize(page, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_search"),
     Description("Search messages using the provider's query syntax (Outlook $search / Gmail q). Reports 'not supported' if the provider lacks search.")]
    public static async Task<string> Search(
        AccountRegistry svc,
        [Description("Search query, e.g. 'from:alice invoice' (Gmail) or free text (Outlook).")] string query,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.MailSearch, "mail search");
        var page = await acct.Mail.SearchAsync(query, svc.Options.DefaultPageSize, ct);
        return JsonSerializer.Serialize(page, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_compose_draft"),
     Description("Create a draft email and return its id. Does not send. Blocked in read-only mode.")]
    public static async Task<string> ComposeDraft(
        AccountRegistry svc,
        [Description("Comma-separated recipient addresses.")] string to,
        [Description("Subject line.")] string subject,
        [Description("Message body.")] string body,
        [Description("Comma-separated CC addresses.")] string? cc = null,
        [Description("Comma-separated BCC addresses.")] string? bcc = null,
        [Description("If true, body is treated as HTML; otherwise plain text.")] bool bodyIsHtml = false,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        svc.EnsureWriteAllowed("mail_compose_draft");
        var acct = svc.Resolve(account);
        var message = new OutgoingMessage
        {
            To = ToolInput.List(to),
            Cc = ToolInput.List(cc),
            Bcc = ToolInput.List(bcc),
            Subject = subject,
            Body = body,
            BodyIsHtml = bodyIsHtml,
        };
        var result = await acct.Mail.CreateDraftAsync(message, ct);
        return JsonSerializer.Serialize(result, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_send"),
     Description("Send an email — either an existing draft (pass draftId) or a new message (pass to/subject/body). Blocked in read-only mode.")]
    public static async Task<string> Send(
        AccountRegistry svc,
        [Description("Id of a draft to send. If set, the inline fields are ignored.")] string? draftId = null,
        [Description("Comma-separated recipient addresses (for a new message).")] string? to = null,
        [Description("Subject line (for a new message).")] string? subject = null,
        [Description("Message body (for a new message).")] string? body = null,
        [Description("Comma-separated CC addresses.")] string? cc = null,
        [Description("Comma-separated BCC addresses.")] string? bcc = null,
        [Description("If true, body is treated as HTML; otherwise plain text.")] bool bodyIsHtml = false,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        svc.EnsureWriteAllowed("mail_send");
        var acct = svc.Resolve(account);

        OutgoingMessage? message = null;
        if (string.IsNullOrWhiteSpace(draftId))
        {
            if (string.IsNullOrWhiteSpace(to))
            {
                throw new ArgumentException("Provide either a draftId or at least one recipient in 'to'.", nameof(to));
            }
            message = new OutgoingMessage
            {
                To = ToolInput.List(to),
                Cc = ToolInput.List(cc),
                Bcc = ToolInput.List(bcc),
                Subject = subject,
                Body = body,
                BodyIsHtml = bodyIsHtml,
            };
        }

        var result = await acct.Mail.SendAsync(message, draftId, ct);
        return JsonSerializer.Serialize(result, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_delete"),
     Description("Delete a message. By default moves to trash/deleted items; set permanent=true (requires MailCal:AllowPermanentDelete) to hard delete. Blocked in read-only mode.")]
    public static async Task<string> Delete(
        AccountRegistry svc,
        [Description("Provider message id.")] string messageId,
        [Description("If true, permanently delete instead of moving to trash. Requires MailCal:AllowPermanentDelete.")] bool permanent = false,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        svc.EnsureWriteAllowed("mail_delete");
        if (permanent)
        {
            svc.EnsurePermanentDeleteAllowed("mail_delete");
        }
        var acct = svc.Resolve(account);
        if (permanent)
        {
            AccountRegistry.EnsureCapability(acct, acct.Capabilities.PermanentDelete, "permanent delete");
        }
        await acct.Mail.DeleteAsync(messageId, permanent, ct);
        return JsonSerializer.Serialize(new { messageId, deleted = true, permanent }, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_move"),
     Description("Move a message to another folder/label. Blocked in read-only mode.")]
    public static async Task<string> Move(
        AccountRegistry svc,
        [Description("Provider message id.")] string messageId,
        [Description("Destination folder/label id.")] string destinationFolderId,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureMailEnabled();
        svc.EnsureWriteAllowed("mail_move");
        var acct = svc.Resolve(account);
        await acct.Mail.MoveAsync(messageId, destinationFolderId, ct);
        return JsonSerializer.Serialize(new { messageId, movedTo = destinationFolderId }, JsonOpts.Default);
    }
}
