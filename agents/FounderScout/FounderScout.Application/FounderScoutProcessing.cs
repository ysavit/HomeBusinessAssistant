using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Configuration;

namespace FounderScout.Application;

/// <summary>Founder-relevant operating roles, deliberately excluding demographic traits.</summary>
public enum FounderRole
{
    /// <summary>Design ownership.</summary>
    Design = 0,
    /// <summary>Sales, marketing, or distribution ownership.</summary>
    SalesMarketing = 1,
    /// <summary>Operational ownership.</summary>
    Operations = 2,
    /// <summary>Product ownership.</summary>
    Product = 3,
    /// <summary>Industry or customer-domain ownership.</summary>
    Domain = 4,
    /// <summary>Fundraising ownership.</summary>
    Fundraising = 5,
    /// <summary>Another explicitly stated operating role.</summary>
    Other = 6,
}

/// <summary>One safe field-level source reference retained with a normalized profile.</summary>
public sealed record FounderProfileEvidence(string Field, string SourceSection, string SafeSnippet);

/// <summary>Versioned founder profile containing only evaluation-relevant, non-protected data.</summary>
public sealed record NormalizedFounderProfile(
    string SchemaVersion,
    string SourceProfileKey,
    string? CanonicalUrl,
    string DisplayName,
    string? Location,
    string? TimeZoneCompatibility,
    TechnicalProfileStatus TechnicalStatus,
    FounderCommitmentStatus CommitmentStatus,
    IdeaCommitmentStatus IdeaCommitmentStatus,
    IReadOnlyList<FounderRole> Roles,
    string? Introduction,
    string? CareerBackground,
    string? EducationBackground,
    string? BuildingBackground,
    string? LeadershipBackground,
    string? StartupDescription,
    string? ProblemDescription,
    string? CustomerDescription,
    string? SolutionDescription,
    IReadOnlyList<string> TractionClaims,
    IReadOnlyList<string> DesiredCofounderSkills,
    string? DesiredCofounderRole,
    FounderCommitmentStatus DesiredCofounderCommitment,
    string? EquityPosture,
    IReadOnlyList<string> Industries,
    string? LastActivityText,
    DateTimeOffset? LastActivityAtUtc,
    string? LastActivityRange,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingFields,
    decimal Completeness,
    string ParserVersion,
    string SourceAdapterVersion)
{
    /// <summary>Current normalized profile schema.</summary>
    public const string CurrentSchemaVersion = "1.0";
}

/// <summary>Deterministic parser-health detail used to stop drifted source runs.</summary>
public sealed record FounderParserHealth(
    bool IsHealthy,
    int RequiredFieldGroupsFound,
    int ExpectedFieldGroups,
    int RecognizedSections,
    int VisibleTextLength,
    decimal RecognizedSectionRatio,
    IReadOnlyList<string> ReasonCodes);

/// <summary>Complete deterministic parse result.</summary>
public sealed record FounderProfileParseResult(
    NormalizedFounderProfile? Profile,
    IReadOnlyList<FounderProfileEvidence> Evidence,
    FounderParserHealth Health,
    IReadOnlyList<string> Errors);

/// <summary>Versioned deterministic profile parser.</summary>
public interface IFounderProfileParser
{
    /// <summary>Gets the parser implementation version.</summary>
    string Version { get; }
    /// <summary>Parses one capture deterministically.</summary>
    FounderProfileParseResult Parse(FounderScoutCaptureEnvelope capture);
}

/// <summary>Protected-attribute-free payload supplied to deterministic screening and Stage 12.</summary>
public sealed record FounderEvaluationInput(
    string SchemaVersion,
    string SourceProfileKey,
    string DisplayName,
    string? LocationCompatibility,
    TechnicalProfileStatus TechnicalStatus,
    FounderCommitmentStatus CommitmentStatus,
    IdeaCommitmentStatus IdeaCommitmentStatus,
    IReadOnlyList<FounderRole> Roles,
    string? Introduction,
    string? Background,
    string? Startup,
    string? Problem,
    string? Customer,
    string? Solution,
    IReadOnlyList<string> TractionClaims,
    IReadOnlyList<string> DesiredCofounderSkills,
    string? DesiredCofounderRole,
    FounderCommitmentStatus DesiredCofounderCommitment,
    string? EquityPosture,
    IReadOnlyList<string> Industries,
    IReadOnlyList<FounderProfileEvidence> Evidence)
{
    /// <summary>Current protected-attribute-free evaluator input schema.</summary>
    public const string CurrentSchemaVersion = "1.0";
}

/// <summary>Safe redaction metadata; removed values are intentionally absent.</summary>
public sealed record FounderRedactionResult(
    FounderEvaluationInput Input,
    IReadOnlyDictionary<string, int> ReasonCounts,
    decimal Confidence,
    bool RequiresManualReview,
    string RedactorVersion);

/// <summary>Builds evaluator input without protected or irrelevant attributes.</summary>
public interface IProfileRedactor
{
    /// <summary>Gets the redaction rules version.</summary>
    string Version { get; }
    /// <summary>Produces a protected-attribute-free evaluator payload.</summary>
    FounderRedactionResult Redact(NormalizedFounderProfile profile, IReadOnlyList<FounderProfileEvidence> evidence);
}

/// <summary>Configured, versioned deterministic screen policy.</summary>
public sealed record FounderScreeningRules(
    string RulesetVersion,
    int DeepAnalysisThreshold,
    int MonitorThreshold,
    bool PreferNonTechnical,
    bool RequireLocationCompatibility,
    bool HardFilterUnpaidImplementationLabor,
    bool FilterIdenticalTechnicalPreference);

/// <summary>Grounded deterministic screening output.</summary>
public sealed record FounderScreeningResult(
    ScreeningOutcome Outcome,
    decimal? Score,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<FounderProfileEvidence> Evidence,
    IReadOnlyList<string> MissingEvidence,
    string RulesetVersion);

/// <summary>Deterministic, versioned fast-screen rules.</summary>
public interface IFounderScreeningEngine
{
    /// <summary>Applies deterministic grounded rules to one evaluator input.</summary>
    FounderScreeningResult Screen(FounderEvaluationInput input, FounderRedactionResult redaction, FounderScreeningRules rules);
}

/// <summary>Safe relevant-field change summary.</summary>
public sealed record FounderProfileChange(string Field, string ChangeKind);

/// <summary>Candidate identity signals derived in documented precedence order.</summary>
public sealed record CandidateIdentityResolution(
    IReadOnlyList<CandidateIdentityInput> Signals,
    string? FingerprintHash,
    bool FingerprintIsStrong,
    IReadOnlyList<string> ReasonCodes);

/// <summary>Builds trusted and deterministic candidate identity evidence without embeddings.</summary>
public interface ICandidateIdentityResolver
{
    /// <summary>Builds trusted ID and deterministic fingerprint signals.</summary>
    CandidateIdentityResolution Resolve(NormalizedFounderProfile profile, JsonElement capturedStructuredFields, string sourceAccountId);
}

/// <summary>Stable canonicalization and SHA-256 controls for Founder Scout processing.</summary>
public static class FounderProfileCanonicalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Normalizes Unicode, line endings, and insignificant whitespace.</summary>
    public static string NormalizeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalized = value.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return string.Join('\n', normalized.Split('\n').Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).Where(line => line.Length > 0)).Trim();
    }

    /// <summary>Normalizes, deduplicates, and stably orders a semantic set.</summary>
    public static IReadOnlyList<string> NormalizeSet(IEnumerable<string> values) => values
        .Select(NormalizeText)
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ThenBy(value => value, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Serializes a value as stable-property-order canonical JSON.</summary>
    public static string Canonicalize<T>(T value) => CanonicalJson.Serialize(JsonSerializer.SerializeToElement(value, JsonOptions));

    /// <summary>Hashes the canonical JSON form of a value with SHA-256.</summary>
    public static string HashCanonical<T>(T value) => HashUtf8(Canonicalize(value));

    /// <summary>Hashes relevant captured visible and structured content.</summary>
    public static string HashRaw(FounderScoutCaptureEnvelope capture) => HashCanonical(new
    {
        rawText = NormalizeText(capture.RawText),
        structuredFields = capture.StructuredFields,
        capture.SourceAdapterVersion,
        capture.SourcePageFingerprint,
    });

    /// <summary>Hashes one UTF-8 string with SHA-256.</summary>
    public static string HashUtf8(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>Known-label parser for Startup School captures and sanitized fixtures.</summary>
public sealed class DeterministicFounderProfileParser : IFounderProfileParser
{
    private static readonly string[] ProtectedLabels = ["age", "gender", "sex", "race", "ethnicity", "religion", "marital status", "family status", "children", "disability", "health", "sexual orientation", "photo", "image"];
    private static readonly string[] ProtectedPhrases = [" years old", "my wife", "my husband", "my children", "my kids", "i am a woman", "i am a man", "my religion"];
    private static readonly string[] KnownFields = ["location", "timezone", "time zone", "technical", "commitment", "idea", "roles", "skills", "strengths", "about", "introduction", "bio", "background", "career", "education", "building", "leadership", "startup", "problem", "customer", "solution", "traction", "progress", "validation", "looking for", "cofounder skills", "co-founder skills", "cofounder role", "co-founder role", "equity", "partnership", "industries", "industry", "interests", "last active", "activity"];
    /// <summary>Current deterministic parser version.</summary>
    public const string CurrentVersion = "founder-profile-parser-1.1";
    /// <inheritdoc />
    public string Version => CurrentVersion;

    /// <inheritdoc />
    public FounderProfileParseResult Parse(FounderScoutCaptureEnvelope capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var warnings = new List<string>();
        Dictionary<string, string[]> fields = ReadFields(capture.StructuredFields);
        Dictionary<string, string> labeled = ReadLabeledText(capture.RawText);
        var evidence = new List<FounderProfileEvidence>();

        string? GetText(string field, params string[] aliases)
        {
            foreach (string alias in aliases)
            {
                if (fields.TryGetValue(alias, out string[]? values) && values.Length > 0)
                {
                    string safe = Sanitize(string.Join("\n", values), warnings);
                    if (safe.Length > 0)
                    {
                        evidence.Add(new(field, $"structured:{alias}", Bound(safe)));
                        return safe;
                    }
                }
            }

            foreach (string alias in aliases)
            {
                if (labeled.TryGetValue(alias, out string? value))
                {
                    string safe = Sanitize(value, warnings);
                    if (safe.Length > 0)
                    {
                        evidence.Add(new(field, $"label:{alias}", Bound(safe)));
                        return safe;
                    }
                }
            }

            return null;
        }

        IReadOnlyList<string> GetSet(string field, params string[] aliases)
        {
            string? value = GetText(field, aliases);
            return value is null ? [] : FounderProfileCanonicalizer.NormalizeSet(value.Split([',', ';', '\n', '|'], StringSplitOptions.RemoveEmptyEntries));
        }

        string? structuredIntroduction = GetText("introduction", "introduction", "about", "bio", "intro");
        string introduction = Sanitize(structuredIntroduction ?? capture.RawText, warnings);
        if (structuredIntroduction is null && introduction.Length > 0)
        {
            evidence.Add(new("introduction", "visible-profile-text", Bound(introduction)));
        }
        string? location = GetText("location", "location", "city", "region");
        string? timezone = GetText("timeZoneCompatibility", "timezone", "time zone", "availability timezone");
        string? technicalText = GetText("technicalStatus", "technical", "technical status", "technical background");
        string? commitmentText = GetText("commitmentStatus", "commitment", "founder commitment", "availability");
        string? ideaText = GetText("ideaCommitmentStatus", "ideaposture", "idea posture", "idea status", "idea");
        IReadOnlyList<string> skills = GetSet("roles", "roles", "skills", "strengths", "can own");
        FounderRole[] roles = ParseRoles(skills);
        string? career = GetText("careerBackground", "background", "career", "work experience", "experience");
        string? education = GetText("educationBackground", "education");
        string? building = GetText("buildingBackground", "building", "built", "projects");
        string? leadership = GetText("leadershipBackground", "leadership", "management");
        string? startup = GetText("startupDescription", "startup", "company", "venture");
        string? problem = GetText("problemDescription", "problem", "problem statement");
        string? customer = GetText("customerDescription", "customer", "customers", "target customer");
        string? solution = GetText("solutionDescription", "solution", "product");
        IReadOnlyList<string> traction = GetSet("tractionClaims", "traction", "progress", "validation", "customer traction");
        IReadOnlyList<string> desiredSkills = GetSet("desiredCofounderSkills", "cofounderskills", "cofounder skills", "co-founder skills", "looking for skills", "seeking");
        string? desiredRole = GetText("desiredCofounderRole", "looking for", "cofounderrole", "cofounder role", "co-founder role", "looking for role");
        string? desiredCommitmentText = GetText("desiredCofounderCommitment", "cofoundercommitment", "cofounder commitment", "co-founder commitment");
        string? equity = GetText("equityPosture", "equity", "partnership");
        IReadOnlyList<string> industries = GetSet("industries", "industries", "industry", "interests");
        string? activity = Sanitize(capture.LastSeenText ?? GetText("lastActivityText", "last active", "activity") ?? string.Empty, warnings);

        var missing = new List<string>();
        AddMissing(missing, "introduction", introduction);
        AddMissing(missing, "location", location);
        AddMissing(missing, "commitment", commitmentText);
        if (problem is null && startup is null)
        {
            missing.Add("problemOrStartup");
        }
        if (customer is null)
        {
            missing.Add("customer");
        }
        if (traction.Count == 0)
        {
            missing.Add("traction");
        }
        if (equity is null)
        {
            missing.Add("equity");
        }

        TechnicalProfileStatus technicalStatus = ParseTechnical(technicalText, introduction);
        if (technicalText is null && technicalStatus != TechnicalProfileStatus.Unknown)
        {
            evidence.Add(new("technicalStatus", "visible-profile-text", Bound(introduction)));
        }

        int recognizedSections = fields.Keys.Count(IsKnownField) + labeled.Keys.Count(IsKnownField);
        int sectionCount = Math.Max(1, fields.Count + labeled.Count);
        int groupsFound = (introduction.Length > 0 ? 1 : 0) + (location is not null ? 1 : 0) + (problem is not null || startup is not null ? 1 : 0) + (roles.Length > 0 || technicalText is not null || desiredRole is not null ? 1 : 0);
        decimal ratio = Math.Clamp((decimal)recognizedSections / sectionCount, 0m, 1m);
        string normalizedRaw = FounderProfileCanonicalizer.NormalizeText(capture.RawText);
        bool unstructuredFallback = groupsFound == 1
            && recognizedSections == 0
            && normalizedRaw.Length >= 200
            && capture.ExtractionCompleteness >= 0.5m
            && !string.IsNullOrWhiteSpace(capture.DisplayName)
            && !string.Equals(capture.DisplayName, "Unknown founder profile", StringComparison.Ordinal);
        if (unstructuredFallback)
        {
            warnings.Add("parser.unstructuredFallback");
        }
        var healthReasons = new List<string>();
        if (normalizedRaw.Length < 20) healthReasons.Add("parser.visibleText.tooShort");
        if (groupsFound < 2 && !unstructuredFallback) healthReasons.Add("parser.requiredGroups.missing");
        if (recognizedSections == 0 && !unstructuredFallback) healthReasons.Add("parser.sections.unrecognized");
        if (capture.SourceAdapterVersion.Length is 0 or > 128) healthReasons.Add("parser.adapter.incompatible");
        bool healthy = healthReasons.Count == 0;
        if (!healthy)
        {
            return new(null, [], new(false, groupsFound, 4, recognizedSections, normalizedRaw.Length, ratio, healthReasons), healthReasons);
        }

        decimal completeness = Math.Round(Math.Clamp((12m - missing.Count) / 12m, 0m, 1m), 4, MidpointRounding.AwayFromZero);
        var profile = new NormalizedFounderProfile(
            NormalizedFounderProfile.CurrentSchemaVersion,
            FounderScoutIdentityNormalizer.NormalizeSourceKey(capture.SourceProfileKey),
            FounderScoutIdentityNormalizer.NormalizeUrl(capture.ProfileUrl),
            FounderProfileCanonicalizer.NormalizeText(capture.DisplayName),
            NullIfEmpty(location),
            NullIfEmpty(timezone),
            technicalStatus,
            ParseCommitment(commitmentText),
            ParseIdea(ideaText),
            roles,
            NullIfEmpty(introduction),
            NullIfEmpty(career),
            NullIfEmpty(education),
            NullIfEmpty(building),
            NullIfEmpty(leadership),
            NullIfEmpty(startup),
            NullIfEmpty(problem),
            NullIfEmpty(customer),
            NullIfEmpty(solution),
            traction,
            desiredSkills,
            NullIfEmpty(desiredRole),
            ParseCommitment(desiredCommitmentText),
            NullIfEmpty(equity),
            industries,
            NullIfEmpty(activity),
            ParseReliableActivity(capture.LastSeenText),
            ParseActivityRange(capture.LastSeenText),
            FounderProfileCanonicalizer.NormalizeSet(warnings),
            FounderProfileCanonicalizer.NormalizeSet(missing),
            completeness,
            Version,
            capture.SourceAdapterVersion);
        return new(profile, evidence.Distinct().OrderBy(item => item.Field, StringComparer.Ordinal).ThenBy(item => item.SourceSection, StringComparer.Ordinal).ToArray(), new(true, groupsFound, 4, recognizedSections, normalizedRaw.Length, ratio, []), []);
    }

    internal static string Sanitize(string value, ICollection<string>? warnings = null)
    {
        var kept = new List<string>();
        foreach (string rawLine in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            string line = FounderProfileCanonicalizer.NormalizeText(rawLine);
            if (line.Length == 0) continue;
            string lower = line.ToLowerInvariant();
            int separator = lower.IndexOf(':');
            string label = separator >= 0 ? lower[..separator].Trim() : string.Empty;
            bool protectedLabel = ProtectedLabels.Contains(label, StringComparer.Ordinal);
            bool protectedPhrase = ProtectedPhrases.Any(lower.Contains);
            if (protectedLabel || protectedPhrase)
            {
                warnings?.Add(protectedLabel ? "redaction.protectedLabel" : "redaction.protectedPassage");
                continue;
            }
            kept.Add(line);
        }
        return string.Join('\n', kept);
    }

    private static Dictionary<string, string[]> ReadFields(JsonElement root)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object) return [];
        AddObject(root, result);
        if (root.TryGetProperty("sections", out JsonElement sections) && sections.ValueKind == JsonValueKind.Object) AddObject(sections, result);
        return result.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static void AddObject(JsonElement element, Dictionary<string, List<string>> fields)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.NameEquals("sections") || IsProtectedName(property.Name)) continue;
            string fieldName = CanonicalizeFieldName(property.Name);
            IEnumerable<string> values = property.Value.ValueKind switch
            {
                JsonValueKind.String => [property.Value.GetString() ?? string.Empty],
                JsonValueKind.Array => property.Value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty),
                _ => [],
            };
            if (!fields.TryGetValue(fieldName, out List<string>? target)) fields[fieldName] = target = [];
            target.AddRange(values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    private static string CanonicalizeFieldName(string value)
    {
        string key = FounderProfileCanonicalizer.NormalizeText(value).ToLowerInvariant();
        if (KnownFields.Contains(key, StringComparer.OrdinalIgnoreCase)) return key;
        if (key.Contains("looking for", StringComparison.Ordinal)
            || key.Contains("seeking", StringComparison.Ordinal)) return "looking for";
        if (key.Contains("about me", StringComparison.Ordinal)
            || key.Contains("introduction", StringComparison.Ordinal)
            || key.Contains("biography", StringComparison.Ordinal)) return "about";
        if (key.Contains("background", StringComparison.Ordinal)
            || key.Contains("experience", StringComparison.Ordinal)) return "background";
        if (key.Contains("location", StringComparison.Ordinal)
            || key.Contains("where i", StringComparison.Ordinal)) return "location";
        if (key.Contains("startup", StringComparison.Ordinal)
            || key.Contains("company", StringComparison.Ordinal)
            || key.Contains("venture", StringComparison.Ordinal)) return "startup";
        if (key.Contains("problem", StringComparison.Ordinal)) return "problem";
        if (key.Contains("customer", StringComparison.Ordinal)
            || key.Contains("market", StringComparison.Ordinal)) return "customer";
        if (key.Contains("solution", StringComparison.Ordinal)
            || key.Contains("product", StringComparison.Ordinal)) return "solution";
        if (key.Contains("traction", StringComparison.Ordinal)
            || key.Contains("progress", StringComparison.Ordinal)
            || key.Contains("validation", StringComparison.Ordinal)) return "traction";
        return key;
    }

    private static Dictionary<string, string> ReadLabeledText(string rawText)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in rawText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            int separator = line.IndexOf(':');
            if (separator is < 1 or > 80) continue;
            string key = FounderProfileCanonicalizer.NormalizeText(line[..separator]).ToLowerInvariant();
            if (IsProtectedName(key)) continue;
            string value = FounderProfileCanonicalizer.NormalizeText(line[(separator + 1)..]);
            if (value.Length > 0) result.TryAdd(key, value);
        }
        return result;
    }

    private static bool IsProtectedName(string value) => ProtectedLabels.Contains(value.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    private static bool IsKnownField(string key) => KnownFields.Contains(key, StringComparer.OrdinalIgnoreCase);
    private static string Bound(string value) => value.Length <= 500 ? value : value[..500];
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : FounderProfileCanonicalizer.NormalizeText(value);
    private static void AddMissing(List<string> missing, string name, string? value) { if (string.IsNullOrWhiteSpace(value)) missing.Add(name); }

    private static FounderRole[] ParseRoles(IEnumerable<string> values)
    {
        var roles = new HashSet<FounderRole>();
        foreach (string value in values)
        {
            string lower = value.ToLowerInvariant();
            if (lower.Contains("design", StringComparison.Ordinal)) roles.Add(FounderRole.Design);
            if (lower.Contains("sales", StringComparison.Ordinal) || lower.Contains("marketing", StringComparison.Ordinal) || lower.Contains("distribution", StringComparison.Ordinal)) roles.Add(FounderRole.SalesMarketing);
            if (lower.Contains("operation", StringComparison.Ordinal)) roles.Add(FounderRole.Operations);
            if (lower.Contains("product", StringComparison.Ordinal)) roles.Add(FounderRole.Product);
            if (lower.Contains("domain", StringComparison.Ordinal) || lower.Contains("industry", StringComparison.Ordinal)) roles.Add(FounderRole.Domain);
            if (lower.Contains("fundrais", StringComparison.Ordinal)) roles.Add(FounderRole.Fundraising);
        }
        if (roles.Count == 0 && values.Any()) roles.Add(FounderRole.Other);
        return roles.Order().ToArray();
    }

    private static TechnicalProfileStatus ParseTechnical(string? value, string introduction)
    {
        string text = $"{value} {introduction}".ToLowerInvariant();
        bool nonTechnical = text.Contains("non-technical", StringComparison.Ordinal) || text.Contains("nontechnical", StringComparison.Ordinal);
        bool technical = text.Contains("engineer", StringComparison.Ordinal) || text.Contains("developer", StringComparison.Ordinal) || text.Contains("technical founder", StringComparison.Ordinal) || text.Contains("cto", StringComparison.Ordinal);
        return (technical, nonTechnical) switch { (true, true) => TechnicalProfileStatus.Mixed, (true, false) => TechnicalProfileStatus.Technical, (false, true) => TechnicalProfileStatus.NonTechnical, _ => TechnicalProfileStatus.Unknown };
    }

    private static FounderCommitmentStatus ParseCommitment(string? value)
    {
        string text = value?.ToLowerInvariant() ?? string.Empty;
        if (text.Contains("open to full-time", StringComparison.Ordinal)
            || text.Contains("transition to full-time", StringComparison.Ordinal)
            || text.Contains("transition plan", StringComparison.Ordinal)
            || text.Contains("planning to go full-time", StringComparison.Ordinal))
        {
            return FounderCommitmentStatus.OpenToFullTime;
        }
        if (text.Contains("full-time", StringComparison.Ordinal) || text.Contains("full time", StringComparison.Ordinal)) return FounderCommitmentStatus.FullTime;
        if (text.Contains("part-time", StringComparison.Ordinal) || text.Contains("part time", StringComparison.Ordinal)) return FounderCommitmentStatus.PartTime;
        if (text.Contains("not committed", StringComparison.Ordinal)) return FounderCommitmentStatus.NotCommitted;
        return FounderCommitmentStatus.Unknown;
    }

    private static IdeaCommitmentStatus ParseIdea(string? value)
    {
        string text = value?.ToLowerInvariant() ?? string.Empty;
        if (text.Contains("open to ideas", StringComparison.Ordinal) || text.Contains("open to other", StringComparison.Ordinal)) return IdeaCommitmentStatus.OpenToIdeas;
        if (text.Contains("no idea", StringComparison.Ordinal)) return IdeaCommitmentStatus.NoIdea;
        if (text.Contains("committed", StringComparison.Ordinal) || text.Contains("working on", StringComparison.Ordinal)) return IdeaCommitmentStatus.Committed;
        return IdeaCommitmentStatus.Unknown;
    }

    private static DateTimeOffset? ParseReliableActivity(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed) ? parsed : null;
    private static string? ParseActivityRange(string? value)
    {
        string lower = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return lower is "today" or "this week" or "this month" ? lower : null;
    }
}

/// <summary>Second deterministic boundary ensuring evaluator input remains protected-attribute free.</summary>
public sealed class DeterministicProfileRedactor : IProfileRedactor
{
    /// <summary>Current deterministic redactor version.</summary>
    public const string CurrentVersion = "founder-profile-redactor-1.0";
    /// <inheritdoc />
    public string Version => CurrentVersion;

    /// <inheritdoc />
    public FounderRedactionResult Redact(NormalizedFounderProfile profile, IReadOnlyList<FounderProfileEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(evidence);
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        string? Clean(string? value)
        {
            if (value is null) return null;
            var warnings = new List<string>();
            string cleaned = DeterministicFounderProfileParser.Sanitize(value, warnings);
            foreach (string warning in warnings) reasons[warning] = reasons.GetValueOrDefault(warning) + 1;
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        FounderProfileEvidence[] safeEvidence = evidence.Select(item => item with { SafeSnippet = Clean(item.SafeSnippet) ?? string.Empty })
            .Where(item => item.SafeSnippet.Length > 0)
            .ToArray();
        string? background = Clean(string.Join("\n", new[] { profile.CareerBackground, profile.EducationBackground, profile.BuildingBackground, profile.LeadershipBackground }.Where(value => value is not null)));
        var input = new FounderEvaluationInput(
            FounderEvaluationInput.CurrentSchemaVersion,
            profile.SourceProfileKey,
            profile.DisplayName,
            profile.TimeZoneCompatibility ?? profile.Location,
            profile.TechnicalStatus,
            profile.CommitmentStatus,
            profile.IdeaCommitmentStatus,
            profile.Roles,
            Clean(profile.Introduction),
            background,
            Clean(profile.StartupDescription),
            Clean(profile.ProblemDescription),
            Clean(profile.CustomerDescription),
            Clean(profile.SolutionDescription),
            profile.TractionClaims.Select(value => Clean(value)).Where(value => value is not null).Cast<string>().ToArray(),
            profile.DesiredCofounderSkills.Select(value => Clean(value)).Where(value => value is not null).Cast<string>().ToArray(),
            Clean(profile.DesiredCofounderRole),
            profile.DesiredCofounderCommitment,
            Clean(profile.EquityPosture),
            profile.Industries,
            safeEvidence);
        bool lowConfidence = profile.Warnings.Contains("redaction.protectedPassage", StringComparer.Ordinal) && reasons.Count == 0;
        return new(input, reasons, lowConfidence ? 0.7m : 1m, lowConfidence, CurrentVersion);
    }
}

/// <summary>Initial grounded screening ruleset; every contribution has a stable reason code.</summary>
public sealed class DeterministicFounderScreeningEngine : IFounderScreeningEngine
{
    private static readonly string[] RoleExpectationTerms = ["product", "engineering", "sales", "fundraising", "operations", "marketing"];
    /// <inheritdoc />
    public FounderScreeningResult Screen(FounderEvaluationInput input, FounderRedactionResult redaction, FounderScreeningRules rules)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(redaction);
        ArgumentNullException.ThrowIfNull(rules);
        var reasons = new List<string>();
        var missing = new List<string>();
        var grounded = new List<FounderProfileEvidence>();
        decimal score = 35m;
        string all = FounderProfileCanonicalizer.NormalizeText(string.Join("\n", new[] { input.Introduction, input.Background, input.Startup, input.Problem, input.Customer, input.Solution, input.DesiredCofounderRole, input.EquityPosture }.Where(value => value is not null)!)).ToLowerInvariant();

        void Add(decimal points, string reason, params string[] fields)
        {
            score += points;
            reasons.Add(reason);
            grounded.AddRange(input.Evidence.Where(item => fields.Contains(item.Field, StringComparer.Ordinal)).Take(3));
        }

        if (rules.HardFilterUnpaidImplementationLabor && (all.Contains("unpaid developer", StringComparison.Ordinal) || all.Contains("work for equity only", StringComparison.Ordinal) || all.Contains("free developer", StringComparison.Ordinal)))
            return Result(ScreeningOutcome.FilteredOut, 0m, ["screen.filter.unpaidImplementationLabor"], input.Evidence, missing, rules);
        if (string.IsNullOrWhiteSpace(input.Introduction) && input.Problem is null && input.Startup is null && input.Background is null)
            return Result(ScreeningOutcome.ManualReview, null, ["screen.review.noMeaningfulContent"], input.Evidence, ["meaningfulProfileContent"], rules);
        if (redaction.RequiresManualReview)
            return Result(ScreeningOutcome.ManualReview, null, ["screen.review.redactionConfidence"], input.Evidence, [], rules);

        if (rules.PreferNonTechnical && input.TechnicalStatus == TechnicalProfileStatus.NonTechnical) Add(15, "screen.preferred.complementaryNonTechnical", "technicalStatus");
        else if (input.TechnicalStatus == TechnicalProfileStatus.Unknown) missing.Add("technicalStatus");
        if (ContainsAny(all, "technical co-founder", "technical cofounder", "cto")) Add(15, "screen.preferred.seeksTechnicalCofounder", "desiredCofounderRole", "introduction");
        else missing.Add("desiredCofounderRole");
        switch (input.CommitmentStatus)
        {
            case FounderCommitmentStatus.FullTime: Add(12, "screen.preferred.fullTime", "commitmentStatus"); break;
            case FounderCommitmentStatus.OpenToFullTime: Add(7, "screen.preferred.transitionPlan", "commitmentStatus"); break;
            case FounderCommitmentStatus.PartTime when !ContainsAny(all, "transition", "full-time", "full time"): reasons.Add("screen.risk.indefinitePartTime"); score -= 15; break;
            case FounderCommitmentStatus.Unknown: missing.Add("commitment"); break;
        }
        if (input.Roles.Count > 0) Add(Math.Min(15, input.Roles.Count * 4), "screen.preferred.complementaryOwnership", "roles"); else missing.Add("ownedRoles");
        if (input.Problem is not null && input.Customer is not null) Add(10, "screen.preferred.clearCustomerProblem", "problemDescription", "customerDescription"); else missing.Add("customerProblem");
        if (input.TractionClaims.Count > 0) Add(10, "screen.preferred.tractionEvidence", "tractionClaims"); else missing.Add("traction");
        if (ContainsAny(input.EquityPosture?.ToLowerInvariant() ?? string.Empty, "equal", "co-founder", "cofounder", "partner")) Add(5, "screen.preferred.founderPartnership", "equityPosture"); else missing.Add("equityPosture");
        if (rules.RequireLocationCompatibility)
        {
            if (ContainsAny(input.LocationCompatibility?.ToLowerInvariant() ?? string.Empty, "united states", "usa", "u.s.", "central", "eastern", "pacific", "mountain", "remote")) Add(5, "screen.preferred.locationCompatible", "location");
            else if (input.LocationCompatibility is null) missing.Add("locationCompatibility");
            else { reasons.Add("screen.risk.locationCompatibilityUnknown"); score -= 5; }
        }
        if (rules.FilterIdenticalTechnicalPreference && input.TechnicalStatus == TechnicalProfileStatus.Technical && ContainsAny(all, "another technical founder", "another engineer"))
            return Result(ScreeningOutcome.FilteredOut, Math.Clamp(score, 0m, 100m), [.. reasons, "screen.filter.identicalTechnicalPreference"], grounded, missing, rules);
        if (CountRoleExpectations(all) >= 5)
            return Result(ScreeningOutcome.ManualReview, Math.Clamp(score, 0m, 100m), [.. reasons, "screen.review.overloadedTechnicalRole"], grounded, missing, rules);
        if (ContainsAny(all, "requires exclusive access", "depends on access") && input.TractionClaims.Count == 0)
            reasons.Add("screen.risk.unvalidatedExternalAccess");

        score = Math.Clamp(score, 0m, 100m);
        ScreeningOutcome outcome = score >= rules.DeepAnalysisThreshold ? ScreeningOutcome.DeepAnalyze : score >= rules.MonitorThreshold ? ScreeningOutcome.Monitor : ScreeningOutcome.FilteredOut;
        reasons.Add(outcome switch { ScreeningOutcome.DeepAnalyze => "screen.outcome.deepAnalyze", ScreeningOutcome.Monitor => "screen.outcome.monitor", _ => "screen.outcome.belowThreshold" });
        return Result(outcome, score, reasons, grounded, missing, rules);
    }

    private static FounderScreeningResult Result(ScreeningOutcome outcome, decimal? score, IEnumerable<string> reasons, IEnumerable<FounderProfileEvidence> evidence, IEnumerable<string> missing, FounderScreeningRules rules) =>
        new(outcome, score, reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), evidence.Distinct().OrderBy(item => item.Field, StringComparer.Ordinal).ToArray(), missing.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), rules.RulesetVersion);
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);
    private static int CountRoleExpectations(string value) => RoleExpectationTerms.Count(value.Contains);
}

/// <summary>Deterministic identity signal builder. Persistence decides merge versus conflict atomically.</summary>
public sealed class CandidateIdentityResolver : ICandidateIdentityResolver
{
    /// <inheritdoc />
    public CandidateIdentityResolution Resolve(NormalizedFounderProfile profile, JsonElement capturedStructuredFields, string sourceAccountId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var signals = new List<CandidateIdentityInput>();
        string? trustedId = ReadTrustedId(capturedStructuredFields);
        if (trustedId is not null)
        {
            signals.Add(new(CandidateIdentityAliasType.TrustedSourceId, FounderScoutIdentityNormalizer.Hash("trusted-source-id", trustedId.ToLowerInvariant()), sourceAccountId, 1m, true));
        }
        string stableText = string.Join('\n', new[] { profile.Introduction, profile.CareerBackground, profile.BuildingBackground }.Where(value => !string.IsNullOrWhiteSpace(value)).Take(2));
        bool strong = !string.IsNullOrWhiteSpace(profile.DisplayName) && !string.IsNullOrWhiteSpace(profile.Location) && stableText.Length >= 40 && profile.Completeness >= 0.65m;
        string? fingerprint = stableText.Length >= 20
            ? FounderScoutIdentityNormalizer.Hash("founder-fingerprint", $"{profile.DisplayName.ToLowerInvariant()}\n{profile.Location?.ToLowerInvariant()}\n{stableText.ToLowerInvariant()}")
            : null;
        if (fingerprint is not null) signals.Add(new(CandidateIdentityAliasType.Fingerprint, fingerprint, sourceAccountId, strong ? 0.95m : 0.6m, strong));
        return new(signals, fingerprint, strong, fingerprint is null ? ["identity.fingerprint.insufficientEvidence"] : strong ? ["identity.fingerprint.strong"] : ["identity.fingerprint.weak"]);
    }

    private static string? ReadTrustedId(JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object) return null;
        foreach (string name in new[] { "trustedSourceId", "sourceId", "profileId" })
        {
            if (fields.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                string? id = value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(id) && id.Length <= 256 && !id.Any(char.IsControl)) return id;
            }
        }
        return null;
    }
}

/// <summary>Relevant structured diff; values are deliberately absent from audit output.</summary>
public static class FounderProfileDiffer
{
    /// <summary>Returns changed relevant field names and change kinds without field values.</summary>
    public static IReadOnlyList<FounderProfileChange> Diff(NormalizedFounderProfile? previous, NormalizedFounderProfile current)
    {
        if (previous is null) return [new("profile", "Added")];
        var changes = new List<FounderProfileChange>();
        Compare(changes, "displayName", previous.DisplayName, current.DisplayName);
        Compare(changes, "location", previous.Location, current.Location);
        Compare(changes, "technicalStatus", previous.TechnicalStatus, current.TechnicalStatus);
        Compare(changes, "commitmentStatus", previous.CommitmentStatus, current.CommitmentStatus);
        Compare(changes, "ideaCommitmentStatus", previous.IdeaCommitmentStatus, current.IdeaCommitmentStatus);
        Compare(changes, "roles", string.Join('|', previous.Roles), string.Join('|', current.Roles));
        Compare(changes, "introduction", previous.Introduction, current.Introduction);
        Compare(changes, "background", string.Join('|', previous.CareerBackground, previous.EducationBackground, previous.BuildingBackground, previous.LeadershipBackground), string.Join('|', current.CareerBackground, current.EducationBackground, current.BuildingBackground, current.LeadershipBackground));
        Compare(changes, "startup", string.Join('|', previous.StartupDescription, previous.ProblemDescription, previous.CustomerDescription, previous.SolutionDescription), string.Join('|', current.StartupDescription, current.ProblemDescription, current.CustomerDescription, current.SolutionDescription));
        Compare(changes, "tractionClaims", string.Join('|', previous.TractionClaims), string.Join('|', current.TractionClaims));
        Compare(changes, "cofounderPreference", string.Join('|', previous.DesiredCofounderSkills) + previous.DesiredCofounderRole + previous.EquityPosture, string.Join('|', current.DesiredCofounderSkills) + current.DesiredCofounderRole + current.EquityPosture);
        return changes;
    }

    private static void Compare<T>(List<FounderProfileChange> changes, string field, T previous, T current)
    {
        if (!EqualityComparer<T>.Default.Equals(previous, current)) changes.Add(new(field, previous is null ? "Added" : current is null ? "Removed" : "Changed"));
    }
}
