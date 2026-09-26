using System.Net;
using System.Text;
using FounderScout.Application;
using FounderScout.Infrastructure.Ai;
using Moq;

namespace FounderScout.Tests;

internal sealed class FounderScoutOnboardingTests
{
    [Test]
    public void SpecializedAdapterDeclaresStableCompleteSequence()
    {
        var configurations = new Mock<HomeBusinessAssistant.Application.Persistence.IAgentConfigurationService>();
        var secrets = new Mock<HomeBusinessAssistant.Application.Secrets.ISecretStore>();
        var adapter = new FounderScoutOnboardingAdapter(configurations.Object, secrets.Object);

        Assert.Multiple(() =>
        {
            Assert.That(adapter.Descriptor.AdapterId, Is.EqualTo("founder-scout.specialized"));
            Assert.That(adapter.Descriptor.AdapterVersion, Is.EqualTo("20.1"));
            Assert.That(adapter.Descriptor.Steps.Select(item => item.Key), Is.EqualTo(new[]
            {
                FounderScoutOnboardingSteps.Purpose,
                FounderScoutOnboardingSteps.BrowserAccount,
                FounderScoutOnboardingSteps.Source,
                FounderScoutOnboardingSteps.Policy,
                FounderScoutOnboardingSteps.Ai,
                FounderScoutOnboardingSteps.Checks,
                FounderScoutOnboardingSteps.Review,
            }));
        });
    }

    [TestCase(HttpStatusCode.Unauthorized, FounderProviderReadinessCode.InvalidCredential, "founder-scout.provider.invalid-credential")]
    [TestCase(HttpStatusCode.Forbidden, FounderProviderReadinessCode.Forbidden, "founder-scout.provider.forbidden")]
    [TestCase(HttpStatusCode.NotFound, FounderProviderReadinessCode.DeploymentNotFound, "founder-scout.provider.deployment-not-found")]
    [TestCase(HttpStatusCode.TooManyRequests, FounderProviderReadinessCode.QuotaOrRateLimit, "founder-scout.provider.quota-or-rate-limit")]
    public async Task ProviderProbeClassifiesSafeHttpFailures(HttpStatusCode status, FounderProviderReadinessCode expected, string reasonCode)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"error\":{\"message\":\"sensitive upstream text\"}}", Encoding.UTF8, "application/json"),
        });
        var probe = new OpenAiResponsesProviderReadinessProbe(new TestHttpClientFactory(handler));

        FounderProviderReadinessResult result = await probe.TestAsync(new(
            new("OpenAI", "https://api.openai.test/v1/", "gpt-test", "secret://founder-scout/test", 10),
            "test-secret-value",
            TimeSpan.FromSeconds(10)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(expected));
            Assert.That(result.ReasonCode, Is.EqualTo(reasonCode));
            Assert.That(result.Message, Does.Not.Contain("sensitive upstream text"));
            Assert.That(result.Message, Does.Not.Contain("test-secret-value"));
            Assert.That(handler.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OpenAiProbeUsesMinimalProfileFreePayloadWithoutRetry()
    {
        var handler = new RecordingHandler(_ => Success());
        var probe = new OpenAiResponsesProviderReadinessProbe(new TestHttpClientFactory(handler));

        FounderProviderReadinessResult result = await probe.TestAsync(new(
            new("OpenAI", "https://api.openai.test/v1/", "gpt-test", "secret://founder-scout/test", 10),
            "test-secret-value",
            TimeSpan.FromSeconds(10)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(FounderProviderReadinessCode.Success));
            Assert.That(handler.CallCount, Is.EqualTo(1));
            Assert.That(handler.LastUri?.AbsolutePath, Is.EqualTo("/v1/responses"));
            Assert.That(handler.LastBody, Does.Contain("provider connectivity check"));
            Assert.That(handler.LastBody, Does.Not.Contain("persona").IgnoreCase);
            Assert.That(handler.LastBody, Does.Not.Contain("candidate").IgnoreCase);
            Assert.That(handler.LastBody, Does.Not.Contain("profile data\"").IgnoreCase);
            Assert.That(handler.LastBody, Does.Not.Contain("test-secret-value"));
        });
    }

    [Test]
    public async Task AzureProbeNormalizesV1RouteAndUsesDeploymentAsModel()
    {
        var handler = new RecordingHandler(_ => Success());
        var probe = new OpenAiResponsesProviderReadinessProbe(new TestHttpClientFactory(handler));

        FounderProviderReadinessResult result = await probe.TestAsync(new(
            new("AzureOpenAI", "https://example-resource.openai.azure.com", "founder-deployment", "secret://founder-scout/test", 10),
            "test-secret-value",
            TimeSpan.FromSeconds(10)));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(handler.LastUri?.AbsolutePath, Is.EqualTo("/openai/v1/responses"));
            Assert.That(handler.LastBody, Does.Contain("founder-deployment"));
            Assert.That(handler.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ProviderProbeClassifiesNetworkFailureWithoutRetry()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("synthetic TLS failure"));
        var probe = new OpenAiResponsesProviderReadinessProbe(new TestHttpClientFactory(handler));

        FounderProviderReadinessResult result = await probe.TestAsync(new(
            new("OpenAI", "https://api.openai.test/v1/", "gpt-test", "secret://founder-scout/test", 10),
            "test-secret-value",
            TimeSpan.FromSeconds(10)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(FounderProviderReadinessCode.NetworkOrTls));
            Assert.That(result.ReasonCode, Is.EqualTo("founder-scout.provider.network-or-tls"));
            Assert.That(handler.CallCount, Is.EqualTo(1));
        });
    }

    private static HttpResponseMessage Success() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""
            {
              "id":"resp_stage20","object":"response","created_at":1788283200,"status":"completed",
              "error":null,"incomplete_details":null,"instructions":null,"max_output_tokens":32,"model":"gpt-test",
              "output":[{"id":"msg_stage20","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","annotations":[],"logprobs":[],"text":"{\"ok\":true}"}]}],
              "parallel_tool_calls":true,"previous_response_id":null,"reasoning":{"effort":null,"summary":null},"store":false,
              "temperature":1.0,"text":{"format":{"type":"json_schema","name":"provider_readiness","schema":{"type":"object"},"strict":true}},
              "tool_choice":"auto","tools":[],"top_p":1.0,"truncation":"disabled",
              "usage":{"input_tokens":10,"input_tokens_details":{"cached_tokens":0},"output_tokens":5,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":15},"metadata":{}
            }
            """, Encoding.UTF8, "application/json"),
    };

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? LastUri { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }
}
