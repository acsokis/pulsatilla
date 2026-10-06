namespace Pulsatilla.Wpf;

public partial class MainWindow
{
    // Unimplemented in Community; calls disappear at compile time. Private edition composition implements these hooks.
    private EditionCapabilities _editionCapabilities = EditionCapabilities.Community;
    partial void ConfigureEdition();
    partial void InitializeEdition();
    partial void ObserveEditionSockets();
    partial void ObserveEditionPacket(PacketObservation packet, ProcessIdentity process, bool outbound);
    partial void ObserveEditionEvent(string level, string summary);
    partial void DisposeEdition();
}
