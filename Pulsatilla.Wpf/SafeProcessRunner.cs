using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pulsatilla.Wpf;

public sealed record ProcessExecutionResult(int ExitCode, string Output, string Error, bool OutputTruncated);

/// <summary>Literal arguments, concurrent bounded drains and cleanup of the process we started.</summary>
public static class SafeProcessRunner
{
    public static string SystemExecutable(string name)
    {
        if (name != Path.GetFileName(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A simple executable name is required.", nameof(name));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
    }

    public static async Task<ProcessExecutionResult> RunAsync(string executable, IEnumerable<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken = default, int maxOutputChars = 262144)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new ArgumentException("An existing absolute executable path is required.", nameof(executable));
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10) || maxOutputChars is < 256 or > 4_194_304)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The process could not be started.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var outputTask = DrainAsync(process.StandardOutput, maxOutputChars, deadline.Token);
        var errorTask = DrainAsync(process.StandardError, maxOutputChars, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var output = await outputTask.WaitAsync(deadline.Token);
            var error = await errorTask.WaitAsync(deadline.Token);
            return new(process.ExitCode, output.Text, error.Text, output.Truncated || error.Truncated);
        }
        catch (OperationCanceledException)
        {
            TerminateOwned(process);
            try { await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("The process exceeded its operation deadline and was stopped.");
        }
        finally { TerminateOwned(process); }
    }

    private static async Task<(string Text, bool Truncated)> DrainAsync(StreamReader reader, int maximum, CancellationToken token)
    {
        var text = new StringBuilder(Math.Min(maximum, 4096));
        var buffer = new char[4096]; var truncated = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) break;
            var retain = Math.Min(count, maximum - text.Length);
            text.Append(buffer, 0, retain);
            truncated |= retain < count;
        }
        return (text.ToString(), truncated);
    }

    internal static void TerminateOwned(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
