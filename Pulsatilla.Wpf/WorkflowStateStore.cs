using System.IO;
using System.Text.Json;

namespace Pulsatilla.Wpf;

/// <summary>Small navigation preference only; adapter AUTO selection is never persisted as a pin.</summary>
public sealed class WorkflowStateStore
{
    private static readonly HashSet<string> Pages = new(["Dashboard", "Network", "Inspect", "Scan", "Security", "Email", "History", "Settings", "About"], StringComparer.Ordinal);
    private readonly string _path;
    private readonly HashSet<string> _pages;
    public WorkflowStateStore(string? path = null, IEnumerable<string>? registeredPages = null)
    {
        _path = path ?? AppStorage.FilePath("workflow.json");
        _pages = registeredPages is null ? new(Pages, StringComparer.Ordinal) : new(registeredPages.Take(64).Where(page => page.Length is > 0 and <= 80), StringComparer.Ordinal);
    }
    public string LoadPage()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 4096) return "Dashboard";
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(_path));
            return state is not null && _pages.Contains(state.Page) ? state.Page : "Dashboard";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return "Dashboard"; }
    }
    public void SavePage(string page)
    {
        if (!_pages.Contains(page)) return;
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
