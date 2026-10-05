using System.IO;
using System.Text.Json;

namespace Pulsatilla.Wpf;

public enum ApplicationTrust { Monitor, Trusted, Blocked }
public sealed record ApplicationPolicy(string ExecutablePath, ApplicationTrust Trust)
{
    public string Application => Path.GetFileName(ExecutablePath);
    public string Policy => Trust switch { ApplicationTrust.Trusted => "Trusted (whitelist)", ApplicationTrust.Blocked => "Blocked (blacklist)", _ => "Monitor" };
}

public sealed class ProtectionSettings
{
    public bool MonitorAttacks { get; set; } = true;
    public bool ReportDnsRotation { get; set; }
    public RainQuality RainQuality { get; set; } = RainQuality.Eco;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;
    public string WatchedEmails { get; set; } = "";
    public string TrustedSenders { get; set; } = "";
    public string BlockedSenders { get; set; } = "";
    public List<ApplicationPolicy> Applications { get; set; } = [];
}

public sealed class ProtectionSettingsStore
{
    private readonly string _path;
    private readonly Dictionary<string, ApplicationTrust> _trust = new(StringComparer.OrdinalIgnoreCase);
    public ProtectionSettings Settings { get; }

    public ProtectionSettingsStore(string? path = null)
    {
        _path = path ?? AppStorage.FilePath("protection.json");
        try { Settings = File.Exists(_path) ? JsonSerializer.Deserialize<ProtectionSettings>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Settings = new(); }
        Settings.Applications ??= [];
        if (!Enum.IsDefined(Settings.RainQuality)) Settings.RainQuality = RainQuality.Eco;
        if (!Enum.IsDefined(Settings.ThemeMode)) Settings.ThemeMode = ThemeMode.Dark;
        foreach (var rule in Settings.Applications)
            if (TryNormalizePath(rule.ExecutablePath, out var normalized) && Enum.IsDefined(rule.Trust)) _trust[normalized] = rule.Trust;
    }

    public ApplicationTrust GetTrust(string path) => TryNormalizePath(path, out var normalized)
        ? _trust.GetValueOrDefault(normalized, ApplicationTrust.Monitor) : ApplicationTrust.Monitor;

    public void SetTrust(string path, ApplicationTrust trust)
    {
        if (!TryNormalizePath(path, out var normalized)) throw new ArgumentException("Choose a full executable path.");
        if (!Enum.IsDefined(trust)) throw new ArgumentOutOfRangeException(nameof(trust));
        var existed = _trust.TryGetValue(normalized, out var previous);
        _trust[normalized] = trust;
        try { Save(); }
        catch { if (existed) _trust[normalized] = previous; else _trust.Remove(normalized); throw; }
    }

    public void Save()
    {
        Settings.Applications = _trust.Select(pair => new ApplicationPolicy(pair.Key, pair.Value)).OrderBy(rule => rule.Application).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
    }

    private static bool TryNormalizePath(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try { normalized = Path.GetFullPath(path); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
