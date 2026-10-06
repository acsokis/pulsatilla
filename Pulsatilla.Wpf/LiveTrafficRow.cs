using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pulsatilla.Wpf;

public sealed class LiveTrafficRow(string application, string executablePath, string host, string protocol)
    : INotifyPropertyChanged
{
    private long _uploadBytes;
    private long _downloadBytes;
    private DateTime _lastSeenLocal = DateTime.Now;
    private ApplicationTrust _trust;

    public string Application { get; } = application;
    public string ExecutablePath { get; } = executablePath;
    public string Host { get; } = host;
    public string Protocol { get; } = protocol;
    public int ProcessId { get; init; }
    public string LocalAddress { get; init; } = "Unknown";
    public int LocalPort { get; init; }
    public string RemoteAddress { get; init; } = host;
    public string RemoteHost => RemoteAddress;
    public int RemotePort { get; init; }
    public string Hostname { get; init; } = "Unknown";
    public string Country { get; init; } = "Unknown";
    public string Status { get; init; } = "Observed packet (socket state unknown)";
    public DateTime FirstSeenUtc { get; init; } = DateTime.UtcNow;
    public DateTime FirstSeenLocal => FirstSeenUtc.ToLocalTime();
    public DateTime LastSeenUtc => LastSeenLocal.ToUniversalTime();
    public string FlowKey { get; init; } = "";
    public ApplicationTrust Trust
    {
        get => _trust;
        set { if (_trust == value) return; _trust = value; PropertyChanged?.Invoke(this, new(nameof(Policy))); }
    }
    public string Policy => Trust switch { ApplicationTrust.Trusted => "Trusted", ApplicationTrust.Blocked => "Blocked", _ => "Monitor" };
    public long UploadBytes { get => _uploadBytes; private set => SetField(ref _uploadBytes, value); }
    public long DownloadBytes { get => _downloadBytes; private set => SetField(ref _downloadBytes, value); }
    public DateTime LastSeenLocal { get => _lastSeenLocal; private set => SetField(ref _lastSeenLocal, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddPacket(int bytes, bool outbound)
    {
        if (bytes <= 0) return;
        if (outbound) UploadBytes = SaturatingAdd(UploadBytes, bytes);
        else DownloadBytes = SaturatingAdd(DownloadBytes, bytes);
        LastSeenLocal = DateTime.Now;
    }
    private static long SaturatingAdd(long current, int bytes) => current > long.MaxValue - bytes ? long.MaxValue : current + bytes;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
