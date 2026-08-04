using System.Net;
using MailCalMCPSharp.Configuration;
using MailCalMCPSharp.Hosting;
using MailCalMCPSharp.Services;
using MailCalMCPSharp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Serilog;

namespace MailCalMCPSharp;

public static class Program
{
    public static int Main(string[] args)
    {
        // When running as a Windows Service the working directory is C:\Windows\System32,
        // so resolve config, logs, and the token store relative to the exe.
        var contentRoot = GetContentRoot();
        var isService = WindowsServiceHelpers.IsWindowsService();

        // One-time OAuth bootstrap: `MailCalMCPSharp --auth <alias> [--auth-mode browser|devicecode]`.
        // Runs the interactive/device-code flow, writes the portable token store, and exits.
        var authIndex = Array.IndexOf(args, "--auth");
        if (authIndex >= 0)
        {
            return RunAuth(contentRoot, args, authIndex);
        }

        if (!isService)
        {
            McpSharpIcon.ApplyConsoleWindowIcon();
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(contentRoot, "logs", "mailcalmcp-bootstrap-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = contentRoot,
            });

            ConfigureAppConfiguration(builder.Configuration, contentRoot, builder.Environment.EnvironmentName, args);

            if (isService)
            {
                var svcOptions = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
                builder.Host.UseWindowsService(o => o.ServiceName = svcOptions.WindowsServiceName);
            }

            builder.Host.UseSerilog((ctx, services, cfg) => cfg
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());

            builder.Services.Configure<MailCalOptions>(builder.Configuration.GetSection(MailCalOptions.SectionName));
            builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
            builder.Services.AddSingleton<AccountRegistry>();

            // Tool surface is scoped by feature toggles so the agent only sees enabled areas.
            var mailCal = builder.Configuration.GetSection(MailCalOptions.SectionName).Get<MailCalOptions>() ?? new MailCalOptions();
            var mcp = builder.Services.AddMcpServer().WithHttpTransport();
            mcp.WithTools<AccountTools>();
            if (mailCal.EnableMail) mcp.WithTools<MailTools>();
            if (mailCal.EnableCalendar) mcp.WithTools<CalendarTools>();

            var server = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
            builder.WebHost.ConfigureKestrel(k =>
            {
                if (string.Equals(server.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    k.ListenLocalhost(server.Port);
                }
                else if (IPAddress.TryParse(server.Host, out var ip))
                {
                    k.Listen(ip, server.Port);
                }
                else
                {
                    k.ListenAnyIP(server.Port);
                }
            });

            var app = builder.Build();

            app.UseSerilogRequestLogging();

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception in AppDomain");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            var registry = app.Services.GetRequiredService<AccountRegistry>();
            var accountDetails = SummarizeAccounts(registry);
            LogStartup(
                "MailCalMCPSharp",
                $"http://{server.Host}:{server.Port}{server.Path}",
                "HTTP",
                isService ? "WindowsService" : "Console",
                contentRoot,
                new[]
                {
                    $"Read-only: {registry.IsReadOnly}",
                    $"Allow permanent delete: {registry.AllowPermanentDelete}",
                    $"Mail: {(mailCal.EnableMail ? "enabled" : "disabled")}, Calendar: {(mailCal.EnableCalendar ? "enabled" : "disabled")}",
                    $"Accounts ({registry.Aliases.Count}): {(accountDetails.Length == 0 ? "none configured" : string.Join(", ", accountDetails))}",
                });

            app.UseMiddleware<McpPasswordMiddleware>();

            app.MapFavicon();
            app.MapGet("/healthz", () => new
            {
                status = "ok",
                server = "MailCalMCPSharp",
                path = server.Path,
                readOnly = registry.IsReadOnly,
                accounts = registry.Aliases,
                timeUtc = DateTimeOffset.UtcNow,
            });
            app.MapMcp(server.Path);

            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Server terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// `--auth <alias> [--auth-mode browser|devicecode]` — build config, resolve the account's
    /// authenticator, run the sign-in flow, and exit. Console output is fine here (this is a CLI
    /// mode, not the MCP transport).
    /// </summary>
    private static int RunAuth(string contentRoot, string[] args, int authIndex)
    {
        try
        {
            var alias = authIndex + 1 < args.Length && !args[authIndex + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[authIndex + 1]
                : null;
            var mode = GetArgValue(args, "--auth-mode") ?? "browser";

            var config = new ConfigurationBuilder().Also(c =>
                ConfigureAppConfiguration(c, contentRoot, Environments.Production, args)).Build();
            var options = config.GetSection(MailCalOptions.SectionName).Get<MailCalOptions>() ?? new MailCalOptions();
            var env = new AuthHostEnvironment(contentRoot);
            var registry = new AccountRegistry(Options.Create(options), env);

            var authenticator = registry.Authenticator(alias);
            Console.WriteLine($"MailCalMCPSharp auth: account='{alias ?? registry.DefaultAlias}', mode='{mode}'.");

            var result = string.Equals(mode, "devicecode", StringComparison.OrdinalIgnoreCase)
                ? authenticator.AuthorizeDeviceCodeAsync(CancellationToken.None).GetAwaiter().GetResult()
                : authenticator.AuthorizeInteractiveAsync(CancellationToken.None).GetAwaiter().GetResult();

            Console.WriteLine($"State: {result.State}. {result.Message}");
            if (!string.IsNullOrWhiteSpace(result.VerificationUrl))
            {
                Console.WriteLine($"Open {result.VerificationUrl} and enter code {result.UserCode}.");
            }
            return result.Completed || result.State == Services.Models.AuthState.Authorized ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Authorization failed: {ex.Message}");
            return 1;
        }
    }

    private static string[] SummarizeAccounts(AccountRegistry registry)
    {
        try
        {
            var summaries = registry.ListAccountsAsync(CancellationToken.None).GetAwaiter().GetResult();
            return summaries.Select(s => $"{s.Alias}[{s.Provider}/{s.AuthState}]").ToArray();
        }
        catch
        {
            return registry.Aliases.ToArray();
        }
    }

    private static void ConfigureAppConfiguration(IConfigurationBuilder configuration, string contentRoot, string environmentName, string[] args)
    {
        configuration
            .SetBasePath(contentRoot)
            .AddJsonFile(ResolveConfigFile(contentRoot, "appsettings.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(ResolveConfigFile(contentRoot, $"appsettings.{environmentName}.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(ResolveConfigFile(contentRoot, "appsettings.Local.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(ResolveConfigFile(contentRoot, "MailCalMCPSharp.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(ResolveConfigFile(contentRoot, $"MailCalMCPSharp.{environmentName}.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(ResolveConfigFile(contentRoot, "MailCalMCPSharp.Local.json"), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddEnvironmentVariables(prefix: "MAILCALMCP_")
            .AddCommandLine(args);
    }

    private static string? GetArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void LogStartup(string serviceName, string endpoint, string transport, string mode, string contentRoot, string[] details)
    {
        var startupLog = Log.ForContext("SourceContext", serviceName + ".Startup");
        startupLog.Information("{ServiceName} startup", serviceName);
        startupLog.Information("  Endpoint: {Endpoint}", endpoint);
        startupLog.Information("  Transport: {Transport}", transport);
        startupLog.Information("  Mode: {Mode}", mode);
        foreach (var detail in details)
        {
            startupLog.Information("  {Detail}", detail);
        }
        startupLog.Information("  Content root: {ContentRoot}", contentRoot);
    }

    private static string GetContentRoot() =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    private static string ResolveConfigFile(string contentRoot, string fileName)
    {
        if (File.Exists(Path.Combine(contentRoot, fileName)))
        {
            return fileName;
        }

        try
        {
            var match = Directory.EnumerateFiles(contentRoot, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

            return match is null ? fileName : Path.GetFileName(match);
        }
        catch (DirectoryNotFoundException)
        {
            return fileName;
        }
    }

    /// <summary>Minimal <see cref="IHostEnvironment"/> for the --auth path (no web host).</summary>
    private sealed class AuthHostEnvironment : IHostEnvironment
    {
        public AuthHostEnvironment(string contentRoot)
        {
            ContentRootPath = contentRoot;
            ContentRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
        }

        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "MailCalMCPSharp";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }
}

internal static class BuilderExtensions
{
    /// <summary>Apply an action to a builder inline and return it (for terse config setup).</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
