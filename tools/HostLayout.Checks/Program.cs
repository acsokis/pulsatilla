using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pulsatilla.Wpf;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "ThemeColors.xaml", "DarkControls.xaml" })
        {
            using var stream = File.OpenRead(Path.Combine("Pulsatilla.Wpf", "Resources", name));
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        var output = Path.Combine("dist", "host-layout-qa"); Directory.CreateDirectory(output);
        var renders = 0;
        foreach (var theme in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System })
        foreach (var language in new[] { "en", "de", "hu", "fa", "ur" })
        {
            ThemeService.Apply(theme); LocalizationService.SetLanguage(language, persist: false);
            using var network = new NetworkPathPanel(new SyntheticTopology());
            network.RefreshAsync().GetAwaiter().GetResult();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            foreach (var section in new[] { "Network Path", "Adapters", "Routes", "Interface Priority", "Diagnostics" })
            foreach (var size in new[] { new Size(660, 130), new Size(1080, 580) })
            {
                network.SelectSection(section); Layout(network, size);
                var selected = Descendants(network).OfType<TabControl>().First().SelectedItem as TabItem;
                Check(selected?.Content is FrameworkElement { ActualHeight: > 20 }, "Network section lost its constrained viewport");
                if (section is "Adapters" or "Routes" or "Interface Priority")
                    Check(Descendants((DependencyObject)selected!.Content).OfType<DataGrid>().All(grid => grid.ActualHeight > 10 && grid.ActualHeight <= size.Height), "Network table became unreachable below header/actions");
                if (section == "Interface Priority")
                {
                    var actions = Descendants((DependencyObject)selected!.Content).OfType<ScrollViewer>().FirstOrDefault(s => s.Content is StackPanel);
                    Check(actions is not null && actions.ActualHeight <= size.Height * 0.65, "Metric actions exceeded bounded compact viewport");
                    actions!.ScrollToEnd(); Layout(network, size);
                    Check(actions.VerticalOffset >= Math.Max(0, actions.ScrollableHeight - 1), "Metric action bottom is not scroll reachable");
                    var restore = Descendants(actions).OfType<Button>().First(b => Equals(b.Content, "Restore Automatic Metric"));
                    restore.BringIntoView(); Layout(network, size);
                    var origin = restore.TransformToAncestor(actions).Transform(new Point());
                    Check(origin.Y + restore.ActualHeight > 0 && origin.Y < actions.ActualHeight, "Restore Automatic button is not reachable after scrolling");
                }
                Render(network, size, Path.Combine(output, $"network-{section.Replace(' ', '-')}-{theme}-{language}-{(int)size.Height}.png")); renders++;
            }
            var apps = new ApplicationTrafficPanel();
            apps.Refresh([new LiveTrafficRow("Synthetic browser", "C:\\Synthetic\\browser.exe", "198.51.100.10", "TCP") { ProcessId = 1234, LocalAddress = "192.0.2.5", LocalPort = 50000, RemotePort = 443 }],
                [new OwnedSocketSnapshot(new(1234, "Synthetic browser", "C:\\Synthetic\\browser.exe"), "TCP", "192.0.2.5", 50000, "198.51.100.10", 443, "Established", true)]);
            var hex = new HexViewer(); var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(); hex.SetBytes(bytes, HexPatternEngine.Default.Analyze(bytes, HexPatternContext.File));
            using var service = new ServiceBannerPanel();
            foreach (var (name, control) in new (string, FrameworkElement)[] { ("applications", apps), ("hex", hex), ("service-initial", service) })
            {
                var size = new Size(1080, 580); Layout(control, size); Check(control.ActualHeight <= size.Height, "Host exceeded allocation");
                Render(control, size, Path.Combine(output, $"{name}-{theme}-{language}.png")); renders++;
            }
        }
        app.Shutdown();
        Console.WriteLine($"Host layout checks passed: {_checks} assertions, {renders} offscreen renders. Synthetic topology/app/byte fixtures; initial service controls remain unconnected. No Window shown, native capture, settings changes or external requests. DIP allocation simulation is not real hardware DPI validation.");
    }
    private static void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Layout(FrameworkElement control, Size size)
    {
        if (control is not HexViewer) control.FlowDirection = LocalizationService.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        control.Measure(size); control.Arrange(new Rect(size)); control.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        control.Measure(size); control.Arrange(new Rect(size)); control.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
    private static void Render(Visual control, Size size, string path)
    {
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(control);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class SyntheticTopology : INetworkTopologyReader
    {
        public NetworkTopologySnapshot Read()
        {
            var adapters = Enumerable.Range(1, 30).Select(i => new NetworkAdapterInfo("synthetic-" + i, "Synthetic Adapter " + i, "Synthetic physical adapter", i, i, OperationalStatus.Up,
                NetworkInterfaceType.Ethernet, AdapterClassification.Physical, ["192.0.2." + i], ["192.0.2.254"], ["192.0.2.53"], true, 0)).ToArray();
            var metrics = adapters.Select(a => new InterfaceMetricState(a.InterfaceIndex, AddressFamily.InterNetwork, true, 25)).ToArray();
            var routes = adapters.Select(a => new NetworkRouteInfo("0.0.0.0", 0, "192.0.2.254", a.InterfaceIndex, AddressFamily.InterNetwork, 10, 25, true, false, a.Name)).ToArray();
            return new(adapters, routes, metrics, 1, null, [], null, new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc));
        }
    }
}
