using System.Security.Claims;
using System.Security.Principal;
using FounderScout.Application;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Host.Dashboard;
using HomeBusinessAssistant.Host.Health;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace HomeBusinessAssistant.Host;

/// <summary>
/// Composes the loopback-only local web host.
/// </summary>
public static class HostApplication
{
    /// <summary>
    /// Gets the default loopback-only URL used when no URL is configured.
    /// </summary>
    public const string DefaultUrl = "http://127.0.0.1:5180";

    /// <summary>
    /// Creates a configured local web application without starting it.
    /// </summary>
    /// <param name="args">Host command-line arguments.</param>
    /// <param name="composition">Optional production desktop services; tests may use safe empty defaults.</param>
    /// <returns>The composed web application.</returns>
    public static WebApplication Build(string[] args, HostWebComposition? composition = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? executableWebRoot = HasExplicitContentRoot(args)
            ? null
            : ResolveExecutableWebRoot(AppContext.BaseDirectory);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = executableWebRoot,
        });

        // The default Windows Event Log provider attempts to create a machine-wide source
        // during local/test startup. The Host owns one explicitly redacting provider instead.
        builder.Logging.ClearProviders();
        string requestedUrl = LoopbackUrlPolicy.Validate(builder.Configuration[WebHostDefaults.ServerUrlsKey] ?? DefaultUrl);
        var localOrigin = new Uri(requestedUrl, UriKind.Absolute);
        builder.WebHost.UseUrls(requestedUrl);
        string? ownerSid = FounderScoutSimpleMode.Enabled
            ? null
            : WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("The interactive Host owner Windows SID is unavailable.");
        HostHealthState healthState = composition?.HealthState ?? new(TimeProvider.System);
        IHostReadinessEvaluator readiness = composition?.Readiness ?? new StaticHostReadinessEvaluator(isReady: true);
        IHostDashboardService dashboard = composition?.Dashboard ?? new EmptyHostDashboardService();
        IGlobalScheduleControlService globalControls = composition?.GlobalScheduleControl ?? new NoOpGlobalScheduleControlService();
        HostManagementComposition management = composition?.Management ?? new();
        if (composition?.LoggerProvider is not null)
        {
            builder.Logging.AddProvider(composition.LoggerProvider);
        }

        builder.Services.AddSingleton(healthState);
        builder.Services.AddSingleton(readiness);
        builder.Services.AddSingleton(dashboard);
        builder.Services.AddSingleton(globalControls);
        builder.Services.AddSingleton(management);
        builder.Services.AddHealthChecks()
            .AddCheck<HostLivenessHealthCheck>("host-live", tags: ["live"])
            .AddCheck<HostReadinessHealthCheck>("host-ready", tags: ["ready"]);
        builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
            .AddNegotiate();
        builder.Services.AddAuthorization(options =>
        {
            if (!FounderScoutSimpleMode.Enabled)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder(NegotiateDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => IsOwner(context.User, ownerSid!))
                    .Build();
            }
        });
        builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(HostApplication).Assembly);
        foreach (IHostedService hostedService in composition?.HostedServices ?? [])
        {
            builder.Services.AddSingleton<IHostedService>(hostedService);
        }

        WebApplication application = builder.Build();
        application.Use(async (context, next) =>
        {
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self'; img-src 'self' data:; form-action 'self'; frame-ancestors 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            if (!string.Equals(context.Request.Host.Value, localOrigin.Authority, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("The request host is not the configured loopback origin.").ConfigureAwait(false);
                return;
            }

            if (IsUnsafeMethod(context.Request.Method)
                && context.Request.Headers.Origin.Count > 0
                && (context.Request.Headers.Origin.Count != 1
                    || !string.Equals(context.Request.Headers.Origin[0], localOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("The request origin is not the configured loopback origin.").ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });
        application.UseStaticFiles();
        application.UseAuthentication();
        application.UseAuthorization();
        application.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        }).AllowAnonymous();
        application.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        }).AllowAnonymous();
        application.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        }).AllowAnonymous();
        application.MapRazorPages();
        return application;
    }

    internal static string? ResolveExecutableWebRoot(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        string candidate = Path.Combine(Path.GetFullPath(baseDirectory), "wwwroot");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static bool HasExplicitContentRoot(string[] args) =>
        args.Any(argument =>
            string.Equals(argument, "--contentRoot", StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith("--contentRoot=", StringComparison.OrdinalIgnoreCase));

    private static bool IsOwner(ClaimsPrincipal principal, string ownerSid) =>
        principal.Identity is WindowsIdentity windowsIdentity
            && string.Equals(windowsIdentity.User?.Value, ownerSid, StringComparison.OrdinalIgnoreCase)
        || principal.Claims.Any(claim => claim.Type == ClaimTypes.PrimarySid
            && string.Equals(claim.Value, ownerSid, StringComparison.OrdinalIgnoreCase));

    private static bool IsUnsafeMethod(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);
}
