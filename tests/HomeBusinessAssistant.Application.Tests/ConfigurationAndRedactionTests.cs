using System.Text.Json;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Secrets;

namespace HomeBusinessAssistant.Application.Tests;

internal sealed class ConfigurationAndRedactionTests
{
    private static readonly string[] ExpectedSecretPolicyErrors =
    [
        "configuration.inlineSecretRejected",
        "configuration.uriCredentialsRejected",
    ];

    [Test]
    public void CanonicalJsonSortsObjectPropertiesAndPreservesArrayOrder()
    {
        using JsonDocument first = JsonDocument.Parse("{\"z\":1.0,\"nested\":{\"b\":2,\"a\":1},\"items\":[2,1]}");
        using JsonDocument second = JsonDocument.Parse("{\"items\":[2,1],\"nested\":{\"a\":1.00,\"b\":2},\"z\":1}");

        string firstCanonical = CanonicalJson.Serialize(first.RootElement);
        string secondCanonical = CanonicalJson.Serialize(second.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(firstCanonical, Is.EqualTo("{\"items\":[2,1],\"nested\":{\"a\":1,\"b\":2},\"z\":1}"));
            Assert.That(secondCanonical, Is.EqualTo(firstCanonical));
            Assert.That(CanonicalJson.ComputeHash(firstCanonical), Is.EqualTo(CanonicalJson.ComputeHash(secondCanonical)));
            Assert.That(CanonicalJson.ComputeHash(firstCanonical), Has.Length.EqualTo(64));
        });
    }

    [TestCase("secret://founder-scout/openai", true)]
    [TestCase("secret://wake-remote/pin", true)]
    [TestCase("secret://one", false)]
    [TestCase("secret://Founder/openai", false)]
    [TestCase("../secret", false)]
    public void SecretReferenceUsesOpaqueSafeSegments(string value, bool expected)
    {
        Assert.That(SecretReference.TryParse(value, out _), Is.EqualTo(expected));
    }

    [Test]
    public void ConfigurationPolicyRejectsInlineSecretsAndUriCredentials()
    {
        using JsonDocument document = JsonDocument.Parse(
            "{\"apiKey\":\"plaintext\",\"endpoint\":\"https://user:pass@example.test\",\"passwordRef\":\"secret://agent/password\"}");

        IReadOnlyList<ConfigurationValidationError> errors = ConfigurationSecretPolicy.Validate(document.RootElement);

        Assert.That(errors.Select(error => error.Code), Is.EquivalentTo(ExpectedSecretPolicyErrors));
    }

    [Test]
    public void AuditRedactorRemovesKnownSensitiveValuesAndBoundsPayloads()
    {
        using JsonDocument document = JsonDocument.Parse(
            "{\"password\":\"do-not-store\",\"reference\":\"secret://agent/password\",\"header\":\"Bearer token-value\",\"uri\":\"https://user:pass@example.test/path\"}");

        string redacted = AuditRedactor.Redact(document.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(redacted, Does.Not.Contain("do-not-store"));
            Assert.That(redacted, Does.Not.Contain("token-value"));
            Assert.That(redacted, Does.Not.Contain("user:pass"));
            Assert.That(redacted, Does.Not.Contain("agent/password"));
            Assert.That(redacted, Does.Contain("[REDACTED]"));
        });
    }

    [Test]
    public async Task InMemorySecretStoreSupportsRoundTripAndDelete()
    {
        using var store = new InMemorySecretStore();
        SecretReference reference = SecretReference.Parse("secret://tests/value");

        await store.SetAsync(reference, "test-value");
        bool exists = await store.ExistsAsync(reference);
        string? value = await store.GetAsync(reference);
        bool deleted = await store.DeleteAsync(reference);
        string? missing = await store.GetAsync(reference);

        Assert.Multiple(() =>
        {
            Assert.That(exists, Is.True);
            Assert.That(value, Is.EqualTo("test-value"));
            Assert.That(deleted, Is.True);
            Assert.That(missing, Is.Null);
        });
    }
}
