using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Files;
using FounderScout.Infrastructure.Persistence;

namespace FounderScout.Tests;

internal sealed class FounderScoutImportTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    [Test]
    public async Task FixtureImportPersistsArtifactsBeforeQueueingAndIsIdempotent()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        FounderScoutImportService service = CreateService(fixture, repository);
        string inputPath = Path.Combine(fixture.Database.ImportsDirectory, "three-candidates.json");
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        FounderScoutCaptureEnvelope first = CreateCapture("candidate-001", "Candidate One", capturedAtUtc, "Builds local workflow tools.");
        FounderScoutCaptureEnvelope second = CreateCapture("candidate-002", "Candidate Two", capturedAtUtc, "Operates a profitable design studio.");
        FounderScoutCaptureEnvelope third = CreateCapture("candidate-003", "Candidate Three", capturedAtUtc, "Exploring a B2B services idea.");
        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(new[] { first, second, third, first }, JsonOptions));

        Guid firstRunId = Guid.NewGuid();
        FounderScoutImportResult initial = await service.ImportAsync(new(inputPath, firstRunId, "fixture-import-1", 30));
        Guid secondRunId = Guid.NewGuid();
        FounderScoutImportResult repeated = await service.ImportAsync(new(inputPath, secondRunId, "fixture-import-2", 30));
        FounderScoutDomainCounts counts = await repository.GetCountsAsync();

        foreach (Guid candidateId in initial.CandidateIds)
        {
            Candidate candidate = await ((ICandidateRepository)repository).GetAsync(candidateId)
                ?? throw new InvalidOperationException("Imported candidate was not persisted.");
            IReadOnlyList<ProfileSnapshot> snapshots = await repository.ListForCandidateAsync(candidateId, 10);
            Assert.Multiple(() =>
            {
                Assert.That(candidate.Status, Is.EqualTo(CandidateStatus.Captured));
                Assert.That(snapshots, Has.Count.EqualTo(1));
                Assert.That(File.Exists(Path.Combine(fixture.Database.DataDirectory, snapshots[0].RawArtifactRelativePath)), Is.True);
            });
        }

        IReadOnlyList<CandidateAction> firstCandidateActions = await repository.ListAsync(initial.CandidateIds[0], 100);
        Assert.Multiple(() =>
        {
            Assert.That(initial.CapturesRead, Is.EqualTo(4));
            Assert.That(initial.CandidatesCreated, Is.EqualTo(3));
            Assert.That(initial.CandidatesMatched, Is.EqualTo(1));
            Assert.That(initial.SnapshotsCreated, Is.EqualTo(3));
            Assert.That(initial.DuplicateSnapshots, Is.EqualTo(1));
            Assert.That(initial.RawArtifactsCreated, Is.EqualTo(3));
            Assert.That(repeated.CandidatesCreated, Is.Zero);
            Assert.That(repeated.CandidatesMatched, Is.EqualTo(4));
            Assert.That(repeated.SnapshotsCreated, Is.Zero);
            Assert.That(repeated.DuplicateSnapshots, Is.EqualTo(4));
            Assert.That(repeated.RawArtifactsCreated, Is.Zero);
            Assert.That(counts.Candidates, Is.EqualTo(3));
            Assert.That(counts.Snapshots, Is.EqualTo(3));
            Assert.That(counts.PendingAnalysis, Is.Zero);
            Assert.That(firstCandidateActions.Any(action => action.RelatedRunId == firstRunId), Is.True);
            Assert.That(firstCandidateActions.Any(action => action.RelatedRunId == secondRunId), Is.True);
        });
    }

    [Test]
    public async Task FixtureReaderRejectsPathsOutsideImportRootAndProtectedStructuredFields()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        var reader = new FounderScoutFixtureFileReader(fixture.Database.ImportsDirectory, TimeProvider.System);
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        string outsidePath = Path.Combine(fixture.Root, "outside.json");
        string protectedPath = Path.Combine(fixture.Database.ImportsDirectory, "protected.json");
        await File.WriteAllTextAsync(outsidePath, JsonSerializer.Serialize(CreateCapture("outside", "Outside", capturedAtUtc, "Outside."), JsonOptions));
        FounderScoutCaptureEnvelope protectedCapture = CreateCapture(
            "protected",
            "Protected",
            capturedAtUtc,
            "Fixture.",
            "{\"skills\":[\"sales\"],\"gender\":\"not-collected\"}");
        await File.WriteAllTextAsync(protectedPath, JsonSerializer.Serialize(protectedCapture, JsonOptions));

        Assert.Multiple(() =>
        {
            Assert.That(async () => await reader.ReadAsync(outsidePath), Throws.TypeOf<InvalidDataException>());
            Assert.That(async () => await reader.ReadAsync(protectedPath), Throws.TypeOf<InvalidDataException>());
        });
    }

    private static FounderScoutImportService CreateService(
        TemporaryFounderScoutDatabase fixture,
        FounderScoutRepository repository) => new(
            new FounderScoutFixtureFileReader(fixture.Database.ImportsDirectory, TimeProvider.System),
            new FounderScoutRawArtifactStore(fixture.Database.DataDirectory, fixture.Database.SnapshotsDirectory),
            repository,
            repository,
            TimeProvider.System);

    private static FounderScoutCaptureEnvelope CreateCapture(
        string sourceKey,
        string displayName,
        DateTimeOffset capturedAtUtc,
        string rawText,
        string structuredFieldsJson = "{\"skills\":[\"operations\"],\"location\":\"Remote\"}")
    {
        using JsonDocument structured = JsonDocument.Parse(structuredFieldsJson);
        return new(
            FounderScoutCaptureEnvelope.CurrentSchemaVersion,
            "fixture",
            "fixture-account",
            "fixture-segment",
            sourceKey,
            $"https://example.invalid/profile/{sourceKey}",
            capturedAtUtc.ToUniversalTime(),
            displayName,
            rawText,
            structured.RootElement.Clone(),
            "fixture-1.0");
    }
}
