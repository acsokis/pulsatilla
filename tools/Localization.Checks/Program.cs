using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Pulsatilla.Wpf;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var directory = Path.Combine(root, "Pulsatilla.Wpf", "Resources", "Locales");
        Dictionary<string, string> Read(string code) => JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(directory, code + ".json")))!;
        var english = Read("en");
        var resources = english.Keys.Select(LocalizationService.ResourceKey).ToHashSet();
        var xaml = File.ReadAllText(Path.Combine(root, "Pulsatilla.Wpf", "MainWindow.xaml"));
        foreach (Match match in Regex.Matches(xaml, @"\{DynamicResource (Loc_[A-F0-9]+)\}"))
            Check(resources.Contains(match.Groups[1].Value), "Missing XAML resource: " + match.Value);
        var application = new Application();
        var label = new TextBlock();
        label.SetResourceReference(TextBlock.TextProperty, LocalizationService.ResourceKey("Dashboard"));
        var panel = new StackPanel();
        panel.Children.Add(label);
        var grid = new DataGrid();
        var column = new DataGridTextColumn();
        LocalizationService.SetSource(column, "Application");
        grid.Columns.Add(column);
        panel.Children.Add(grid);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "First", Content = new TextBlock() });
        var inactiveGrid = new DataGrid();
        var inactiveColumn = new DataGridTextColumn();
        LocalizationService.SetSource(inactiveColumn, "Status");
        inactiveGrid.Columns.Add(inactiveColumn);
        tabs.Items.Add(new TabItem { Header = "Inactive", Content = inactiveGrid });
        panel.Children.Add(tabs);
        var window = new Window { Content = panel };
        foreach (var language in LocalizationService.Languages)
        {
            var pack = Read(language.Code);
            Check(pack.Count > 0, "Empty pack: " + language.Code);
            foreach (var pair in pack)
            {
                Check(english.ContainsKey(pair.Key), "Unknown source in " + language.Code + ": " + pair.Key);
                Check(!string.IsNullOrWhiteSpace(pair.Value), "Empty translation: " + pair.Key);
                static string[] Placeholders(string text) => Regex.Matches(text, @"\{\d+(?:[^}]*)\}")
                    .Select(match => match.Value).Order().ToArray();
                Check(Placeholders(pair.Key).SequenceEqual(Placeholders(pair.Value)),
                    "Changed placeholders in " + language.Code + ": " + pair.Key);
            }
            Check(LocalizationService.SetLanguage(language.Code, persist: false), "Cannot load " + language.Code);
            LocalizationService.Apply(window);
            Check(label.Text == pack["Dashboard"], "Dynamic language switch failed: " + language.Code);
            Check((string)column.Header == pack["Application"], "Column header switch failed: " + language.Code);
            Check((string)inactiveColumn.Header == pack["Status"], "Inactive tab header switch failed: " + language.Code);
            Check(window.FlowDirection == (language.Code is "fa" or "ur" or "ar"
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), "Incorrect direction: " + language.Code);
            Check(LocalizationService.Translate("missing test key") == "missing test key", "Source fallback failed");
            var expected = pack.GetValueOrDefault("Updated {0:HH:mm:ss}  /  500 ms sampling",
                "Updated {0:HH:mm:ss}  /  500 ms sampling");
            var time = new DateTime(2026, 10, 5, 12, 34, 56);
            Check(LocalizationService.Format($"Updated {time:HH:mm:ss}  /  500 ms sampling") ==
                string.Format(expected, time), "Formatted translation failed: " + language.Code);
            Console.WriteLine($"{language.Code}: {pack.Count}/{english.Count} entries; resources, formatting, switching and RTL passed");
        }
        Check(!LocalizationService.SetLanguage("unsupported", persist: false), "Invalid language accepted");
        Check(LocalizationService.SetLanguage("en", persist: false), "English reload failed");
        LocalizationService.Apply(window);
        Check(label.Text == "Dashboard", "Return to English failed");
        window.Close();
        application.Shutdown();
        Console.WriteLine("All localization checks passed. User settings were not modified.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
