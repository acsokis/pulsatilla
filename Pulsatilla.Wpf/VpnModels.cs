namespace Pulsatilla.Wpf;

public enum VpnConnectionState { NotInstalled, NotConfigured, Disconnected, Connecting, Connected, Disconnecting, Failed }
public sealed record VpnProfile(Guid Id, string Name, DateTimeOffset ImportedUtc, bool RequiresPassword, string Endpoint);
public sealed record VpnProfileValidation(bool Accepted, string NormalizedProfile, bool RequiresPassword,
    string Endpoint, IReadOnlyList<string> Findings);
public sealed record VpnStatus(VpnConnectionState State, string Message, string Profile,
    DateTimeOffset? ConnectedUtc, bool OwnedByPulsatilla, string TunnelAddress = "", string TunnelInterface = "");
public interface IVpnProvider : IAsyncDisposable
{
    VpnStatus Status { get; }
    event EventHandler? StatusChanged;
    Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
public enum VpnDirectoryIntegration { ProfileImport, Documentation, Api }
public sealed record VpnDirectoryEntry(string Provider, bool? FreePlanAvailable, bool? OpenVpnSupported,
    Uri SetupUrl, Uri? ConfigurationUrl, DateTimeOffset LastVerifiedUtc, VpnDirectoryIntegration Integration);
public static class VpnProviderDirectory
{
    // No unverifiable free-plan claims. Entries require an explicit source and verification date.
    public static IReadOnlyList<VpnDirectoryEntry> Entries { get; } = Array.Empty<VpnDirectoryEntry>();
}
