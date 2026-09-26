using System.Reflection;
using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Files;
using FounderScout.Infrastructure.Persistence;

namespace FounderScout.Tests;

internal sealed class FounderScoutProcessingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] FullRoles = ["Operations", "Sales / Marketing", "Product"];
    private static readonly string[] TractionClaims = ["Three shops completed paid pilots.", "Five letters of intent."];
    private static readonly string[] CofounderSkills = ["Software architecture", "Product engineering"];
    private static readonly string[] Industries = ["Local services", "Workflow software"];
    private static readonly string[] HashSkillsOne = ["Sales", "Operations"];
    private static readonly string[] HashSkillsTwo = ["Operations", "Sales"];
    private static readonly string[] SalesOperations = ["sales", "operations"];
    private static readonly string[] SalesOnly = ["sales"];
    private static readonly string[] UnpaidReason = ["screen.filter.unpaidImplementationLabor"];
    private static readonly string[] OperationalRoles = ["operations", "sales", "product"];
    private static readonly string[] WorkflowIndustries = ["local services", "workflow"];

    [Test]
    public void ParserNormalizesEveryFounderFieldAndExcludesProtectedContent()
    {
        FounderScoutCaptureEnvelope capture = Capture(
            "unicode-founder",
            "  José  Founder  ",
            "About: I lead customer discovery.\r\nAge: PROTECTED_AGE_41\r\nGender: PROTECTED_GENDER\r\nProblem: Independent shops lose time.\r\nCustomer: Independent shops.",
            new
            {
                location = "Austin, USA",
                timezone = "US Central",
                technical = "non-technical",
                commitment = "part-time with a transition plan",
                ideaPosture = "committed",
                roles = FullRoles,
                about = "I lead customer discovery and partnerships.",
                career = "Built a regional services business.",
                education = "Business operations program.",
                building = "Built workflow prototypes.",
                leadership = "Led a ten-person operations team.",
                startup = "Workflow software for independent shops.",
                problem = "Independent shops lose time on manual handoffs.",
                customer = "Owner-operated service businesses.",
                solution = "A shared local workflow hub.",
                traction = TractionClaims,
                cofounderSkills = CofounderSkills,
                cofounderRole = "Technical co-founder / CTO",
                cofounderCommitment = "full-time",
                equity = "Equal co-founder partnership is expected.",
                industries = Industries,
                trustedSourceId = "source-123",
            });

        FounderProfileParseResult parsed = new DeterministicFounderProfileParser().Parse(capture);
        NormalizedFounderProfile profile = parsed.Profile ?? throw new AssertionException("Expected a valid normalized profile.");
        FounderRedactionResult redacted = new DeterministicProfileRedactor().Redact(profile, parsed.Evidence);
        string normalizedJson = FounderProfileCanonicalizer.Canonicalize(profile);
        string evaluatorJson = FounderProfileCanonicalizer.Canonicalize(redacted.Input);
        string[] forbiddenNames = ["Age", "Gender", "Race", "Ethnicity", "Religion", "Photo", "Image", "Marital", "Family", "Health", "Disability", "SexualOrientation"];

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Health.IsHealthy, Is.True);
            Assert.That(profile.DisplayName, Is.EqualTo("José Founder"));
            Assert.That(profile.TechnicalStatus, Is.EqualTo(TechnicalProfileStatus.NonTechnical));
            Assert.That(profile.CommitmentStatus, Is.EqualTo(FounderCommitmentStatus.OpenToFullTime));
            Assert.That(profile.Roles, Does.Contain(FounderRole.Operations));
            Assert.That(profile.Roles, Does.Contain(FounderRole.SalesMarketing));
            Assert.That(profile.TractionClaims, Has.Count.EqualTo(2));
            Assert.That(profile.DesiredCofounderRole, Does.Contain("CTO"));
            Assert.That(profile.EquityPosture, Does.Contain("Equal"));
            Assert.That(profile.MissingFields, Does.Not.Contain("traction"));
            Assert.That(normalizedJson, Does.Not.Contain("PROTECTED_AGE_41"));
            Assert.That(evaluatorJson, Does.Not.Contain("PROTECTED_GENDER"));
            Assert.That(typeof(NormalizedFounderProfile).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name), Has.None.Matches<string>(name => forbiddenNames.Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase))));
        });
    }

    [Test]
    public void CanonicalHashesAreStableForUnicodeWhitespaceSetsAndIgnoreRawHtml()
    {
        FounderScoutCaptureEnvelope first = Capture("hash", "Jos\u00e9 Founder", "About: Builds tools.\r\nProblem: Slow handoffs.", new { skills = HashSkillsOne, location = "Remote" }) with { RawHtml = "<main class='one'>layout</main>" };
        FounderScoutCaptureEnvelope equivalent = Capture("HASH", "Jose\u0301   Founder", "About:   Builds tools.\nProblem: Slow handoffs.", new { location = "Remote", skills = HashSkillsTwo }) with { ProfileUrl = "https://example.invalid/profile/hash", RawHtml = "<main class='two'>different layout</main>" };
        FounderScoutCaptureEnvelope htmlOnly = first with { RawHtml = "<main class='three'>different html only</main>" };
        DeterministicFounderProfileParser parser = new();
        NormalizedFounderProfile one = parser.Parse(first).Profile!;
        NormalizedFounderProfile two = parser.Parse(equivalent).Profile!;

        Assert.Multiple(() =>
        {
            Assert.That(FounderProfileCanonicalizer.HashCanonical(one), Is.EqualTo(FounderProfileCanonicalizer.HashCanonical(two)));
            Assert.That(FounderProfileCanonicalizer.HashRaw(first), Is.Not.EqualTo(FounderProfileCanonicalizer.HashRaw(equivalent)), "Raw hashes preserve structured capture changes such as source array order.");
            Assert.That(FounderProfileCanonicalizer.HashRaw(first), Is.EqualTo(FounderProfileCanonicalizer.HashRaw(htmlOnly)), "Raw HTML layout is not evaluator content.");
            Assert.That(FounderScoutIdentityNormalizer.NormalizeUrl("https://EXAMPLE.invalid/profile/hash?tracking=1#fragment"), Is.EqualTo("https://example.invalid/profile/hash"));
            Assert.That(FounderProfileCanonicalizer.HashCanonical(one with { ProblemDescription = "A relevant change." }), Is.Not.EqualTo(FounderProfileCanonicalizer.HashCanonical(one)));
        });
    }

    [Test]
    public void ScreeningProducesGroundedVersionedOutcomesAndMissingEvidence()
    {
        DeterministicFounderProfileParser parser = new();
        DeterministicProfileRedactor redactor = new();
        DeterministicFounderScreeningEngine engine = new();
        FounderProfileParseResult parsed = parser.Parse(Capture("screen", "Screen Founder", "About: Non-technical operator seeking a technical co-founder CTO.\nProblem: Shops lose time.\nCustomer: Local shops.\nTraction: Three paid pilots.", new { location = "Austin, USA", technical = "non-technical", commitment = "full-time", roles = SalesOperations, problem = "Shops lose time.", customer = "Local shops.", traction = "Three paid pilots.", cofounderRole = "Technical co-founder CTO", equity = "equal partnership" }));
        FounderRedactionResult input = redactor.Redact(parsed.Profile!, parsed.Evidence);
        FounderScreeningRules defaultRules = new("rules-test-1", 65, 45, true, true, true, true);
        FounderScreeningResult deep = engine.Screen(input.Input, input, defaultRules);
        FounderScreeningResult monitored = engine.Screen(input.Input, input, defaultRules with { DeepAnalysisThreshold = 100, MonitorThreshold = 0, PreferNonTechnical = false, RequireLocationCompatibility = false });
        FounderProfileParseResult unpaidParsed = parser.Parse(Capture("unpaid", "Labor Founder", "About: Seeking a free developer to work for equity only.\nProblem: Need an app.", new { location = "Remote", skills = SalesOnly }));
        FounderRedactionResult unpaidInput = redactor.Redact(unpaidParsed.Profile!, unpaidParsed.Evidence);
        FounderScreeningResult filtered = engine.Screen(unpaidInput.Input, unpaidInput, defaultRules);

        Assert.Multiple(() =>
        {
            Assert.That(deep.Outcome, Is.EqualTo(ScreeningOutcome.DeepAnalyze));
            Assert.That(deep.RulesetVersion, Is.EqualTo("rules-test-1"));
            Assert.That(deep.ReasonCodes, Does.Contain("screen.preferred.clearCustomerProblem"));
            Assert.That(monitored.Outcome, Is.EqualTo(ScreeningOutcome.Monitor));
            Assert.That(filtered.Outcome, Is.EqualTo(ScreeningOutcome.FilteredOut));
            Assert.That(filtered.ReasonCodes, Is.EquivalentTo(UnpaidReason));
            Assert.That(deep.MissingEvidence, Does.Not.Contain("traction"));
            Assert.That(filtered.MissingEvidence, Does.Not.Contain("traction"), "A hard-filter result does not invent a zero-traction claim.");
        });
    }

    [Test]
    public void ParserHealthAndIdentitySignalsRemainDeterministicAndConservative()
    {
        DeterministicFounderProfileParser parser = new();
        FounderProfileParseResult failed = parser.Parse(Capture("short", "Short", "tiny", new { unknown = "value" }));
        FounderProfileParseResult parsed = parser.Parse(Capture("identity", "Identity Founder", "About: Leads operations and customer partnerships for independent service businesses.", new { location = "Chicago, USA", roles = SalesOperations, trustedSourceId = "stable-88" }));
        CandidateIdentityResolution identity = new CandidateIdentityResolver().Resolve(parsed.Profile!, Capture("identity", "Identity Founder", "About: Leads operations and customer partnerships for independent service businesses.", new { location = "Chicago, USA", roles = SalesOperations, trustedSourceId = "stable-88" }).StructuredFields, "account-two");

        Assert.Multiple(() =>
        {
            Assert.That(failed.Profile, Is.Null);
            Assert.That(failed.Health.ReasonCodes, Does.Contain("parser.visibleText.tooShort"));
            Assert.That(identity.Signals.Any(signal => signal.AliasType == CandidateIdentityAliasType.TrustedSourceId && signal.IsStrong), Is.True);
            Assert.That(identity.Signals.Any(signal => signal.AliasType == CandidateIdentityAliasType.Fingerprint), Is.True);
            Assert.That(identity.FingerprintIsStrong, Is.False, "Incomplete profiles remain weak even when a deterministic fingerprint can be formed.");
        });
    }

    [Test]
    public void RedactionPreservesBusinessLanguageFalsePositives()
    {
        FounderProfileParseResult parsed = new DeterministicFounderProfileParser().Parse(Capture(
            "false-positive",
            "Business Founder",
            "About: We build gender-neutral product defaults for healthcare operations teams.\nProblem: Clinics lose time on workflow handoffs.",
            new { location = "Remote", roles = OperationalRoles, about = "We build gender-neutral product defaults for healthcare operations teams.", problem = "Clinics lose time on workflow handoffs." }));
        FounderRedactionResult redacted = new DeterministicProfileRedactor().Redact(parsed.Profile!, parsed.Evidence);
        string json = FounderProfileCanonicalizer.Canonicalize(redacted.Input);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("gender-neutral product"));
            Assert.That(json, Does.Contain("healthcare operations"));
            Assert.That(redacted.RequiresManualReview, Is.False);
        });
    }

    [Test]
    public async Task StrongFingerprintMergesAndWeakAmbiguityCreatesVisibleConflict()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        FounderScoutImportService importer = CreateImporter(fixture, repository);
        FounderScoutProcessingService processor = CreateProcessor(fixture, repository);
        DateTimeOffset atUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        FounderScoutCaptureEnvelope strongOne = RichCapture("strong-one", 0, atUtc) with { DisplayName = "Shared Strong Founder", SourceAccountId = "account-one" };
        FounderScoutCaptureEnvelope strongTwo = RichCapture("strong-two", 0, atUtc.AddSeconds(1)) with { DisplayName = "Shared Strong Founder", SourceAccountId = "account-two" };
        string strongPath = Path.Combine(fixture.Database.ImportsDirectory, "strong-fingerprint.json");
        await File.WriteAllTextAsync(strongPath, JsonSerializer.Serialize(new[] { strongOne, strongTwo }, JsonOptions));
        _ = await importer.ImportAsync(new(strongPath, Guid.NewGuid(), "strong-import", 30));
        FounderScoutProcessingBatchResult strong = await processor.ProcessAsync(new(10, "strong-worker", Guid.NewGuid(), "strong-process", FounderScoutProcessingSettings.Default));
        FounderScoutDomainCounts afterStrong = await repository.GetCountsAsync();
        CandidatePage strongPage = await repository.QueryRankedAsync(new(null, null, 0, 10));
        IReadOnlyList<ProfileSnapshot> mergedSnapshots = await repository.ListForCandidateAsync(strongPage.Items.Single().Id, 10);

        FounderScoutCaptureEnvelope weakOne = Capture("weak-one", "Shared Weak Founder", "About: Leads operations partnerships for local businesses.", new { location = "Remote", roles = SalesOnly }, atUtc.AddMinutes(1)) with { SourceAccountId = "account-one" };
        FounderScoutCaptureEnvelope weakTwo = Capture("weak-two", "Shared Weak Founder", "About: Leads operations partnerships for local businesses.", new { location = "Remote", roles = SalesOnly }, atUtc.AddMinutes(1).AddSeconds(1)) with { SourceAccountId = "account-two" };
        string weakPath = Path.Combine(fixture.Database.ImportsDirectory, "weak-fingerprint.json");
        await File.WriteAllTextAsync(weakPath, JsonSerializer.Serialize(new[] { weakOne, weakTwo }, JsonOptions));
        _ = await importer.ImportAsync(new(weakPath, Guid.NewGuid(), "weak-import", 30));
        FounderScoutProcessingBatchResult weak = await processor.ProcessAsync(new(10, "weak-worker", Guid.NewGuid(), "weak-process", FounderScoutProcessingSettings.Default));
        IReadOnlyList<CandidateIdentityConflictRecord> conflicts = await repository.ListOpenAsync(20);

        Assert.Multiple(() =>
        {
            Assert.That(strong.Completed, Is.EqualTo(2));
            Assert.That(afterStrong.Candidates, Is.EqualTo(1));
            Assert.That(mergedSnapshots, Has.Count.EqualTo(2));
            Assert.That(weak.Completed, Is.EqualTo(2));
            Assert.That(weak.IdentityConflicts, Is.EqualTo(1));
            Assert.That(conflicts, Has.Count.EqualTo(1));
            Assert.That(conflicts[0].ReasonCode, Is.EqualTo("identity.weakAmbiguous"));
        });
    }

    [Test]
    public async Task RepeatedParserFailuresStopBatchAndPauseAccountSegment()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        _ = await repository.UpsertAsync(new BrowserAccount("parser-account", "Parser Account", "browser/parser-account", true, BrowserSessionStatus.Healthy, ["parser-segment"], nowUtc, null, null, null, null, null, nowUtc, nowUtc, 1));
        _ = await repository.UpsertAsync(new DiscoverySegment("parser-segment", "Parser Segment", true, 10, "{}", "parser-account", null, 0, 0, 0, 0, 0, null, nowUtc, nowUtc, 1));
        FounderScoutCaptureEnvelope[] broken = Enumerable.Range(0, 4).Select(index => Capture($"broken-{index}", $"Broken {index}", "tiny", new { unknown = "value" }, nowUtc.AddSeconds(index)) with { SourceAccountId = "parser-account", SourceSegmentId = "parser-segment" }).ToArray();
        string path = Path.Combine(fixture.Database.ImportsDirectory, "parser-failures.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(broken, JsonOptions));
        _ = await CreateImporter(fixture, repository).ImportAsync(new(path, Guid.NewGuid(), "parser-import", 30));
        FounderScoutProcessingBatchResult result = await CreateProcessor(fixture, repository).ProcessAsync(new(10, "parser-worker", Guid.NewGuid(), "parser-process", FounderScoutProcessingSettings.Default with { MaximumConsecutiveParserFailures = 3 }));
        BrowserAccount account = await ((IBrowserAccountRepository)repository).GetAsync("parser-account") ?? throw new AssertionException("Expected parser account.");
        DiscoverySegment segment = await ((IDiscoverySegmentRepository)repository).GetAsync("parser-segment") ?? throw new AssertionException("Expected parser segment.");

        Assert.Multiple(() =>
        {
            Assert.That(result.StoppedForParserHealth, Is.True);
            Assert.That(result.ParserFailures, Is.EqualTo(3));
            Assert.That(account.SessionStatus, Is.EqualTo(BrowserSessionStatus.ParserFailure));
            Assert.That(segment.PausedUntilUtc, Is.EqualTo(DateTimeOffset.MaxValue));
        });
    }

    [Test]
    public async Task ProcessingBatchIsDurableIdempotentRedactedAndRequeuesOnlyRelevantChanges()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        FounderScoutImportService importer = CreateImporter(fixture, repository);
        string initialPath = Path.Combine(fixture.Database.ImportsDirectory, "stage-11-twenty.json");
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        FounderScoutCaptureEnvelope[] captures = Enumerable.Range(0, 18).Select(index => RichCapture($"candidate-{index:00}", index, capturedAtUtc)).ToArray();
        FounderScoutCaptureEnvelope duplicate = RichCapture("candidate-00", 0, capturedAtUtc) with { SourceAccountId = "fixture-account-two" };
        FounderScoutCaptureEnvelope changed = RichCapture("candidate-01", 1, capturedAtUtc.AddMinutes(1), problem: "A materially changed workflow problem.") with { RawText = "About: I lead operations and customer discovery.\nProblem: A materially changed workflow problem.\nCustomer: Local service businesses.\nAge: PROTECTED_AGE_MARKER" };
        await File.WriteAllTextAsync(initialPath, JsonSerializer.Serialize(captures.Concat([duplicate, changed]), JsonOptions));
        FounderScoutImportResult imported = await importer.ImportAsync(new(initialPath, Guid.NewGuid(), "stage11-import", 30));
        FounderScoutProcessingService service = CreateProcessor(fixture, repository);
        FounderScoutProcessingSettings settings = FounderScoutProcessingSettings.Default with { MaximumConsecutiveParserFailures = 3 };
        FounderScoutProcessingBatchResult first = await service.ProcessAsync(new(50, "worker-one", Guid.NewGuid(), "stage11-screen", settings));
        FounderScoutProcessingBatchResult repeated = await service.ProcessAsync(new(50, "worker-two", Guid.NewGuid(), "stage11-rerun", settings));
        FounderScoutDomainCounts counts = await repository.GetCountsAsync();
        CandidatePage page = await repository.QueryRankedAsync(new(null, null, 0, 100));
        ProfileSnapshot[] allSnapshots = (await Task.WhenAll(page.Items.Select(async candidate => await repository.ListForCandidateAsync(candidate.Id, 10)))).SelectMany(items => items).ToArray();
        ScreeningDecision[] decisions = (await Task.WhenAll(page.Items.Select(async candidate => await repository.GetLatestAsync(candidate.Id)))).Where(item => item is not null).Cast<ScreeningDecision>().ToArray();

        string layoutPath = Path.Combine(fixture.Database.ImportsDirectory, "layout-change.json");
        FounderScoutCaptureEnvelope layout = RichCapture("candidate-02", 2, capturedAtUtc.AddMinutes(2)) with { RawText = "\r\n  Completely different layout wrapper text that remains irrelevant because semantic fields are stable.  \r\n", SourcePageFingerprint = "layout-v2" };
        await File.WriteAllTextAsync(layoutPath, JsonSerializer.Serialize(layout, JsonOptions));
        _ = await importer.ImportAsync(new(layoutPath, Guid.NewGuid(), "layout-import", 30));
        FounderScoutProcessingBatchResult layoutResult = await service.ProcessAsync(new(5, "worker-three", Guid.NewGuid(), "layout-screen", settings));

        string relevantPath = Path.Combine(fixture.Database.ImportsDirectory, "relevant-change.json");
        FounderScoutCaptureEnvelope relevant = RichCapture("candidate-03", 3, capturedAtUtc.AddMinutes(3), problem: "A newly validated and relevant customer problem.");
        await File.WriteAllTextAsync(relevantPath, JsonSerializer.Serialize(relevant, JsonOptions));
        _ = await importer.ImportAsync(new(relevantPath, Guid.NewGuid(), "relevant-import", 30));
        FounderScoutProcessingBatchResult relevantResult = await service.ProcessAsync(new(5, "worker-four", Guid.NewGuid(), "relevant-screen", settings));

        Candidate beforeParserFailure = page.Items.Single(candidate => candidate.CurrentSourceProfileKey == "candidate-04");
        string invalidPath = Path.Combine(fixture.Database.ImportsDirectory, "invalid-change.json");
        FounderScoutCaptureEnvelope invalid = Capture("candidate-04", beforeParserFailure.DisplayName, "tiny", new { unknown = "layout drift" }) with { CapturedAtUtc = capturedAtUtc.AddMinutes(4) };
        await File.WriteAllTextAsync(invalidPath, JsonSerializer.Serialize(invalid, JsonOptions));
        _ = await importer.ImportAsync(new(invalidPath, Guid.NewGuid(), "invalid-import", 30));
        FounderScoutProcessingBatchResult parserFailure = await service.ProcessAsync(new(1, "worker-five", Guid.NewGuid(), "invalid-screen", settings));
        Candidate afterParserFailure = await ((ICandidateRepository)repository).GetAsync(beforeParserFailure.Id) ?? throw new AssertionException("Expected the candidate after parser failure.");

        Assert.Multiple(() =>
        {
            Assert.That(imported.CapturesRead, Is.EqualTo(20));
            Assert.That(imported.CandidatesCreated, Is.EqualTo(18));
            Assert.That(imported.DuplicateSnapshots, Is.EqualTo(1));
            Assert.That(first.Claimed, Is.EqualTo(19));
            Assert.That(first.Completed, Is.EqualTo(19));
            Assert.That(repeated.Claimed, Is.Zero);
            Assert.That(counts.Candidates, Is.EqualTo(18));
            Assert.That(counts.ScreeningDecisions, Is.EqualTo(19));
            Assert.That(decisions, Has.Length.EqualTo(18));
            Assert.That(decisions, Has.All.Matches<ScreeningDecision>(decision => decision.EvaluatorInputHash.Length == 64));
            Assert.That(decisions, Has.All.Matches<ScreeningDecision>(decision => !decision.EvaluatorInputJson.Contains("PROTECTED_AGE_MARKER", StringComparison.Ordinal)));
            Assert.That(allSnapshots.Where(snapshot => snapshot.NormalizedProfileJson is not null), Has.All.Matches<ProfileSnapshot>(snapshot => !snapshot.NormalizedProfileJson!.Contains("PROTECTED_", StringComparison.Ordinal)));
            Assert.That(layoutResult.Unchanged, Is.EqualTo(1));
            Assert.That(layoutResult.DeepAnalysisQueued + layoutResult.Monitored + layoutResult.FilteredOut + layoutResult.ManualReview, Is.Zero);
            Assert.That(relevantResult.Changed, Is.EqualTo(1));
            Assert.That(relevantResult.DeepAnalysisQueued + relevantResult.Monitored + relevantResult.FilteredOut + relevantResult.ManualReview, Is.EqualTo(1));
            Assert.That(parserFailure.ParserFailures, Is.EqualTo(1));
            Assert.That(afterParserFailure.CurrentSnapshotId, Is.EqualTo(beforeParserFailure.CurrentSnapshotId));
            Assert.That(afterParserFailure.Status, Is.EqualTo(beforeParserFailure.Status));
            Assert.That(first.ReasonCodeDistribution, Is.Not.Empty);
        });
    }

    [Test]
    public async Task ProcessingClaimsHaveOneOwnerExpireAndManualOverrideIsAudited()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        FounderScoutRepository seed = fixture.CreateRepository(clock);
        FounderScoutImportService importer = CreateImporter(fixture, seed, clock);
        string path = Path.Combine(fixture.Database.ImportsDirectory, "claim.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(RichCapture("claim-one", 1, clock.GetUtcNow()), JsonOptions));
        _ = await importer.ImportAsync(new(path, Guid.NewGuid(), "claim-import", 30));
        FounderScoutRepository firstRepository = fixture.CreateRepository(clock);
        FounderScoutRepository secondRepository = fixture.CreateRepository(clock);
        FounderScoutProcessingClaim?[] claims = await Task.WhenAll(
            firstRepository.ClaimPendingProcessingAsync("first", TimeSpan.FromSeconds(10)).AsTask(),
            secondRepository.ClaimPendingProcessingAsync("second", TimeSpan.FromSeconds(10)).AsTask());
        FounderScoutProcessingClaim owned = claims.Single(claim => claim is not null)!;
        clock.Advance(TimeSpan.FromSeconds(11));
        FounderScoutProcessingClaim? reclaimed = await secondRepository.ClaimPendingProcessingAsync("recovery", TimeSpan.FromMinutes(1));
        _ = await secondRepository.FailProcessingAsync(new(reclaimed!.Snapshot.Id, "recovery", "test.release", true, Guid.NewGuid(), "claim-test"));
        FounderScoutProcessingBatchResult processed = await CreateProcessor(fixture, secondRepository).ProcessAsync(new(1, "final", Guid.NewGuid(), "claim-process", FounderScoutProcessingSettings.Default));
        Candidate candidate = (await secondRepository.QueryRankedAsync(new(null, null, 0, 10))).Items.Single();
        ProfileSnapshot snapshot = (await secondRepository.ListForCandidateAsync(candidate.Id, 10)).Single();
        ScreeningDecision overridden = await secondRepository.OverrideScreeningAsync(new(candidate.Id, snapshot.Id, ScreeningOutcome.Monitor, "manual.override.test", "test-user", Guid.NewGuid(), "override-test"));
        IReadOnlyList<CandidateAction> actions = await secondRepository.ListAsync(candidate.Id, 100);

        Assert.Multiple(() =>
        {
            Assert.That(claims.Count(claim => claim is not null), Is.EqualTo(1));
            Assert.That(owned.WorkerId, Is.AnyOf("first", "second"));
            Assert.That(reclaimed.WorkerId, Is.EqualTo("recovery"));
            Assert.That(processed.Completed, Is.EqualTo(1));
            Assert.That(overridden.IsManualOverride, Is.True);
            Assert.That(overridden.Outcome, Is.EqualTo(ScreeningOutcome.Monitor));
            Assert.That(actions.Any(action => action.ActionType == CandidateActionType.ScreeningOverridden), Is.True);
        });
    }

    private static FounderScoutImportService CreateImporter(TemporaryFounderScoutDatabase fixture, FounderScoutRepository repository, TimeProvider? timeProvider = null) => new(
        new FounderScoutFixtureFileReader(fixture.Database.ImportsDirectory, timeProvider ?? TimeProvider.System),
        new FounderScoutRawArtifactStore(fixture.Database.DataDirectory, fixture.Database.SnapshotsDirectory),
        repository,
        repository,
        timeProvider ?? TimeProvider.System);

    private static FounderScoutProcessingService CreateProcessor(TemporaryFounderScoutDatabase fixture, FounderScoutRepository repository) => new(
        repository,
        new FounderScoutRawArtifactStore(fixture.Database.DataDirectory, fixture.Database.SnapshotsDirectory),
        new DeterministicFounderProfileParser(),
        new DeterministicProfileRedactor(),
        new CandidateIdentityResolver(),
        new DeterministicFounderScreeningEngine());

    private static FounderScoutCaptureEnvelope RichCapture(string key, int index, DateTimeOffset atUtc, string? problem = null) => Capture(
        key,
        $"Synthetic Founder {index:00}",
        $"About: I lead operations and customer discovery.\nProblem: {problem ?? "Local businesses lose time on manual handoffs."}\nCustomer: Local service businesses.\nAge: PROTECTED_AGE_MARKER",
        new
        {
            location = index % 2 == 0 ? "Austin, USA" : "Remote US Central",
            technical = index % 3 == 0 ? "non-technical" : "unknown",
            commitment = index % 4 == 0 ? "full-time" : "open to full-time with transition plan",
            roles = OperationalRoles,
            about = "I lead operations and customer discovery for independent service businesses.",
            problem = problem ?? "Local businesses lose time on manual handoffs.",
            customer = "Independent local service businesses.",
            solution = "A shared workflow hub.",
            traction = index % 5 == 0 ? "Two paid pilots." : null,
            cofounderRole = "Technical co-founder CTO",
            equity = "Equal founder partnership.",
            industries = WorkflowIndustries,
        }, atUtc);

    private static FounderScoutCaptureEnvelope Capture(string key, string name, string rawText, object structuredFields, DateTimeOffset? atUtc = null)
    {
        JsonElement structured = JsonSerializer.SerializeToElement(structuredFields, JsonOptions);
        return new(
            FounderScoutCaptureEnvelope.CurrentSchemaVersion,
            "fixture",
            "fixture-account",
            "fixture-segment",
            key,
            $"https://example.invalid/profile/{key}",
            (atUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            name,
            rawText,
            structured,
            "fixture-1.0");
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now = now.Add(amount);
    }
}
