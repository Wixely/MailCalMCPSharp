using System.ComponentModel;
using System.Text.Json;
using MailCalMCPSharp.Services;
using MailCalMCPSharp.Services.Models;
using ModelContextProtocol.Server;

namespace MailCalMCPSharp.Tools;

/// <summary>
/// Provider-agnostic inbox rule / filter tools (Outlook message rules / Gmail filters). A
/// pragmatic common subset of conditions (from, subject) and actions (move, mark read, delete).
/// </summary>
[McpServerToolType]
public sealed class MailRulesTools
{
    [McpServerTool(Name = "mail_list_rules"),
     Description("List inbox rules (Outlook) or filters (Gmail) for an account.")]
    public static async Task<string> ListRules(
        AccountRegistry svc,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureRulesEnabled();
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.MailRules, "mail rules");
        var rules = await acct.Rules.ListRulesAsync(ct);
        return JsonSerializer.Serialize(rules, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_apply_rule"),
     Description("Create an inbox rule/filter. Provide at least one condition (fromContains/subjectContains) and one action (moveToFolderId/markAsRead/delete). Blocked in read-only mode.")]
    public static async Task<string> ApplyRule(
        AccountRegistry svc,
        [Description("Rule display name (Outlook). Ignored by Gmail filters.")] string? name = null,
        [Description("Match when the sender contains this text.")] string? fromContains = null,
        [Description("Match when the subject contains this text.")] string? subjectContains = null,
        [Description("Action: move matching mail to this folder/label id.")] string? moveToFolderId = null,
        [Description("Action: mark matching mail as read.")] bool markAsRead = false,
        [Description("Action: delete/trash matching mail.")] bool delete = false,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureRulesEnabled();
        svc.EnsureWriteAllowed("mail_apply_rule");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.MailRules, "mail rules");

        if (string.IsNullOrWhiteSpace(fromContains) && string.IsNullOrWhiteSpace(subjectContains))
        {
            throw new ArgumentException("Provide at least one condition: fromContains or subjectContains.");
        }
        if (string.IsNullOrWhiteSpace(moveToFolderId) && !markAsRead && !delete)
        {
            throw new ArgumentException("Provide at least one action: moveToFolderId, markAsRead, or delete.");
        }

        var rule = await acct.Rules.CreateRuleAsync(new MailRuleInput
        {
            Name = name,
            FromContains = fromContains,
            SubjectContains = subjectContains,
            MoveToFolderId = moveToFolderId,
            MarkAsRead = markAsRead,
            Delete = delete,
        }, ct);
        return JsonSerializer.Serialize(rule, JsonOpts.Default);
    }

    [McpServerTool(Name = "mail_delete_rule"),
     Description("Delete an inbox rule/filter by id. Blocked in read-only mode.")]
    public static async Task<string> DeleteRule(
        AccountRegistry svc,
        [Description("Rule/filter id.")] string ruleId,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureRulesEnabled();
        svc.EnsureWriteAllowed("mail_delete_rule");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.MailRules, "mail rules");
        await acct.Rules.DeleteRuleAsync(ruleId, ct);
        return JsonSerializer.Serialize(new { ruleId, deleted = true }, JsonOpts.Default);
    }
}
