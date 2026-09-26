using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Text.Json;
using FounderScout.Application;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Responses;

namespace FounderScout.Infrastructure.Ai;

/// <summary>Owns the HttpClientFactory-backed provider client lifetime.</summary>
public sealed class FounderEvaluationModelClientHost : IAsyncDisposable
{
    private readonly ServiceProvider? services;

    private FounderEvaluationModelClientHost(IFounderEvaluationModelClient client, ServiceProvider? services)
    {
        Client = client;
        this.services = services;
    }

    /// <summary>Gets the provider-neutral model client.</summary>
    public IFounderEvaluationModelClient Client { get; }

    /// <summary>Creates a production SDK client or the explicitly labeled deterministic fake.</summary>
    public static FounderEvaluationModelClientHost Create(FounderScoutAiSettings settings, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Provider == "DiagnosticFake")
        {
            return new(new DeterministicFounderEvaluationModelClient(), null);
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new FounderModelException(
                FounderModelFailureKind.Permanent,
                "analysis.provider.secretMissing",
                "The configured provider secret is unavailable.");
        }

        var collection = new ServiceCollection();
        collection.AddHttpClient("founder-evaluation", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeBusinessAssistant-FounderScout/1.0");
        });
        ServiceProvider services = collection.BuildServiceProvider();
        IHttpClientFactory factory = services.GetRequiredService<IHttpClientFactory>();
        return new(new OpenAiResponsesFounderEvaluationModelClient(factory, settings, apiKey), services);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (services is not null) await services.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Official OpenAI SDK Responses adapter shared by OpenAI and Azure OpenAI v1 endpoints.</summary>
#pragma warning disable OPENAI001 // Responses API is the selected official SDK surface; isolated by ADR-0010.
public sealed class OpenAiResponsesFounderEvaluationModelClient : IFounderEvaluationModelClient
{
    private readonly ResponsesClient client;
    private readonly FounderScoutAiSettings settings;

    /// <summary>Creates a strict Responses client over one factory-owned HttpClient.</summary>
    public OpenAiResponsesFounderEvaluationModelClient(
        IHttpClientFactory httpClientFactory,
        FounderScoutAiSettings settings,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        Uri endpoint = ResolveEndpoint(settings);
        HttpClient httpClient = httpClientFactory.CreateClient("founder-evaluation");
        var options = new ResponsesClientOptions
        {
            Endpoint = endpoint,
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(0),
            NetworkTimeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds),
            UserAgentApplicationId = "HomeBusinessAssistant-FounderScout",
        };
        client = new ResponsesClient(new ApiKeyCredential(apiKey), options);
    }

    /// <inheritdoc />
    public async Task<ModelEvaluationResponse> EvaluateAsync(
        FounderEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = new CreateResponseOptions
        {
            Model = request.ModelOrDeployment,
            MaxOutputTokenCount = request.MaximumOutputTokens,
            StoredOutputEnabled = false,
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "founder_evaluation",
                    BinaryData.FromString(FounderEvaluationJsonSchema.Json),
                    jsonSchemaIsStrict: true),
            },
        };
        options.InputItems.Add(ResponseItem.CreateSystemMessageItem(request.SystemPrompt));
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(request.CreateUserPayload()));
        try
        {
            ResponseResult response = await client.CreateResponseAsync(options, cancellationToken).ConfigureAwait(false);
            string output = response.GetOutputText();
            if (string.IsNullOrWhiteSpace(output) || output.Length > 1_048_576)
            {
                throw new FounderModelException(
                    FounderModelFailureKind.InvalidStructuredOutput,
                    "analysis.provider.outputInvalid",
                    "The provider returned empty or oversized structured output.");
            }
            try
            {
                return JsonSerializer.Deserialize<ModelEvaluationResponse>(output, StrictJsonOptions.Instance)
                    ?? throw new JsonException("The structured response is empty.");
            }
            catch (JsonException exception)
            {
                throw new FounderModelException(
                    FounderModelFailureKind.InvalidStructuredOutput,
                    "analysis.provider.schemaInvalid",
                    "The provider output does not match the strict evaluation schema.",
                    innerException: exception);
            }
        }
        catch (FounderModelException)
        {
            throw;
        }
        catch (ClientResultException exception)
        {
            throw Classify(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FounderModelException(
                FounderModelFailureKind.Transient,
                "analysis.provider.timeout",
                "The provider request timed out.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new FounderModelException(
                FounderModelFailureKind.Transient,
                "analysis.provider.network",
                "The provider request encountered a network failure.",
                innerException: exception);
        }
    }

    private static Uri ResolveEndpoint(FounderScoutAiSettings settings)
    {
        string endpoint = settings.Endpoint.Trim();
        if (settings.Provider == "OpenAI")
        {
            return endpoint.Length == 0 ? new Uri("https://api.openai.com/v1/") : EnsureTrailingSlash(endpoint);
        }
        if (settings.Provider != "AzureOpenAI" || endpoint.Length == 0)
        {
            throw new FounderModelException(
                FounderModelFailureKind.Permanent,
                "analysis.provider.endpointMissing",
                "The configured Azure OpenAI endpoint is missing.");
        }
        if (!string.IsNullOrWhiteSpace(settings.ApiVersion))
        {
            throw new FounderModelException(
                FounderModelFailureKind.Permanent,
                "analysis.provider.apiVersionUnsupported",
                "The selected Azure OpenAI v1 Responses route uses implicit versioning and cannot specify apiVersion.");
        }
        string normalized = endpoint.TrimEnd('/');
        if (!normalized.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/openai/v1";
        }
        return EnsureTrailingSlash(normalized);
    }

    private static Uri EnsureTrailingSlash(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new FounderModelException(
                FounderModelFailureKind.Permanent,
                "analysis.provider.endpointInvalid",
                "The provider endpoint is invalid.");
        }
        return uri;
    }

    private static FounderModelException Classify(ClientResultException exception)
    {
        int status = exception.Status;
        if (status is 401 or 403 or 404)
            return new(FounderModelFailureKind.Permanent, "analysis.provider.authenticationOrModel", "The provider rejected authentication, endpoint, or deployment configuration.", innerException: exception);
        if (status == 429)
            return new(FounderModelFailureKind.Transient, "analysis.provider.throttled", "The provider throttled the request.", innerException: exception);
        if (status >= 500 || status == 408)
            return new(FounderModelFailureKind.Transient, "analysis.provider.unavailable", "The provider is temporarily unavailable.", innerException: exception);
        return new(FounderModelFailureKind.Permanent, "analysis.provider.requestRejected", "The provider rejected the evaluation request.", innerException: exception);
    }

    private static class StrictJsonOptions
    {
        internal static JsonSerializerOptions Instance { get; } = new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        };
    }
}
#pragma warning restore OPENAI001

/// <summary>Explicitly labeled deterministic fake model used by normal tests and local synthetic smoke.</summary>
public sealed class DeterministicFounderEvaluationModelClient : IFounderEvaluationModelClient
{
    private readonly ConcurrentDictionary<Guid, int> calls = new();

    /// <inheritdoc />
    public Task<ModelEvaluationResponse> EvaluateAsync(
        FounderEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        int call = calls.AddOrUpdate(request.CandidateId, 1, static (_, current) => current + 1);
        string key = request.Input.SourceProfileKey.ToLowerInvariant();
        if (key.Contains("transient", StringComparison.Ordinal) && call == 1)
            throw new FounderModelException(FounderModelFailureKind.Transient, "diagnosticFake.transient", "DiagnosticFake emitted one transient failure.", TimeSpan.Zero);
        if (key.Contains("invalid-schema", StringComparison.Ordinal) && request.ValidationFeedback.Count == 0)
            throw new FounderModelException(FounderModelFailureKind.InvalidStructuredOutput, "diagnosticFake.schema", "DiagnosticFake emitted one invalid-schema result.");
        return Task.FromResult(CreateResponse(request, key));
    }

    private static ModelEvaluationResponse CreateResponse(FounderEvaluationRequest request, string key)
    {
        string fact = (request.Input.Evidence.Count > 0 ? request.Input.Evidence[0].SafeSnippet : null)
            ?? request.Input.Introduction
            ?? request.Input.Background
            ?? request.Input.Startup
            ?? "Profile evidence is limited.";
        decimal confidence = key.Contains("manual", StringComparison.Ordinal) ? 0.15m : 0.88m;
        decimal ratio = key.Contains("strong", StringComparison.Ordinal) ? 0.95m
            : key.Contains("exploratory", StringComparison.Ordinal) ? 0.78m
            : key.Contains("monitor", StringComparison.Ordinal) ? 0.66m
            : key.Contains("pass", StringComparison.Ordinal) ? 0.32m
            : 0.72m;
        ModelScoreItem[] quality = FounderEvaluationScorecard.Quality.Select(definition => new ModelScoreItem(
            definition.Key,
            Math.Clamp((int)Math.Round(definition.Maximum * ratio, MidpointRounding.AwayFromZero), 0, definition.Maximum),
            [fact],
            "The supplied profile evidence supports this bounded assessment.",
            [],
            confidence)).ToArray();
        ModelScoreItem[] fit = FounderEvaluationScorecard.Fit.Select(definition => new ModelScoreItem(
            definition.Key,
            Math.Clamp((int)Math.Round(definition.Maximum * ratio, MidpointRounding.AwayFromZero), 0, definition.Maximum),
            [fact],
            "The supplied profile and configured persona support this bounded fit assessment.",
            [],
            confidence)).ToArray();
        if (key.Contains("evidence-failure", StringComparison.Ordinal))
        {
            quality[0] = quality[0] with { Evidence = ["Raised $9999999 from 42 customers."] };
        }
        string strength = request.Persona.Strengths[0];
        string shortDraft = $"Hi {request.Input.DisplayName}, your note that {fact} stood out. I bring {strength}; I would enjoy a founder-to-founder conversation to compare notes.";
        string detailedDraft = $"Hi {request.Input.DisplayName}, I appreciated the specific context that {fact}. My complementary background includes {strength}, and I am exploring thoughtful founder-to-founder connections where ownership is clear. Would you be open to a brief conversation about {request.Input.Problem ?? request.Input.Startup ?? "the customer problem"}?";
        if (key.Contains("draft-failure", StringComparison.Ordinal))
        {
            shortDraft = "Automated scoring says we should connect.";
            detailedDraft = "This scraping and automated analysis ranked you highly, so I will join and build for free.";
        }
        var invitation = new ModelInvitationResponse(
            shortDraft,
            detailedDraft,
            [fact],
            [strength],
            request.Input.Problem ?? request.Input.Startup ?? "the customer problem",
            confidence);
        return new(
            "1.0",
            "A bounded summary based only on the supplied profile.",
            quality,
            fit,
            [],
            [fact],
            [],
            request.Input.EquityPosture is null ? ["equityPosture"] : [],
            ["What customer evidence is most important next?"],
            "The recommendation is based only on supplied evidence and explicit unknowns.",
            invitation);
    }
}
