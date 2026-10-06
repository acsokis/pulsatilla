using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pulsatilla.Wpf;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "ThemeColors", "DarkControls" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/Pulsatilla;component/Resources/{name}.xaml", UriKind.Relative) });
        var xaml = File.ReadAllText("Pulsatilla.Wpf/MainWindow.xaml");
        xaml = Regex.Replace(xaml, "x:Class=\"[^\"]+\"", "").Replace("clr-namespace:Pulsatilla.Wpf", "clr-namespace:Pulsatilla.Wpf;assembly=Pulsatilla");
        xaml = Regex.Replace(xaml, @"\s(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|TreeViewItem\.Expanded)=""[^""]+""", "");
        var window = (Window)XamlReader.Parse(xaml);
        var main = (TabControl)window.FindName("MainTabs");
        var navigation = new WorkflowNavigation(window, main);
        window.ShowInTaskbar = false; window.ShowActivated = false;
        window.Left = -20000; window.Top = -20000; window.Show();
        Check(main.Items.Count == 10, "Ten main workflow sections");
        Check(navigation.Select("Firewall") && navigation.CurrentPage == "Security", "Firewall shortcut selects its parent");
        Check(navigation.Select("Events") && navigation.CurrentPage == "Security", "Alerts shortcut selects its parent");
        Check(navigation.Select("Adapters") && navigation.CurrentPage == "Network", "Adapter page reachable");
        var adapters = (ListBox)window.FindName("AdapterList");
        for (var i = 0; i < 80; i++) adapters.Items.Add("Synthetic adapter " + i);
        var output = Path.Combine("dist", "workflow-qa"); Directory.CreateDirectory(output);
        var renders = 0;
        foreach (var theme in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System })
        foreach (var language in new[] { "en", "de", "hu", "fa", "ur" })
        {
            ThemeService.Apply(theme); LocalizationService.SetLanguage(language, persist: false); LocalizationService.Apply(window);
            window.Width = 1120; window.Height = 700;
            window.Measure(new Size(1120, 700)); window.Arrange(new Rect(0, 0, 1120, 700)); window.UpdateLayout();
            Check(adapters.ActualHeight > 0 && adapters.ActualHeight < 700, "Adapter list bounded to viewport");
            adapters.ScrollIntoView(adapters.Items[^1]);
            adapters.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Check(adapters.ItemContainerGenerator.ContainerFromIndex(79) is not null, "Last adapter reachable by scrolling");
            var image = new RenderTargetBitmap(1120, 700, 96, 96, PixelFormats.Pbgra32); image.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(output, $"adapters-{theme}-{language}.png"))) encoder.Save(file);
            renders++;
        }
        foreach (var page in new[] { "Dashboard", "Network Path", "Live Traffic", "Applications", "Connections", "Packet Inspector", "Protocol Analysis", "Traffic Sources", "Scan", "Overview", "Inspector & Sender Rules", "VPN", "History", "Settings", "About" })
        {
            Check(navigation.Select(page), "Reachable: " + page);
            window.Measure(new Size(1120, 700)); window.Arrange(new Rect(0, 0, 1120, 700)); window.UpdateLayout();
        }
        Check(!navigation.Select("not a real page"), "Unknown navigation refused");
        window.Close();
        Console.WriteLine($"Workflow navigation PASS: retained controls, parent shortcuts, bounded 80-adapter list and {renders} theme/language renders. No MainWindow constructor, OS commands or capture executed.");
    }
    private static void Check(bool result, string name) { if (!result) throw new InvalidOperationException(name); }
}
