using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pulsatilla.Wpf;

public sealed class WindowsFirewallRuleService
{
    private readonly string _statePath = AppStorage.FilePath("blocked-apps.json");
    private readonly HashSet<string> _blockedPaths;

    public WindowsFirewallRuleService()
    {
        try
        {
            var paths = File.Exists(_statePath) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(_statePath)) : [];
            _blockedPaths = new HashSet<string>(paths ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception) { _blockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    public bool IsBlocked(string executablePath) => _blockedPaths.Contains(executablePath);

    public async Task<(bool Success, string Message)> SetBlockedAsync(string executablePath, bool blocked)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath) || blocked && !File.Exists(executablePath))
            return (false, "The executable path is unavailable; no firewall rule was changed.");

        var names = GetManagedRuleNames(executablePath);
        HashSet<string> existing;
        try { existing = FindExistingRules(names); }
        catch (Exception ex) { return (false, "Could not inspect application firewall rules: " + ex.Message); }
        var added = new List<string>();
        var changes = blocked ? names.Take(2).Where(name => !existing.Contains(name)) : names.Where(existing.Contains);
        foreach (var name in changes)
        {
            var arguments = new List<string> { "advfirewall", "firewall" };
            if (blocked)
            {
                var direction = name.EndsWith("-out", StringComparison.Ordinal) ? "out" : "in";
                arguments.AddRange(["add", "rule", $"name={name}", $"dir={direction}", "action=block",
                    $"program={executablePath}", "enable=yes", "profile=any"]);
            }
            else
            {
                arguments.AddRange(["delete", "rule", $"name={name}"]);
            }

            var result = await RunNetshAsync(arguments);
            if (!result.Success)
            {
                foreach (var addedName in added)
                    await RunNetshAsync(["advfirewall", "firewall", "delete", "rule", $"name={addedName}"]);
                return (false, $"Could not change the application firewall rule. Try running Pulsatilla as Administrator. {result.Message}");
            }
            if (blocked) added.Add(name);
        }

        if (blocked) _blockedPaths.Add(executablePath);
        else _blockedPaths.Remove(executablePath);
        await SaveStateAsync();
        return (true, blocked ? "Inbound and outbound traffic blocked." : "Pulsatilla firewall rules removed.");
    }

    private async Task<(bool Success, string Message)> RunNetshAsync(IEnumerable<string> arguments)
    {
        try
        {
            var result = await SafeProcessRunner.RunAsync(SafeProcessRunner.SystemExecutable("netsh.exe"),
                arguments, TimeSpan.FromSeconds(15), maxOutputChars: 65536);
            return result.ExitCode == 0 ? (true, result.Output.Trim()) : (false, (result.Error + " " + result.Output).Trim());
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private async Task SaveStateAsync()
    {
        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _statePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(_blockedPaths));
        File.Move(temporaryPath, _statePath, true);
    }

    internal static IReadOnlyList<string> GetManagedRuleNames(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..12];
        // Legacy identifiers are removal aliases; new rules always use Pulsatilla.
        return [$"Pulsatilla-{hash}-out", $"Pulsatilla-{hash}-in", $"NetWTool-{hash}-out", $"NetWTool-{hash}-in"];
    }

    private static HashSet<string> FindExistingRules(IReadOnlyList<string> names)
    {
        object? policy = null; object? rules = null;
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2") ?? throw new InvalidOperationException("Windows Firewall is unavailable.");
            policy = Activator.CreateInstance(type)!;
            rules = ((dynamic)policy).Rules;
            foreach (var name in names)
            {
                object? rule = null;
                try { rule = ((dynamic)rules).Item(name); found.Add(name); }
                catch (COMException ex) when (ex.HResult is unchecked((int)0x80070002) or unchecked((int)0x80070490)) { }
                finally { if (rule is not null) Marshal.FinalReleaseComObject(rule); }
            }
        }
        finally
        {
            if (rules is not null) Marshal.FinalReleaseComObject(rules);
            if (policy is not null) Marshal.FinalReleaseComObject(policy);
        }
        return found;
    }
}
