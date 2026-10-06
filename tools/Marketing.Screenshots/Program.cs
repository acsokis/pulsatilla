using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pulsatilla.Wpf;

// Render the real application XAML with synthetic data and all operation handlers removed.
// This process does not start MainWindow/App, capture packets, read a profile or change a firewall.
internal static class Program
{
    private const int Width = 1500, Height = 900;
    [STAThread]
    private static void Main()
    {
        var root = Directory.GetCurrentDirectory();
        var xamlPath = Path.Combine(root, "Pulsatilla.Wpf", "MainWindow.xaml");
        if (!File.Exists(xamlPath)) throw new InvalidOperationException("Run from the Pulsatilla repository root.");
        var output = Path.Combine(root, "docs", "launch", "assets", "screenshots");
        Directory.CreateDirectory(output);
        var originOutput = Path.Combine(root, "docs", "origin", "screenshots");
        Directory.CreateDirectory(originOutput);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[] { "ThemeColors", "DarkControls" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"/Pulsatilla;component/Resources/{resource}.xaml", UriKind.Relative) });
        ThemeService.Apply(ThemeMode.Dark);
        SaveLogo(Path.Combine(root, "docs", "launch", "assets", "marketing", "pulsatilla-logo.png"));
        var xaml = File.ReadAllText(xamlPath);
        xaml = Regex.Replace(xaml, "x:Class=\"[^\"]+\"", "")
            .Replace("clr-namespace:Pulsatilla.Wpf", "clr-namespace:Pulsatilla.Wpf;assembly=Pulsatilla");
        xaml = Regex.Replace(xaml, @"\s(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|TreeViewItem\.Expanded)=""[^""]+""", "");
        var window = (Window)XamlReader.Parse(xaml);
        window.WindowStyle = WindowStyle.None; window.ShowInTaskbar = false; window.ShowActivated = false;
        window.Left = -20000; window.Top = -20000; window.Width = Width; window.Height = Height;
        T Find<T>(string name) where T : class => (T)window.FindName(name);
        void Text(string name, string value) => Find<TextBlock>(name).Text = value;
        LocalizationService.SetLanguage("en", persist: false); LocalizationService.Apply(window);
        Text("MonitorStatus", "DEMO DATA · no live capture");
        Text("LastUpdateLabel", "Demo · simulated traffic / example.test addresses");
        Text("SelectedAdapterLabel", "Demo Ethernet · simulated adapter throughput");
        Text("DownloadRate", "2.7 MiB/s"); Text("UploadRate", "860 KiB/s"); Text("AdapterStatus", "Up · demo");
        Text("Ipv4Value", "192.0.2.10 (example)"); Text("GatewayValue", "192.0.2.1 (example)");
        Text("MacValue", "02:00:00:00:00:10 (synthetic)"); Text("LinkSpeedValue", "1 Gbit/s · demo"); Text("WifiInfoValue", "Ethernet · demo adapter");
        Text("DashboardCpuValue", "8% · demo"); Text("DashboardMemoryValue", "34% · demo");
        Text("DashboardConnectionsValue", "3 demo flows"); Text("DashboardPacketsValue", "Simulated packet observations");
        Find<ListBox>("AdapterList").Items.Add("Demo Ethernet\n192.0.2.10\nExample adapter · no system inventory");
        Find<ListBox>("AdapterList").Foreground = ThemeService.Brush("E6F2E8");
        Find<ListBox>("AdapterList").SelectedIndex = 0;
        Find<ComboBox>("LanguageSelector").ItemsSource = LocalizationService.Languages;
        Find<ComboBox>("LanguageSelector").SelectedValue = "en";
        Find<ComboBox>("ThemeSelector").ItemsSource = new[] { new { Name = "Dark", Mode = ThemeMode.Dark }, new { Name = "Light", Mode = ThemeMode.Light } };
        Find<ComboBox>("ThemeSelector").SelectedValue = ThemeMode.Dark;
        var chart = Find<VisualTrafficChart>("ThroughputChart");
        var system = Find<NetworkChart>("SystemActivityChart");
        system.MinimumScale = 100;
        var start = DateTime.UtcNow.AddSeconds(-59.5);
        for (var index = 0; index < 120; index++)
        {
            chart.AddSample(300_000 + 2_500_000 * Math.Pow(Math.Max(0, Math.Sin(index * .11)), 5),
                80_000 + 800_000 * Math.Pow(Math.Max(0, Math.Cos(index * .15)), 5), start.AddMilliseconds(index * 500));
            system.AddSample(8 + 3 * Math.Sin(index * .12), 34 + Math.Sin(index * .07));
        }
        Text("ThroughputSummaryText", chart.Summary + " · simulated sample timeline");
        var rows = new[]
        {
            new LiveTrafficRow("Demo Browser", @"C:\Demo\browser.exe", "203.0.113.20", "TCP") { Trust = ApplicationTrust.Trusted },
            new LiveTrafficRow("Demo Sync", @"C:\Demo\sync.exe", "198.51.100.8", "TCP"),
            new LiveTrafficRow("Demo Updater", @"C:\Demo\updater.exe", "203.0.113.30", "UDP")
        };
        for (var index = 0; index < rows.Length; index++) { rows[index].AddPacket(85_000 * (index + 1), true); rows[index].AddPacket(2_250_000 / (index + 1), false); }
        Find<DataGrid>("LiveTrafficGrid").ItemsSource = rows;
        Text("LiveTrafficStatus", "DEMO DATA · synthetic per-app flows; no packet capture is running.");
        var tracker = new TrafficSourceTracker();
        for (var index = 0; index < 72; index++)
        {
            var row = rows[index % rows.Length];
            tracker.Observe(new(row.Host, "192.0.2.10", row.Protocol, 1400 + index * 10, 443, 40000, "", null, []), "192.0.2.10", row.Application, row.ExecutablePath);
        }
        var ipRows = tracker.TopIps(); var appRows = tracker.TopApplications();
        Find<TreeView>("TopIpSourcesTree").ItemsSource = ipRows.Select(row => { var node = new TrafficSourceNode(row.Key) { IsExpanded = row == ipRows[0] }; node.Update(row, ipRows[0].Packets, "Example remote host"); return node; }).ToArray();
        Find<TreeView>("TopSoftwareSourcesTree").ItemsSource = appRows.Select(row => { var node = new TrafficSourceNode(row.Key) { IsExpanded = row == appRows[0] }; node.Update(row, appRows[0].Bytes, byBytes: true); return node; }).ToArray();
        Find<ListBox>("EventsList").ItemsSource = new[] { "DEMO · new local application: Demo Browser", "DEMO · possible scan-like pattern — review signal", "DEMO · repeated RDP connection pattern — verify independently", "DEMO · routine DNS rotation is quiet by default", "DEMO · all addresses shown are reserved examples" };
        Text("SecurityStatusLabel", "Demo · synthetic observations"); Text("PacketCountLabel", "72 demo observations");
        Text("PacketSummaryLabel", "Synthetic sample: TCP / UDP · 3 local demo applications");
        Find<TextBox>("HexInspector").Text = "DEMO: no live packet payload or personal traffic is displayed.";
        var message = new EmailMessage("billing@example.test", "reply@different.example.test", "demo@example.test", "Urgent: verify your account", "Please send your password and verification code immediately. https://example.test/verify");
        Find<TextBox>("EmailFromInput").Text = message.From; Find<TextBox>("EmailReplyInput").Text = message.ReplyTo;
        Find<TextBox>("EmailRecipientsInput").Text = message.Recipients; Find<TextBox>("EmailSubjectInput").Text = message.Subject;
        Find<TextBox>("EmailBodyInput").Text = message.Body;
        var result = EmailSafetyService.Analyze(message);
        Text("EmailImportDetailsText", "DEMO MESSAGE · synthetic example.test content");
        Text("EmailResultText", result.Summary + "\n\n" + string.Join("\n\n", result.Findings.Select(finding => "• " + finding)));
        var creator = AboutContent.Creator;
        Text("CreatorNameText", creator.Name); Text("CreatorLocationText", creator.Location);
        Text("AboutVersionText", "Pulsatilla " + (typeof(ProductInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.2") + " · Community · MIT");
        Find<FlowDocumentScrollViewer>("ReadmeViewer").Document = AboutContent.CreateReadmeDocument();
        Find<FlowDocumentScrollViewer>("LicenseViewer").Document = AboutContent.CreateDocument("# MIT License\n\n" + AboutContent.LicenseText);
        Find<FlowDocumentScrollViewer>("ServicesViewer").Document = AboutContent.CreateDocument(AboutContent.Services);
        Find<FlowDocumentScrollViewer>("PrivacyViewer").Document = AboutContent.CreateDocument(AboutContent.Privacy);
        Find<FlowDocumentScrollViewer>("OriginViewer").Document = AboutContent.CreateDocument(AboutContent.Origin);
        var tabs = Find<TabControl>("MainTabs");
        var navigation = new WorkflowNavigation(window, tabs, EditionCapabilities.Community);
        ResponsiveWorkflowShell.Apply(window, tabs);
        // Preserve export dimensions independently of the workstation work area.
        window.Width = Width; window.Height = Height;
        using var network = new NetworkPathPanel(new SyntheticTopology());
        network.RefreshAsync().GetAwaiter().GetResult();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        navigation.InstallNetworkPanel(network);
        navigation.PathSummary.Text = "DEMO · Ethernet → example gateway 192.0.2.1";
        navigation.InspectionSummary.Text = "COMMUNITY 1.1.0-beta.2 · DEMO DATA · no capture or system changes";
        var applications = new ApplicationTrafficPanel();
        applications.Refresh(rows, []);
        navigation.ApplicationsHost.Content = applications;
        void Save(string name, string page)
        {
            if (!navigation.Select(page)) throw new InvalidOperationException("Unknown workflow page: " + page); window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
            Console.WriteLine("Saved " + name);
        }
        try
        {
            window.Show();
            Save("community-beta2-dashboard-dark-en.png", "Dashboard");
            Save("community-beta2-network-path-dark-en.png", "Network Path");
            Save("community-beta2-live-traffic-dark-en.png", "Live Traffic");
            Save("community-beta2-security-sources-dark-en.png", "Traffic Sources");
            Save("community-beta2-email-review-dark-en.png", "Inspector & Sender Rules");
            ThemeService.Apply(ThemeMode.Light); Find<ComboBox>("ThemeSelector").SelectedValue = ThemeMode.Light;
            Save("community-beta2-dashboard-light-en.png", "Dashboard");
            ThemeService.Apply(ThemeMode.Dark); Find<ComboBox>("ThemeSelector").SelectedValue = ThemeMode.Dark;
            LocalizationService.SetLanguage("de", persist: false); LocalizationService.Apply(window);
            Find<ComboBox>("LanguageSelector").SelectedValue = "de";
            Text("MonitorStatus", "DEMODATEN · keine Live-Erfassung");
            Text("LastUpdateLabel", "Demo · simulierte Daten / Beispieldaten");
            Save("community-beta2-dashboard-dark-de.png", "Dashboard");
        }
        finally { window.Close(); app.Shutdown(); }
    }

    private sealed class SyntheticTopology : INetworkTopologyReader
    {
        public NetworkTopologySnapshot Read()
        {
            var adapters = new[] { new NetworkAdapterInfo("demo-ethernet", "Demo Ethernet",
                "Synthetic adapter — not this computer's inventory", 1, 1, OperationalStatus.Up,
                NetworkInterfaceType.Ethernet, AdapterClassification.Physical,
                ["192.0.2.10"], ["192.0.2.1"], ["192.0.2.53"], true, 0) };
            InterfaceMetricState[] metrics = [new(1, AddressFamily.InterNetwork, true, 25)];
            NetworkRouteInfo[] routes = [new("0.0.0.0", 0, "192.0.2.1", 1,
                AddressFamily.InterNetwork, 10, 25, true, false, "Demo Ethernet")];
            return new(adapters, routes, metrics, 1, null, [], null,
                new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc));
        }
    }
    private static void SaveLogo(string output)
    {
        // Deterministic vector artwork, separate from the AI-generated flower banners.
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            Brush Color(string value) => new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
            drawing.DrawRoundedRectangle(Color("#080e0d"), null, new Rect(0, 0, 1280, 320), 24, 24);
            var petal = Geometry.Parse("M0,0 C-57,-31 -64,-102 0,-120 C64,-102 57,-31 0,0 Z");
            drawing.PushTransform(new TranslateTransform(155, 156));
            for (var angle = 0; angle < 360; angle += 60)
            {
                drawing.PushTransform(new RotateTransform(angle));
                drawing.DrawGeometry(Color("#b28af5"), new Pen(Color("#d9c5ff"), 2), petal);
                drawing.Pop();
            }
            drawing.DrawEllipse(Color("#ffcc71"), null, new Point(0, 0), 28, 28);
            var network = new Pen(Color("#162a1e"), 3);
            drawing.DrawLine(network, new Point(-18, 0), new Point(18, 0));
            drawing.DrawLine(network, new Point(0, -18), new Point(0, 18));
            drawing.DrawLine(network, new Point(-13, -13), new Point(13, 13));
            drawing.DrawLine(network, new Point(-13, 13), new Point(13, -13));
            foreach (var node in new[] { new Point(-18, 0), new Point(18, 0), new Point(0, -18), new Point(0, 18) })
                drawing.DrawEllipse(Color("#90e0a9"), null, node, 5, 5);
            drawing.Pop();
            void Text(string value, double x, double baseline, double size, string color, double tracking = 0, bool bold = false)
            {
                foreach (var character in value)
                {
                    var text = new FormattedText(character.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                        bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, Color(color), 1);
                    drawing.DrawText(text, new Point(x, baseline - text.Baseline)); x += text.WidthIncludingTrailingWhitespace + tracking;
                }
            }
            Text("PULSATILLA", 320, 144, 87, "#f2f6f3", 8, bold: true);
            Text("Network clarity. Security insight.", 324, 198, 30, "#c5d7ca");
            Text("GABOR WEB", 324, 251, 23, "#90e0a9", 4);
        }
        var bitmap = new RenderTargetBitmap(1280, 320, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output); encoder.Save(stream);
        Console.WriteLine("Saved pulsatilla-logo.png");
    }
}
