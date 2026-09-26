using HomeBusinessAssistant.Application.Configuration;

namespace HomeBusinessAssistant.Host.Pages.Components;

/// <summary>Platform-owned generic field rendering shared by Agent Settings and onboarding.</summary>
public sealed record GenericConfigurationFieldsView(
    GenericConfigurationSchema Schema,
    IReadOnlyDictionary<string, string> Values,
    string FieldPrefix);
