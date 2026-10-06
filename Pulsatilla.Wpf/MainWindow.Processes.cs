using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Pulsatilla.Wpf;

public partial class MainWindow
{
    private readonly CancellationTokenSource _workflowProcessLifetime = new();
    private int _wifiInformationPending;
    private int _hardwareInformationPending;
    private int _nmapOperationPending;
    private int _wifiReviewPending;
    private string? _selectedNmapExecutable;

    private string? SelectNmapExecutable()
    {
        var candidates = new[] { _selectedNmapExecutable,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nmap", "nmap.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Nmap", "nmap.exe") };
        foreach (var candidate in candidates)
            if (candidate is not null && File.Exists(candidate))
            {
                try { return LocalFileInspector.ValidateLocalFile(candidate); }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
            }
        var dialog = new OpenFileDialog { Title = "Select your installed Nmap executable", Filter = "Nmap executable (nmap.exe)|nmap.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return null;
        if (!Path.GetFileName(dialog.FileName).Equals("nmap.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select the installed nmap.exe executable.");
        return _selectedNmapExecutable = LocalFileInspector.ValidateLocalFile(dialog.FileName);
    }

    // The window's closing workflow calls this before disposing its other services.
    private void DisposeWorkflowProcesses() => _ = CancelWorkflowProcessesAsync();
    private async Task CancelWorkflowProcessesAsync()
    {
        // CancelAsync marks the token immediately and avoids invoking owned-process registrations on the closing UI thread.
        try { await _workflowProcessLifetime.CancelAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
    }
}
