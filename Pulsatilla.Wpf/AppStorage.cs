using System.IO;

namespace Pulsatilla.Wpf;

public static class AppStorage
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name);
    public static string FilePath(string name) => Path.Combine(Root, name);

    public static IReadOnlyList<string> MigrateLegacyProfile() => Migrate(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetWTool"), Root);

    // Copy known profile files only, without replacing an existing Pulsatilla profile.
    // The old profile remains available if a user still runs an older version.
    internal static IReadOnlyList<string> Migrate(string legacyRoot, string destinationRoot)
    {
        var warnings = new List<string>();
        if (!Directory.Exists(legacyRoot)) return warnings;
        try
        {
            if ((File.GetAttributes(legacyRoot) & FileAttributes.ReparsePoint) != 0)
                return ["The legacy profile is a linked directory; automatic migration was skipped."];
            foreach (var name in new[] { "settings.json", "protection.json", "usage-history.json", "blocked-apps.json" })
                CopyIfMissing(Path.Combine(legacyRoot, name), Path.Combine(destinationRoot, name), warnings);
            var legacyLogs = Path.Combine(legacyRoot, "logs");
            if (Directory.Exists(legacyLogs) && (File.GetAttributes(legacyLogs) & FileAttributes.ReparsePoint) == 0)
                foreach (var log in Directory.EnumerateFiles(legacyLogs, "netw-*.log").Take(100))
                    CopyIfMissing(log, Path.Combine(destinationRoot, "logs", "pulsatilla-" + Path.GetFileName(log)[5..]), warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { warnings.Add("Legacy profile migration could not finish: " + ex.Message); }
        return warnings;
    }

    private static void CopyIfMissing(string source, string target, List<string> warnings)
    {
        if (!File.Exists(source) || File.Exists(target)) return;
        string? temporary = null;
        try
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            { warnings.Add("A linked legacy profile file was skipped: " + Path.GetFileName(source)); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            temporary = target + ".migration-" + Guid.NewGuid().ToString("N");
            File.Copy(source, temporary, false);
            File.Move(temporary, target, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { warnings.Add("Could not migrate " + Path.GetFileName(source) + ": " + ex.Message); }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
