using System.Text.Json;
using FounderScout.Application;

namespace FounderScout.Tests;

internal sealed class FounderScoutConfigurationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task SafeDefaultsPassTheTypedBaselineValidator()
    {
        var defaults = new FounderScoutDefaults().GetDefault();
        var validator = new FounderScoutConfigurationValidator();

        var errors = await validator.ValidateAsync(defaults.AgentId, defaults.SchemaVersion, defaults.Configuration);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty);
            Assert.That(defaults.Configuration.GetProperty("ai").GetProperty("apiKeySecretReference").GetString(),
                Is.EqualTo("secret://founder-scout/azure-openai-key"));
            Assert.That(defaults.Configuration.GetProperty("priority").GetProperty("ourFitWeight").GetDecimal(),
                Is.EqualTo(0.55m));
            Assert.That(defaults.Configuration.GetProperty("persona").GetProperty("schemaVersion").GetString(),
                Is.EqualTo("1.0"));
            Assert.That(defaults.Configuration.GetProperty("analysis").GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(defaults.Configuration.GetProperty("analysis").GetProperty("maximumRetries").GetInt32(), Is.Zero);
            Assert.That(defaults.Configuration.GetProperty("ai").GetProperty("provider").GetString(), Is.EqualTo("OpenAI"));
            Assert.That(defaults.Configuration.GetProperty("ai").GetProperty("deployment").GetString(), Is.EqualTo("gpt-5.4-mini"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("browserChannel").GetString(), Is.EqualTo("chrome"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("adapterVersion").GetString(),
                Is.EqualTo("startup-school-1.6"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("entryUrl").GetString(),
                Does.EndWith("/cofounder-matching/candidate/next"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("profileLinkLocators")
                .EnumerateArray().Select(item => item.GetProperty("value").GetString()),
                Does.Contain("a[href^='/cofounder-matching/']"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("nextPageLocators")
                .EnumerateArray().Select(item => item.GetProperty("value").GetString()),
                Does.Contain("a[href='/cofounder-matching/candidate/next']"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("allowedHosts")
                .EnumerateArray().Select(item => item.GetString()), Does.Contain("account.ycombinator.com"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("authenticatedLocators").GetRawText(),
                Does.Not.Contain("data-testid='cofounder-matching'"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("loginLocators").GetRawText(),
                Does.Contain("sign-in-card"));
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("headlessDiscovery").GetBoolean(), Is.True);
            Assert.That(defaults.Configuration.GetProperty("startupSchool").GetProperty("storeRawHtml").GetBoolean(), Is.False);
            Assert.That(defaults.Configuration.GetRawText(), Does.Not.Contain("password"));
        });
    }

    [Test]
    public async Task SimpleModePreservesOnlyFocusedOpenAiChoicesAndValidatesCopiedContext()
    {
        FounderScoutConfiguration defaults = FounderScoutDefaults.CreateConfiguration();
        FounderScoutConfiguration customized = defaults with
        {
            Discovery = defaults.Discovery with { MaxNewProfilesPerRun = 499 },
            Analysis = defaults.Analysis with { Enabled = false, BatchSize = 7, MaximumConcurrency = 3 },
            Ai = defaults.Ai with { Deployment = "gpt-5.5", RequestTimeoutSeconds = 90 },
            Persona = defaults.Persona with { AdditionalContext = "Focus on complementary go-to-market leadership.\nKeep uncertainty explicit." },
        };

        FounderScoutConfiguration merged = FounderScoutSimpleMode.CreateConfiguration(customized);
        IReadOnlyList<HomeBusinessAssistant.Application.Configuration.ConfigurationValidationError> valid =
            await new FounderScoutConfigurationValidator().ValidateAsync(
                FounderScoutDefaults.AgentId,
                FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(merged, JsonOptions));
        IReadOnlyList<HomeBusinessAssistant.Application.Configuration.ConfigurationValidationError> invalid =
            await new FounderScoutConfigurationValidator().ValidateAsync(
                FounderScoutDefaults.AgentId,
                FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(
                    merged with { Persona = merged.Persona with { AdditionalContext = new string('x', 20_001) } },
                    JsonOptions));

        Assert.Multiple(() =>
        {
            Assert.That(merged.Analysis.Enabled, Is.True);
            Assert.That(merged.Analysis.BatchSize, Is.EqualTo(7));
            Assert.That(merged.Analysis.MaximumConcurrency, Is.EqualTo(3));
            Assert.That(merged.Ai.Provider, Is.EqualTo("OpenAI"));
            Assert.That(merged.Ai.Endpoint, Is.Empty);
            Assert.That(merged.Ai.Deployment, Is.EqualTo("gpt-5.5"));
            Assert.That(merged.Persona.AdditionalContext, Does.Contain("go-to-market"));
            Assert.That(merged.Discovery.MaxNewProfilesPerRun, Is.EqualTo(defaults.Discovery.MaxNewProfilesPerRun));
            Assert.That(valid, Is.Empty);
            Assert.That(invalid.Select(item => item.Path), Does.Contain("$.persona.additionalContext"));
        });
    }

    [Test]
    public async Task ValidatorRejectsEnforcementFailoverAndPlainSecretShape()
    {
        JsonElement source = new FounderScoutDefaults().GetDefault().Configuration;
        var model = source.Deserialize<FounderScoutConfiguration>(JsonOptions)
            ?? throw new InvalidOperationException("Default configuration did not deserialize.");
        FounderScoutConfiguration unsafeModel = model with
        {
            Discovery = model.Discovery with { AutomaticFailoverAfterEnforcementSignal = true },
            Ai = model.Ai with { ApiKeySecretReference = "plain-text-key" },
        };
        var validator = new FounderScoutConfigurationValidator();

        var errors = await validator.ValidateAsync(
            FounderScoutDefaults.AgentId,
            FounderScoutConfiguration.CurrentSchemaVersion,
            JsonSerializer.SerializeToElement(unsafeModel, JsonOptions));

        Assert.Multiple(() =>
        {
            Assert.That(errors.Select(item => item.Path), Does.Contain("$.discovery.automaticFailoverAfterEnforcementSignal"));
            Assert.That(errors.Select(item => item.Path), Does.Contain("$.ai.apiKeySecretReference"));
        });
    }

    [Test]
    public async Task ValidatorRejectsUnbalancedPriorityWeightsAndInvalidPersona()
    {
        JsonElement source = new FounderScoutDefaults().GetDefault().Configuration;
        var model = source.Deserialize<FounderScoutConfiguration>(JsonOptions)
            ?? throw new InvalidOperationException("Default configuration did not deserialize.");
        FounderScoutConfiguration invalid = model with
        {
            Priority = model.Priority with { OurFitWeight = 0.75m },
            Persona = model.Persona with { SchemaVersion = "2.0", Strengths = [] },
        };

        IReadOnlyList<HomeBusinessAssistant.Application.Configuration.ConfigurationValidationError> errors =
            await new FounderScoutConfigurationValidator().ValidateAsync(
                FounderScoutDefaults.AgentId,
                FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(invalid, JsonOptions));

        Assert.Multiple(() =>
        {
            Assert.That(errors.Select(item => item.Code), Does.Contain("founderScout.configuration.priorityWeightTotal"));
            Assert.That(errors.Select(item => item.Path), Does.Contain("$.persona.schemaVersion"));
            Assert.That(errors.Select(item => item.Path), Does.Contain("$.persona.strengths"));
        });
    }

    [Test]
    public async Task ValidatorRejectsBrittleLocatorsUnexpectedHostsAndExcessRetries()
    {
        FounderScoutConfiguration model = new FounderScoutDefaults().GetDefault().Configuration
            .Deserialize<FounderScoutConfiguration>(JsonOptions)
            ?? throw new InvalidOperationException("Default configuration did not deserialize.");
        StartupSchoolSourceOptions source = (model.StartupSchool ?? StartupSchoolSourceOptions.Default) with
        {
            AllowedHosts = ["example.invalid"],
            ProfileLinkLocators = [new("css", "article:nth-child(2) a")],
            MaximumTransientNavigationRetries = 5,
        };

        IReadOnlyList<HomeBusinessAssistant.Application.Configuration.ConfigurationValidationError> errors =
            await new FounderScoutConfigurationValidator().ValidateAsync(
                FounderScoutDefaults.AgentId,
                FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(model with { StartupSchool = source }, JsonOptions));

        Assert.Multiple(() =>
        {
            Assert.That(errors.Select(item => item.Path), Does.Contain("$.startupSchool.allowedHosts"));
            Assert.That(errors.Select(item => item.Path), Does.Contain("$.startupSchool.profileLinkLocators"));
            Assert.That(errors.Select(item => item.Path), Does.Contain("$.startupSchool.maximumTransientNavigationRetries"));
        });
    }

    [Test]
    public async Task ValidatorRejectsNonLoopbackHttpButAllowsLoopbackFixtures()
    {
        FounderScoutConfiguration model = new FounderScoutDefaults().GetDefault().Configuration
            .Deserialize<FounderScoutConfiguration>(JsonOptions)
            ?? throw new InvalidOperationException("Default configuration did not deserialize.");
        StartupSchoolSourceOptions insecure = (model.StartupSchool ?? StartupSchoolSourceOptions.Default) with
        {
            EntryUrl = "http://example.invalid/founders",
            AllowedHosts = ["example.invalid"],
        };
        StartupSchoolSourceOptions fixture = insecure with
        {
            EntryUrl = "http://127.0.0.1:51899/founders",
            AllowedHosts = ["127.0.0.1"],
        };
        var validator = new FounderScoutConfigurationValidator();

        IReadOnlyList<HomeBusinessAssistant.Application.Configuration.ConfigurationValidationError> insecureErrors =
            await validator.ValidateAsync(FounderScoutDefaults.AgentId, FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(model with { StartupSchool = insecure }, JsonOptions));
        IReadOnlyList<HomeBusinessAssistant.Application.Configuration.ConfigurationValidationError> fixtureErrors =
            await validator.ValidateAsync(FounderScoutDefaults.AgentId, FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(model with { StartupSchool = fixture }, JsonOptions));

        Assert.Multiple(() =>
        {
            Assert.That(insecureErrors.Select(item => item.Path), Does.Contain("$.startupSchool.entryUrl"));
            Assert.That(fixtureErrors.Select(item => item.Path), Does.Not.Contain("$.startupSchool.entryUrl"));
        });
    }
}
