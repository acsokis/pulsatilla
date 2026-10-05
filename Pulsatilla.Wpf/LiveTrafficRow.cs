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
        if (outbound) UploadBytes += bytes;
        else DownloadBytes += bytes;
        LastSeenLocal = DateTime.Now;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
