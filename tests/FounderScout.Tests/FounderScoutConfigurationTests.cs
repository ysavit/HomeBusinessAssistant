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
            Assert.That(defaults.Configuration.GetRawText(), Does.Not.Contain("password"));
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
