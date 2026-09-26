using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text.Json;
using FounderScout.Application;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Responses;

namespace FounderScout.Infrastructure.Ai;

/// <summary>Owns the factory-backed HTTP lifetime for onboarding provider probes.</summary>
public sealed class FounderProviderReadinessProbeHost : IAsyncDisposable
{
    private readonly ServiceProvider services;

    private FounderProviderReadinessProbeHost(ServiceProvider services)
    {
        this.services = services;
        Probe = new OpenAiResponsesProviderReadinessProbe(services.GetRequiredService<IHttpClientFactory>());
    }

    /// <summary>Gets the one-shot provider probe.</summary>
    public IFounderProviderReadinessProbe Probe { get; }

    /// <summary>Creates a short-timeout client factory. Retry remains disabled by the SDK adapter.</summary>
    public static FounderProviderReadinessProbeHost Create()
    {
        var collection = new ServiceCollection();
        collection.AddHttpClient(OpenAiResponsesProviderReadinessProbe.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeBusinessAssistant-FounderScout-Onboarding/1.0");
        });
        return new(collection.BuildServiceProvider());
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => services.DisposeAsync();
}

/// <summary>Official Responses SDK one-shot readiness check for OpenAI and Azure OpenAI v1.</summary>
#pragma warning disable OPENAI001 // Responses is the selected official SDK surface; isolated by ADR-0010.
public sealed class OpenAiResponsesProviderReadinessProbe(IHttpClientFactory httpClientFactory) : IFounderProviderReadinessProbe
{
    internal const string HttpClientName = "founder-onboarding-provider-check";
    private const string ReadinessSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": { "ok": { "type": "boolean" } },
          "required": ["ok"]
        }
        """;

    /// <inheritdoc />
    public async ValueTask<FounderProviderReadinessResult> TestAsync(
        FounderProviderReadinessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (request.Settings.Provider is not ("OpenAI" or "AzureOpenAI")
            || string.IsNullOrWhiteSpace(request.Settings.Deployment)
            || string.IsNullOrWhiteSpace(request.ApiKey)
            || request.Timeout <= TimeSpan.Zero)
        {
            return Result(FounderProviderReadinessCode.InvalidConfiguration, "founder-scout.provider.configuration-invalid", "Provider, model/deployment, protected key, or timeout is invalid.", stopwatch);
        }

        Uri endpoint;
        try
        {
            endpoint = ResolveEndpoint(request.Settings);
        }
        catch (ArgumentException)
        {
            return Result(FounderProviderReadinessCode.InvalidConfiguration, "founder-scout.provider.endpoint-invalid", "The provider endpoint is invalid.", stopwatch);
        }

        HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);
        var options = new ResponsesClientOptions
        {
            Endpoint = endpoint,
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(0),
            NetworkTimeout = request.Timeout,
            UserAgentApplicationId = "HomeBusinessAssistant-FounderScout-Onboarding",
        };
        var client = new ResponsesClient(new ApiKeyCredential(request.ApiKey), options);
        var responseOptions = new CreateResponseOptions
        {
            Model = request.Settings.Deployment,
            MaxOutputTokenCount = 32,
            StoredOutputEnabled = false,
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "provider_readiness",
                    BinaryData.FromString(ReadinessSchema),
                    jsonSchemaIsStrict: true),
            },
        };
        responseOptions.InputItems.Add(ResponseItem.CreateSystemMessageItem("Return the requested readiness JSON only."));
        responseOptions.InputItems.Add(ResponseItem.CreateUserMessageItem("Return {\"ok\":true}. This is a provider connectivity check and contains no person or profile data."));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            ResponseResult response = await client.CreateResponseAsync(responseOptions, timeout.Token).ConfigureAwait(false);
            string output = response.GetOutputText();
            using JsonDocument document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("ok", out JsonElement ok)
                || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !ok.GetBoolean())
            {
                return Result(FounderProviderReadinessCode.MalformedResponse, "founder-scout.provider.response-malformed", "The provider returned an unexpected readiness response.", stopwatch);
            }
            return Result(FounderProviderReadinessCode.Success, "founder-scout.provider.ready", "The provider accepted the minimal structured readiness request.", stopwatch);
        }
        catch (ClientResultException exception) when (exception.Status == 401)
        {
            return Result(FounderProviderReadinessCode.InvalidCredential, "founder-scout.provider.invalid-credential", "The provider rejected the protected credential.", stopwatch);
        }
        catch (ClientResultException exception) when (exception.Status == 403)
        {
            return Result(FounderProviderReadinessCode.Forbidden, "founder-scout.provider.forbidden", "The provider denied this request.", stopwatch);
        }
        catch (ClientResultException exception) when (exception.Status == 404)
        {
            return Result(FounderProviderReadinessCode.DeploymentNotFound, "founder-scout.provider.deployment-not-found", "The configured endpoint or model/deployment was not found.", stopwatch);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            return Result(FounderProviderReadinessCode.QuotaOrRateLimit, "founder-scout.provider.quota-or-rate-limit", "The provider reported a quota or rate limit.", stopwatch);
        }
        catch (ClientResultException exception) when (exception.Status == 0 || exception.InnerException is HttpRequestException)
        {
            return Result(FounderProviderReadinessCode.NetworkOrTls, "founder-scout.provider.network-or-tls", "The provider readiness request encountered a network or TLS failure.", stopwatch);
        }
        catch (ClientResultException)
        {
            return Result(FounderProviderReadinessCode.MalformedResponse, "founder-scout.provider.request-rejected", "The provider rejected or could not process the readiness request.", stopwatch);
        }
        catch (JsonException)
        {
            return Result(FounderProviderReadinessCode.MalformedResponse, "founder-scout.provider.response-malformed", "The provider returned malformed structured output.", stopwatch);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(FounderProviderReadinessCode.NetworkOrTls, "founder-scout.provider.timeout", "The provider readiness request timed out.", stopwatch);
        }
        catch (HttpRequestException)
        {
            return Result(FounderProviderReadinessCode.NetworkOrTls, "founder-scout.provider.network-or-tls", "The provider readiness request encountered a network or TLS failure.", stopwatch);
        }
    }

    private static Uri ResolveEndpoint(FounderScoutAiSettings settings)
    {
        string endpoint = settings.Endpoint.Trim();
        if (settings.Provider == "OpenAI")
        {
            return EnsureHttps(endpoint.Length == 0 ? "https://api.openai.com/v1" : endpoint);
        }
        if (settings.Provider != "AzureOpenAI" || endpoint.Length == 0 || !string.IsNullOrWhiteSpace(settings.ApiVersion))
        {
            throw new ArgumentException("Azure OpenAI requires an HTTPS v1 endpoint and implicit versioning.", nameof(settings));
        }
        string normalized = endpoint.TrimEnd('/');
        if (!normalized.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)) normalized += "/openai/v1";
        return EnsureHttps(normalized);
    }

    private static Uri EnsureHttps(string value)
    {
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("A credential-free HTTPS provider endpoint is required.", nameof(value));
        }
        return uri;
    }

    private static FounderProviderReadinessResult Result(FounderProviderReadinessCode code, string reasonCode, string message, Stopwatch stopwatch) =>
        new(code, reasonCode, message, stopwatch.Elapsed);
}
#pragma warning restore OPENAI001
