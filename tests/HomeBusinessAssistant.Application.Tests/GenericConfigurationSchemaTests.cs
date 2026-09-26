using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Tests;

internal sealed class GenericConfigurationSchemaTests
{
    private static readonly string[] ExpectedValidationCodes =
    [
        "configuration.additionalProperty",
        "configuration.constant",
        "configuration.enum",
        "configuration.maximum",
        "configuration.secretReference",
    ];
    private static readonly string[] NonObjectConfiguration = ["not-an-object"];

    [Test]
    public void ParserAcceptsSafePrimitiveSubsetAndBuildsDefaults()
    {
        using JsonDocument document = JsonDocument.Parse(SupportedSchemaJson);

        GenericConfigurationSchemaParseResult result = GenericConfigurationSchemaParser.Parse(document.RootElement, "1.0");

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Schema!.SupportsGenericEditor, Is.True);
        JsonElement defaults = result.Schema.CreateDefaultDocument();
        Assert.Multiple(() =>
        {
            Assert.That(defaults.GetProperty("schemaVersion").GetString(), Is.EqualTo("1.0"));
            Assert.That(defaults.GetProperty("mode").GetString(), Is.EqualTo("safe"));
            Assert.That(defaults.GetProperty("maximumItems").GetInt32(), Is.EqualTo(10));
            Assert.That(defaults.GetProperty("enabled").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void ParserReportsComplexObjectAsRequiringTypedAdapter()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"title":"Complex","type":"object","properties":{"nested":{"type":"object"}},"additionalProperties":false}
            """);

        GenericConfigurationSchemaParseResult result = GenericConfigurationSchemaParser.Parse(document.RootElement, "1.0");

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Schema!.SupportsGenericEditor, Is.False);
        Assert.That(result.Schema.UnsupportedReason, Does.Contain("nested"));
    }

    [Test]
    public async Task ValidatorEnforcesTypesBoundsEnumSecretReferenceAndAdditionalProperties()
    {
        using JsonDocument schemaDocument = JsonDocument.Parse(SupportedSchemaJson);
        GenericConfigurationSchema schema = GenericConfigurationSchemaParser.Parse(schemaDocument.RootElement, "1.0").Schema!;
        var validator = new GenericAgentConfigurationValidator(
            new StubCatalog(schema),
            new HashSet<AgentId>());
        JsonElement configuration = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = "2.0",
            mode = "unsafe",
            maximumItems = 99,
            enabled = true,
            tokenReference = "inline-secret",
            extra = true,
        });

        IReadOnlyList<ConfigurationValidationError> errors = await validator.ValidateAsync(
            AgentId.Parse("generic-agent"), "1.0", configuration);

        Assert.That(errors.Select(error => error.Code), Is.SupersetOf(ExpectedValidationCodes));
    }

    [Test]
    public async Task ValidatorLeavesNonObjectRootToBaselineValidatorWithoutThrowing()
    {
        using JsonDocument schemaDocument = JsonDocument.Parse(SupportedSchemaJson);
        GenericConfigurationSchema schema = GenericConfigurationSchemaParser.Parse(schemaDocument.RootElement, "1.0").Schema!;
        var validator = new GenericAgentConfigurationValidator(
            new StubCatalog(schema),
            new HashSet<AgentId>());
        JsonElement configuration = JsonSerializer.SerializeToElement(NonObjectConfiguration);

        IReadOnlyList<ConfigurationValidationError> errors = await validator.ValidateAsync(
            AgentId.Parse("generic-agent"), "1.0", configuration);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void SharedFormCodecPreservesConstantsEnumsAndTypedArrays()
    {
        using JsonDocument schemaDocument = JsonDocument.Parse("""
            {
              "title":"Codec",
              "type":"object",
              "required":["schemaVersion","mode","ports"],
              "properties":{
                "schemaVersion":{"const":"1.0"},
                "mode":{"type":"string","enum":["safe","review"],"default":"safe"},
                "ports":{"type":"array","items":{"type":"integer"},"default":[80,443]}
              },
              "additionalProperties":false
            }
            """);
        GenericConfigurationSchema schema = GenericConfigurationSchemaParser.Parse(schemaDocument.RootElement, "1.0").Schema!;

        GenericConfigurationFormResult result = GenericConfigurationFormCodec.Build(schema, new Dictionary<string, string>
        {
            ["mode"] = "review",
            ["ports"] = "8080\r\n8443",
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Configuration!.Value.GetProperty("schemaVersion").GetString(), Is.EqualTo("1.0"));
            Assert.That(result.Configuration.Value.GetProperty("mode").GetString(), Is.EqualTo("review"));
            Assert.That(result.Configuration.Value.GetProperty("ports").EnumerateArray().Select(item => item.GetInt64()),
                Is.EqualTo(new long[] { 8080, 8443 }));
        });

        JsonElement configuration = result.Configuration
            ?? throw new InvalidOperationException("The valid form did not produce configuration.");
        IReadOnlyDictionary<string, string> display = GenericConfigurationFormCodec.ReadValues(schema, configuration);
        Assert.That(display["ports"], Is.EqualTo($"8080{Environment.NewLine}8443"));
    }

    [Test]
    public void SharedFormCodecRejectsMalformedTypedArrayWithoutProducingConfiguration()
    {
        using JsonDocument schemaDocument = JsonDocument.Parse("""
            {
              "title":"Codec",
              "type":"object",
              "required":["schemaVersion","ports"],
              "properties":{
                "schemaVersion":{"const":"1.0"},
                "ports":{"type":"array","items":{"type":"integer"}}
              },
              "additionalProperties":false
            }
            """);
        GenericConfigurationSchema schema = GenericConfigurationSchemaParser.Parse(schemaDocument.RootElement, "1.0").Schema!;

        GenericConfigurationFormResult result = GenericConfigurationFormCodec.Build(schema, new Dictionary<string, string>
        {
            ["ports"] = "443\nnot-a-number",
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Configuration, Is.Null);
            Assert.That(result.Errors.Single().FieldName, Is.EqualTo("ports"));
        });
    }

    private const string SupportedSchemaJson = """
        {
          "title":"Generic",
          "type":"object",
          "required":["schemaVersion","mode","maximumItems","enabled","tokenReference"],
          "properties":{
            "schemaVersion":{"const":"1.0"},
            "mode":{"type":"string","enum":["safe","review"],"default":"safe"},
            "maximumItems":{"type":"integer","minimum":1,"maximum":20,"default":10},
            "enabled":{"type":"boolean","default":true},
            "tokenReference":{"type":"string","format":"hba-secret-reference","default":"secret://generic-agent/token"}
          },
          "additionalProperties":false
        }
        """;

    private sealed class StubCatalog(GenericConfigurationSchema schema) : IAgentConfigurationSchemaCatalog
    {
        public ValueTask<GenericConfigurationSchema?> GetAsync(AgentId agentId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<GenericConfigurationSchema?>(schema);
        }
    }
}
