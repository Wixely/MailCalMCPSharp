using System.ComponentModel;
using System.Text.Json;
using MailCalMCPSharp.Services;
using MailCalMCPSharp.Services.Models;
using ModelContextProtocol.Server;

namespace MailCalMCPSharp.Tools;

/// <summary>
/// Provider-agnostic contact tools (Outlook contacts / Google People). Each takes an optional
/// <c>account</c> alias. Read tools are always available; write tools pass the read-only gate.
/// </summary>
[McpServerToolType]
public sealed class ContactTools
{
    [McpServerTool(Name = "contact_list"),
     Description("List contacts for an account, paged.")]
    public static async Task<string> List(
        AccountRegistry svc,
        [Description("Continuation token from a previous page.")] string? pageToken = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureContactsEnabled();
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Contacts, "contacts");
        var page = await acct.Contacts.ListAsync(pageToken, svc.Options.DefaultPageSize, ct);
        return JsonSerializer.Serialize(page, JsonOpts.Default);
    }

    [McpServerTool(Name = "contact_get"),
     Description("Get a single contact by id.")]
    public static async Task<string> Get(
        AccountRegistry svc,
        [Description("Provider contact id (Outlook contact id / Google resourceName like 'people/c123').")] string contactId,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureContactsEnabled();
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Contacts, "contacts");
        var contact = await acct.Contacts.GetAsync(contactId, ct);
        return JsonSerializer.Serialize(contact, JsonOpts.Default);
    }

    [McpServerTool(Name = "contact_add"),
     Description("Create a contact. Blocked in read-only mode.")]
    public static async Task<string> Add(
        AccountRegistry svc,
        [Description("Given (first) name.")] string? givenName = null,
        [Description("Surname (last name).")] string? surname = null,
        [Description("Comma-separated email addresses.")] string? emails = null,
        [Description("Comma-separated phone numbers.")] string? phones = null,
        [Description("Company / organisation name.")] string? company = null,
        [Description("Job title.")] string? jobTitle = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureContactsEnabled();
        svc.EnsureWriteAllowed("contact_add");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Contacts, "contacts");
        var contact = await acct.Contacts.AddAsync(BuildInput(givenName, surname, emails, phones, company, jobTitle), ct);
        return JsonSerializer.Serialize(contact, JsonOpts.Default);
    }

    [McpServerTool(Name = "contact_edit"),
     Description("Update a contact. Only provided fields change. Blocked in read-only mode.")]
    public static async Task<string> Edit(
        AccountRegistry svc,
        [Description("Provider contact id.")] string contactId,
        [Description("Given (first) name.")] string? givenName = null,
        [Description("Surname (last name).")] string? surname = null,
        [Description("Comma-separated email addresses (replaces the list).")] string? emails = null,
        [Description("Comma-separated phone numbers (replaces the list).")] string? phones = null,
        [Description("Company / organisation name.")] string? company = null,
        [Description("Job title.")] string? jobTitle = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureContactsEnabled();
        svc.EnsureWriteAllowed("contact_edit");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Contacts, "contacts");
        var contact = await acct.Contacts.EditAsync(contactId, BuildInput(givenName, surname, emails, phones, company, jobTitle), ct);
        return JsonSerializer.Serialize(contact, JsonOpts.Default);
    }

    [McpServerTool(Name = "contact_delete"),
     Description("Delete a contact. Blocked in read-only mode.")]
    public static async Task<string> Delete(
        AccountRegistry svc,
        [Description("Provider contact id.")] string contactId,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureContactsEnabled();
        svc.EnsureWriteAllowed("contact_delete");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Contacts, "contacts");
        await acct.Contacts.DeleteAsync(contactId, ct);
        return JsonSerializer.Serialize(new { contactId, deleted = true }, JsonOpts.Default);
    }

    private static ContactInput BuildInput(string? givenName, string? surname, string? emails, string? phones, string? company, string? jobTitle) => new()
    {
        GivenName = givenName,
        Surname = surname,
        Emails = ToolInput.List(emails),
        Phones = ToolInput.List(phones),
        Company = company,
        JobTitle = jobTitle,
    };
}
