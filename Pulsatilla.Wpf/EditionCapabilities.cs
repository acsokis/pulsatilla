namespace Pulsatilla.Wpf;

public enum FeatureCapability { None, VpnManagement, AdvancedThreatIntel, AdvancedWfp, SecureMessaging, CommercialIntegrations, AdvancedReports }
public enum FeatureTier { Community, Core, ExperimentalCore }

/// <summary>Composition metadata only. Modules must be present and implemented before enabling a capability.</summary>
public sealed record EditionCapabilities(bool VpnManagement = false, bool AdvancedThreatIntel = false,
    bool AdvancedWfp = false, bool SecureMessaging = false, bool CommercialIntegrations = false, bool AdvancedReports = false)
{
    public static EditionCapabilities Community { get; } = new();
    public bool Supports(FeatureCapability capability) => capability switch
    {
        FeatureCapability.None => true, FeatureCapability.VpnManagement => VpnManagement,
        FeatureCapability.AdvancedThreatIntel => AdvancedThreatIntel, FeatureCapability.AdvancedWfp => AdvancedWfp,
        FeatureCapability.SecureMessaging => SecureMessaging, FeatureCapability.CommercialIntegrations => CommercialIntegrations,
        FeatureCapability.AdvancedReports => AdvancedReports, _ => false
    };
}
public sealed record FeatureDescriptor(string Id, string Name, FeatureCapability Capability,
    FeatureTier Tier, string Icon, string NavigationGroup);
