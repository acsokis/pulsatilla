using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pulsatilla.Wpf;

public sealed record ThreatIntelResult(string Provider, string State, string Summary);
public interface IThreatIntelProvider
{
    string Name { get; }
    Task<ThreatIntelResult> InspectAsync(string localPath, bool userConfirmed, CancellationToken cancellationToken = default);
}
public sealed class LocalThreatIntelProvider : IThreatIntelProvider
{
    private readonly LocalFileInspector _inspector = new();
    public string Name => "Local analysis";
    public async Task<ThreatIntelResult> InspectAsync(string localPath, bool userConfirmed, CancellationToken cancellationToken = default)
    {
        if (!userConfirmed) return new(Name, "Not requested", "Local file inspection requires explicit selection.");
        var file = await _inspector.InspectAsync(localPath, cancellationToken).ConfigureAwait(false);
        return new(Name, file.Risk, string.Join("\n", file.Reasons));
    }
}

/// <summary>Explicit supported Defender custom scan. Does not modify Defender configuration.</summary>
public sealed class MicrosoftDefenderProvider : IThreatIntelProvider
{
    public string Name => "Microsoft Defender";
    public string? FindCommand()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        var platform = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender", "Platform");
        var candidates = new List<string>();
        try { if (Directory.Exists(platform)) candidates.AddRange(Directory.EnumerateDirectories(platform).Take(256).OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).Select(p => Path.Combine(p, "MpCmdRun.exe"))); }
        catch (UnauthorizedAccessException) { } catch (IOException) { }
        candidates.Add(basePath);
        foreach (var path in candidates.Take(7).Append(basePath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { LocalFileInspector.ValidateLocalFile(path); if (WindowsSignatureInspector.Verify(path) is { Status: LocalSignatureStatus.Valid, Publisher: "Microsoft Corporation" }) return path; }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        }
        return null;
    }
    public async Task<ThreatIntelResult> InspectAsync(string localPath, bool userConfirmed, CancellationToken cancellationToken = default)
    {
        if (!userConfirmed) return new(Name, "Not requested", "Defender runs only after explicit confirmation. Its configured cloud settings may apply.");
        localPath = LocalFileInspector.ValidateLocalFile(localPath);
        var command = await Task.Run(FindCommand, cancellationToken).ConfigureAwait(false);
        if (command is null) return new(Name, "Not installed / unavailable", "A locally trusted Microsoft Defender command could not be found. Offline signature verification can limit detection.");
        // Re-open and keep the verified executable read-locked through process creation and completion.
        // This prevents ordinary writes/replacement between trust verification and execution.
        using var commandLock = new FileStream(LocalFileInspector.ValidateLocalFile(command), FileMode.Open, FileAccess.Read, FileShare.Read);
        var lockedTrust = await Task.Run(() => WindowsSignatureInspector.Verify(command), cancellationToken).ConfigureAwait(false);
        if (lockedTrust is not { Status: LocalSignatureStatus.Valid, Publisher: "Microsoft Corporation" })
            return new(Name, "Unavailable", "Defender executable trust changed or could not be established immediately before execution.");
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var process = new Process { StartInfo = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in new[] { "-Scan", "-ScanType", "3", "-File", localPath, "-DisableRemediation" }) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) return new(Name, "Failed", "Defender process did not start.");
        var output = Drain(process.StandardOutput, deadline.Token); var errors = Drain(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); await Task.WhenAll(output, errors).ConfigureAwait(false);
            // Exit codes alone do not establish a clean file. Defender records authoritative results in its own history.
            return new(Name, "Completed", $"Defender custom scan exited with code {process.ExitCode}. Review Windows Security protection history; this is not a safety guarantee.");
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            try { await Task.WhenAll(output, errors).ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return new(Name, "Timed out", "Defender scan exceeded the local wait limit. Check Windows Security for scan state/results.");
        }
    }
    private static async Task Drain(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0) { }
        // Drain without accumulating file paths, threat names or unbounded output in Pulsatilla logs.
    }
}
