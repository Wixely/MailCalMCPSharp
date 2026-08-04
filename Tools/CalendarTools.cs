using System.ComponentModel;
using System.Text.Json;
using MailCalMCPSharp.Services;
using MailCalMCPSharp.Services.Models;
using ModelContextProtocol.Server;

namespace MailCalMCPSharp.Tools;

/// <summary>
/// Provider-agnostic calendar tools. Each takes an optional <c>account</c> alias and routes
/// through the registry to Outlook or Gmail. Read tools are always available; write tools pass
/// the read-only gate first.
/// </summary>
[McpServerToolType]
public sealed class CalendarTools
{
    [McpServerTool(Name = "cal_list_calendars"),
     Description("List calendars available to an account.")]
    public static async Task<string> ListCalendars(
        AccountRegistry svc,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureCalendarEnabled();
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Calendar, "calendar");
        var calendars = await acct.Calendar.ListCalendarsAsync(ct);
        return JsonSerializer.Serialize(calendars, JsonOpts.Default);
    }

    [McpServerTool(Name = "cal_read"),
     Description("List events in a time window (ISO-8601 start/end), ordered by start, paged.")]
    public static async Task<string> Read(
        AccountRegistry svc,
        [Description("Window start, ISO-8601 (e.g. 2026-08-04T00:00:00Z).")] string start,
        [Description("Window end, ISO-8601.")] string end,
        [Description("Calendar id. Omit for the primary calendar.")] string? calendarId = null,
        [Description("Continuation token from a previous page.")] string? pageToken = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureCalendarEnabled();
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Calendar, "calendar");
        var from = ToolInput.Date(start, nameof(start));
        var to = ToolInput.Date(end, nameof(end));
        var page = await acct.Calendar.ReadAsync(calendarId, from, to, pageToken, svc.Options.DefaultPageSize, ct);
        return JsonSerializer.Serialize(page, JsonOpts.Default);
    }

    [McpServerTool(Name = "cal_get_event"),
     Description("Get a single calendar event by id.")]
    public static async Task<string> GetEvent(
        AccountRegistry svc,
        [Description("Provider event id.")] string eventId,
        [Description("Calendar id. Omit for the primary calendar.")] string? calendarId = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureCalendarEnabled();
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Calendar, "calendar");
        var ev = await acct.Calendar.GetEventAsync(calendarId, eventId, ct);
        return JsonSerializer.Serialize(ev, JsonOpts.Default);
    }

    [McpServerTool(Name = "cal_search"),
     Description("Search events by text (and optional time window). Reports 'not supported' if the provider lacks text search.")]
    public static async Task<string> Search(
        AccountRegistry svc,
        [Description("Free-text query.")] string query,
        [Description("Optional window start, ISO-8601.")] string? start = null,
        [Description("Optional window end, ISO-8601.")] string? end = null,
        [Description("Calendar id. Omit for the primary calendar.")] string? calendarId = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureCalendarEnabled();
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Calendar, "calendar");
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.CalendarTextSearch, "calendar text search");
        var from = ToolInput.OptionalDate(start, nameof(start));
        var to = ToolInput.OptionalDate(end, nameof(end));
        var page = await acct.Calendar.SearchAsync(calendarId, query, from, to, svc.Options.DefaultPageSize, ct);
        return JsonSerializer.Serialize(page, JsonOpts.Default);
    }

    [McpServerTool(Name = "cal_create_event"),
     Description("Create a calendar event. Blocked in read-only mode.")]
    public static async Task<string> CreateEvent(
        AccountRegistry svc,
        [Description("Event title/subject.")] string subject,
        [Description("Start, ISO-8601.")] string start,
        [Description("End, ISO-8601.")] string end,
        [Description("Optional body/description.")] string? body = null,
        [Description("Optional location.")] string? location = null,
        [Description("Comma-separated attendee addresses.")] string? attendees = null,
        [Description("If true, create an online meeting link where supported.")] bool onlineMeeting = false,
        [Description("If true, an all-day event.")] bool allDay = false,
        [Description("IANA time zone (e.g. Europe/Dublin). Optional.")] string? timeZone = null,
        [Description("Calendar id. Omit for the primary calendar.")] string? calendarId = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureCalendarEnabled();
        svc.EnsureWriteAllowed("cal_create_event");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Calendar, "calendar");
        var input = new EventInput
        {
            Subject = subject,
            Body = body,
            Location = location,
            Start = ToolInput.Date(start, nameof(start)),
            End = ToolInput.Date(end, nameof(end)),
            IsAllDay = allDay,
            TimeZone = timeZone,
            Attendees = ToolInput.List(attendees),
            CreateOnlineMeeting = onlineMeeting,
        };
        var ev = await acct.Calendar.CreateEventAsync(calendarId, input, ct);
        return JsonSerializer.Serialize(ev, JsonOpts.Default);
    }

    [McpServerTool(Name = "cal_update_event"),
     Description("Update an existing event. Only provided fields change. Blocked in read-only mode.")]
    public static async Task<string> UpdateEvent(
        AccountRegistry svc,
        [Description("Provider event id.")] string eventId,
        [Description("New title/subject.")] string? subject = null,
        [Description("New start, ISO-8601.")] string? start = null,
        [Description("New end, ISO-8601.")] string? end = null,
        [Description("New body/description.")] string? body = null,
        [Description("New location.")] string? location = null,
        [Description("Comma-separated attendee addresses (replaces the list).")] string? attendees = null,
        [Description("IANA time zone. Optional.")] string? timeZone = null,
        [Description("Calendar id. Omit for the primary calendar.")] string? calendarId = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureCalendarEnabled();
        svc.EnsureWriteAllowed("cal_update_event");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Calendar, "calendar");
        var input = new EventInput
        {
            Subject = subject,
            Body = body,
            Location = location,
            Start = ToolInput.OptionalDate(start, nameof(start)),
            End = ToolInput.OptionalDate(end, nameof(end)),
            TimeZone = timeZone,
            Attendees = ToolInput.List(attendees),
        };
        var ev = await acct.Calendar.UpdateEventAsync(calendarId, eventId, input, ct);
        return JsonSerializer.Serialize(ev, JsonOpts.Default);
    }

    [McpServerTool(Name = "cal_delete_event"),
     Description("Delete/cancel an event. Blocked in read-only mode.")]
    public static async Task<string> DeleteEvent(
        AccountRegistry svc,
        [Description("Provider event id.")] string eventId,
        [Description("Calendar id. Omit for the primary calendar.")] string? calendarId = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureCalendarEnabled();
        svc.EnsureWriteAllowed("cal_delete_event");
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Calendar, "calendar");
        await acct.Calendar.DeleteEventAsync(calendarId, eventId, ct);
        return JsonSerializer.Serialize(new { eventId, deleted = true }, JsonOpts.Default);
    }

    [McpServerTool(Name = "cal_respond_event"),
     Description("Respond to an event invite: accept, decline, or tentative. Blocked in read-only mode.")]
    public static async Task<string> RespondEvent(
        AccountRegistry svc,
        [Description("Provider event id.")] string eventId,
        [Description("Response: accept, decline, or tentative.")] string response,
        [Description("Optional comment to include with the response.")] string? comment = null,
        [Description("Calendar id. Omit for the primary calendar.")] string? calendarId = null,
        [Description("Account alias. Falls back to the default account.")] string? account = null,
        CancellationToken ct = default)
    {
        svc.EnsureCalendarEnabled();
        svc.EnsureWriteAllowed("cal_respond_event");
        if (!Enum.TryParse<EventResponse>(response, ignoreCase: true, out var parsed))
        {
            throw new ArgumentException("response must be one of: accept, decline, tentative.", nameof(response));
        }
        var acct = svc.Resolve(account);
        AccountRegistry.EnsureCapability(acct, acct.Capabilities.Calendar, "calendar");
        await acct.Calendar.RespondEventAsync(calendarId, eventId, parsed, comment, ct);
        return JsonSerializer.Serialize(new { eventId, response = parsed.ToString() }, JsonOpts.Default);
    }
}
