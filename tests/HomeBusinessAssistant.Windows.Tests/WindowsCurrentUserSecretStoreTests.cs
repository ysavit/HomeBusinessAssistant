using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Windows.Secrets;

namespace HomeBusinessAssistant.Windows.Tests;

internal sealed class WindowsCurrentUserSecretStoreTests
{
    private static readonly string[] ExpectedAuditActions = ["secret.set", "secret.deleted"];

    [Test]
    public async Task DpapiStoreRoundTripsEncryptedFileAndAuditsSetAndDelete()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var audit = new RecordingAuditWriter();
            var store = new WindowsCurrentUserSecretStore(root, audit);
            SecretReference reference = SecretReference.Parse("secret://tests/dpapi-value");
            const string secret = "test-secret-material";

            await store.SetAsync(reference, secret);
            string[] files = Directory.GetFiles(Path.Combine(root, "secrets"), "*.secret");
            byte[] encrypted = await File.ReadAllBytesAsync(files.Single());
            string? roundTrip = await store.GetAsync(reference);
            bool deleted = await store.DeleteAsync(reference);

            Assert.Multiple(() =>
            {
                Assert.That(files, Has.Length.EqualTo(1));
                Assert.That(Path.GetFileName(files[0]), Does.Not.Contain("dpapi-value"));
                Assert.That(encrypted, Is.Not.EqualTo(Encoding.UTF8.GetBytes(secret)));
                Assert.That(roundTrip, Is.EqualTo(secret));
                Assert.That(deleted, Is.True);
                Assert.That(File.Exists(files[0]), Is.False);
                Assert.That(audit.Requests.Select(item => item.Action),
                    Is.EqualTo(ExpectedAuditActions));
                Assert.That(audit.Requests.All(item => !item.TargetId.Contains("dpapi-value", StringComparison.Ordinal)), Is.True);
            });
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public void FailedAuditCompensatesNewSecretFile()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var store = new WindowsCurrentUserSecretStore(root, new FailingAuditWriter());
            SecretReference reference = SecretReference.Parse("secret://tests/compensation");

            Assert.ThrowsAsync<InvalidOperationException>(async () => await store.SetAsync(reference, "test-value"));
            Assert.That(Directory.GetFiles(Path.Combine(root, "secrets"), "*.secret"), Is.Empty);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task CorruptedCiphertextFailsClosedWithoutReturningContent()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var store = new WindowsCurrentUserSecretStore(root, new RecordingAuditWriter());
            SecretReference reference = SecretReference.Parse("secret://tests/corrupted");
            await store.SetAsync(reference, "test-value");
            string path = Directory.GetFiles(Path.Combine(root, "secrets"), "*.secret").Single();
            byte[] bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 0xff;
            await File.WriteAllBytesAsync(path, bytes);

            Assert.ThrowsAsync<InvalidOperationException>(async () => await store.GetAsync(reference));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-secret-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        string fullRoot = Path.GetFullPath(root);
        if (!Path.GetFileName(fullRoot).StartsWith("hba-secret-tests-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected test directory.");
        }

        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<WriteAuditEventRequest> Requests { get; } = [];

        public ValueTask<AuditEventRecord> WriteAsync(
            WriteAuditEventRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(new AuditEventRecord(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                request.ActorType,
                request.ActorId,
                request.Action,
                request.TargetType,
                request.TargetId,
                request.Outcome,
                request.CorrelationId,
                request.RunId,
                JsonSerializer.Serialize(request.Data)));
        }
    }

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public ValueTask<AuditEventRecord> WriteAsync(
            WriteAuditEventRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<AuditEventRecord>(new InvalidOperationException("Expected test audit failure."));
    }
}
