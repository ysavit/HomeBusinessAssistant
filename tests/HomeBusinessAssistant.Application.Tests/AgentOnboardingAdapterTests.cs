using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Tests;

internal sealed class AgentOnboardingAdapterTests
{
    [Test]
    public void RegistryUsesExplicitCodeOwnedAdapterBeforeGenericFallbackAndFailsComplexSchemasClosed()
    {
        AgentId specializedId = AgentId.Parse("specialized-agent");
        var specialized = new StubAdapter(specializedId, AgentOnboardingSupport.Specialized, "specialized.adapter");
        var generic = new StubAdapter(null, AgentOnboardingSupport.SafeGeneric, "platform.generic");
        var registry = new AgentOnboardingAdapterRegistry([specialized], generic);
        GenericConfigurationSchema safe = CreateSchema(supported: true);
        GenericConfigurationSchema complex = CreateSchema(supported: false);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Resolve(specializedId, safe), Is.SameAs(specialized));
            Assert.That(registry.Resolve(AgentId.Parse("generic-agent"), safe), Is.SameAs(generic));
            Assert.That(registry.Resolve(AgentId.Parse("complex-agent"), complex), Is.Null);
            Assert.That(registry.Resolve(AgentId.Parse("schema-missing"), null), Is.Null);
        });
    }

    [Test]
    public void RegistryRejectsAnExplicitAdapterWithoutOneFixedAgentIdentity()
    {
        var generic = new StubAdapter(null, AgentOnboardingSupport.SafeGeneric, "platform.generic");

        Assert.That(
            () => new AgentOnboardingAdapterRegistry([generic], generic),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("explicitAdapters"));
    }

    private static GenericConfigurationSchema CreateSchema(bool supported) => new(
        "Test",
        null,
        "1.0",
        [],
        supported,
        supported ? null : "A typed adapter is required.");

    private sealed class StubAdapter(
        AgentId? supportedAgentId,
        AgentOnboardingSupport support,
        string adapterId) : IAgentOnboardingAdapter
    {
        public AgentOnboardingAdapterDescriptor Descriptor { get; } = new(
            adapterId,
            "1.0",
            supportedAgentId,
            support,
            CanConfigure: true,
            []);

        public ValueTask<AgentOnboardingAssessment> AssessAsync(
            AgentOnboardingContext context,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<AgentConfigurationRecord?> LoadConfigurationAsync(
            AgentId agentId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<SaveConfigurationResult> SaveConfigurationAsync(
            SaveConfigurationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
