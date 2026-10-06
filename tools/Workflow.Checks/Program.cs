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
        var ordinary = ResponsiveWorkflowShell.FitInitialBounds(1500, 900, new Rect(0, 0, 683, 360));
        Check(ordinary.Width == 683 && ordinary.Height == 360 && ordinary.MinimumWidth == 640 && !ordinary.BelowDesignMinimum, "Initial oversized window fits logical work area");
        var tiny = ResponsiveWorkflowShell.FitInitialBounds(1500, 900, new Rect(100, 100, 500, 300));
        Check(tiny.Width == 500 && tiny.Height == 300 && tiny.MinimumWidth == 500 && tiny.MinimumHeight == 300 && tiny.BelowDesignMinimum, "Tiny work area does not force an oversized minimum");
        var automatic = ResponsiveWorkflowShell.FitInitialBounds(double.NaN, double.PositiveInfinity, new Rect(0, 0, 1920, 1080));
        Check(automatic.Width == 1280 && automatic.Height == 800, "Automatic/invalid initial dimension fallback bounded");
        try { ResponsiveWorkflowShell.FitInitialBounds(100, 100, new Rect(0, 0, 0, 0)); throw new InvalidOperationException("Empty work area accepted"); } catch (ArgumentException) { }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "ThemeColors", "DarkControls" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/Pulsatilla;component/Resources/{name}.xaml", UriKind.Relative) });
        var xaml = File.ReadAllText("Pulsatilla.Wpf/MainWindow.xaml");
        xaml = Regex.Replace(xaml, "x:Class=\"[^\"]+\"", "").Replace("clr-namespace:Pulsatilla.Wpf", "clr-namespace:Pulsatilla.Wpf;assembly=Pulsatilla");
        xaml = Regex.Replace(xaml, @"\s(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|TreeViewItem\.Expanded)=""[^""]+""", "");
        var window = (Window)XamlReader.Parse(xaml);
        var main = (TabControl)window.FindName("MainTabs");
        var navigation = new WorkflowNavigation(window, main);
        ResponsiveWorkflowShell.Apply(window, main);
        ResponsiveWorkflowShell.Apply(window, main);
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
        var cases = 0;
        foreach (var resolution in new[] { (Width: 1366, Height: 768), (Width: 1920, Height: 1080), (Width: 2560, Height: 1440), (Width: 3840, Height: 2160) })
        foreach (var scale in new[] { 1d, 1.25d, 1.5d, 1.75d, 2d })
        foreach (var theme in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System })
        foreach (var language in new[] { "en", "de", "hu", "fa", "ur" })
        {
            ThemeService.Apply(theme); LocalizationService.SetLanguage(language, persist: false); LocalizationService.Apply(window);
            var width = resolution.Width / scale; var height = resolution.Height / scale;
            window.Width = width; window.Height = height;
            navigation.Select("Adapters"); window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Check(window.ActualWidth <= width + 1 && window.ActualHeight <= height + 1, "Minimum size fits simulated resolution/DPI");
            Check(main.ActualHeight >= 80 && main.ActualWidth > 400, "Selected content retains a constrained usable viewport");
            Check(adapters.ActualHeight > 20 && adapters.ActualHeight < height, $"Adapters retain a bounded list viewport: {resolution.Width}x{resolution.Height} / {scale} / {theme} / {language}, adapter={adapters.ActualHeight:F1}, main={main.ActualHeight:F1}, window={window.ActualHeight:F1}");
            adapters.ScrollIntoView(adapters.Items[^1]); adapters.UpdateLayout();
            Check(adapters.ItemContainerGenerator.ContainerFromIndex(79) is not null, "Last adapter reachable at simulated DPI");
            foreach (var name in new[] { "ThemeSelector", "LanguageSelector", "DashboardRestartAdminButton" })
            {
                var control = (FrameworkElement)window.FindName(name);
                var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                Check(bounds.Left >= -1 && bounds.Right <= window.ActualWidth + 1 && bounds.Top >= -1 && bounds.Bottom <= window.ActualHeight + 1, "Shell control reachable: " + name);
            }
            if (resolution.Width == 1366 && scale == 2)
            {
                var image = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32); image.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(output, $"compact-1366x768-at200-{theme}-{language}.png")); encoder.Save(file); renders++;
            }
            cases++;
        }
        Check(main.TabStripPlacement == Dock.Left, "Wide viewport retains sidebar navigation");
        window.Width = 683; window.Height = 384; window.UpdateLayout();
        Check(main.TabStripPlacement == Dock.Top, "Compact viewport uses horizontally scrollable navigation");
        var headers = (ScrollViewer)main.Template.FindName("HeaderScroller", main);
        var about = main.Items.Cast<TabItem>().Last(); about.BringIntoView(); window.UpdateLayout();
        Check(headers.ScrollableWidth > 0 && headers.HorizontalOffset > 0, "Final main navigation header reachable by scrolling");
        var originalPolicyActions = (FrameworkElement)window.FindName("PolicyActions");
        Check(originalPolicyActions.Parent is not null, "Named legacy action panel retained");
        window.Close();
        Console.WriteLine($"Workflow navigation PASS: retained controls, parent shortcuts, bounded 80-adapter list, {cases} simulated resolution/DPI/theme/language geometry cases and {renders} renders. Actual display DPI/hardware is NOT TESTED. No MainWindow constructor, OS commands or capture executed.");
    }
    private static void Check(bool result, string name) { if (!result) throw new InvalidOperationException(name); }
}
