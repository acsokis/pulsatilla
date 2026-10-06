using System.IO;
using System.Text.Json;

namespace Pulsatilla.Wpf;

/// <summary>Small navigation preference only; adapter AUTO selection is never persisted as a pin.</summary>
public sealed class WorkflowStateStore
{
    private static readonly HashSet<string> Pages = new(["Dashboard", "Network", "Inspect", "Scan", "Security", "Email", "VPN", "History", "Settings", "About"], StringComparer.Ordinal);
    private readonly string _path;
    public WorkflowStateStore(string? path = null) => _path = path ?? AppStorage.FilePath("workflow.json");
    public string LoadPage()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 4096) return "Dashboard";
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(_path));
            return state is not null && Pages.Contains(state.Page) ? state.Page : "Dashboard";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return "Dashboard"; }
    }
    public void SavePage(string page)
    {
        if (!Pages.Contains(page)) return;
        var temporary = _path + ".pending";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new State(page)));
            File.Move(temporary, _path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private sealed record State(string Page);
}
