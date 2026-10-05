using System.Buffers.Binary;
using System.Diagnostics;
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
    private const string LocalIp = "192.168.1.5";
    private static readonly DateTime Start = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    [STAThread]
    private static void Main()
    {
        CheckNetwork(); CheckEmail(); CheckSettings();
        ExtendedChecks.CheckServices();
        ProductChecks.CheckMigrationAndProvider();
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using (var colors = File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), "Pulsatilla.Wpf", "Resources", "ThemeColors.xaml")))
            application.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(colors));
        ThemeService.Apply(ThemeMode.Dark);
        CheckGraphics(); CheckDropdown(); CheckFullLayout();
        ExtendedChecks.CheckVisuals();
        application.Shutdown();
        Console.WriteLine("All protection checks passed; no network requests or firewall changes were made.");
    }
    private static PacketObservation Packet(int port, int seconds = 0, byte flags = 2, string source = "8.8.8.8") =>
        new(source, LocalIp, "TCP", 40, 51000, port, "", null, []) { TcpFlags = flags, CapturedAtUtc = Start.AddSeconds(seconds) };
    private static PacketObservation Dns(string answer, int seconds = 0) =>
        new("8.8.8.8", LocalIp, "UDP", 80, 53, 51000, "", "example.com", [answer]) { CapturedAtUtc = Start.AddSeconds(seconds) };
    private static void CheckNetwork()
    {
        var monitor = new NetworkThreatMonitor();
        for (var i = 0; i < 300; i++) Assert(monitor.Observe(Packet(443, flags: 0x12), LocalIp).Count == 0, "SYN/ACK must not count as a scan");
        monitor.Observe(Dns("8.8.4.4"), LocalIp);
        Assert(monitor.Observe(Dns("1.1.1.1", 1), LocalIp).Count == 0, "Ordinary public DNS rotation generated an alarm");
        Assert(monitor.Observe(Dns("10.0.0.1", 2), LocalIp).Any(signal => signal.Level == "DNS ANOMALY"), "Rebinding indicator missing");
        Assert(monitor.Observe(Dns("10.0.0.2", 3), LocalIp).Count == 0, "DNS alerts not rate-limited");
        monitor.Reset();
        for (var i = 1; i < 12; i++) Assert(monitor.Observe(Packet(i), LocalIp).Count == 0, "Scan alerted before threshold");
        Assert(monitor.Observe(Packet(12), LocalIp).Any(), "Inbound port scan missing");
        Assert(monitor.Observe(Packet(13), LocalIp).Count == 0, "Scan alerts not rate-limited");
        monitor.Reset();
        for (var i = 0; i < 19; i++) Assert(monitor.Observe(Packet(3389), LocalIp).Count == 0, "RDP alerted before threshold");
        Assert(monitor.Observe(Packet(3389), LocalIp).Any(signal => signal.Message.Contains("RDP")), "Repeated RDP attempt indicator missing");
        monitor.Reset();
        for (var i = 0; i < 149; i++) Assert(monitor.Observe(Packet(443), LocalIp).Count == 0, "SYN burst alerted before threshold");
        Assert(monitor.Observe(Packet(443), LocalIp).Any(signal => signal.Message.Contains("SYN")), "SYN burst missing");
        monitor.Reset();
        for (var i = 0; i < 20; i++) Assert(monitor.Observe(Packet(3389, i * 16), LocalIp).Count == 0, "Expired RDP attempts carried forward");
        monitor.Enabled = false;
        Assert(monitor.Observe(Packet(13), LocalIp).Count == 0 && monitor.Observe(Dns("127.0.0.1"), LocalIp).Count == 0, "Disabled detector reported an event");
        monitor.Enabled = true;
        for (var i = 0; i < 300; i++) monitor.Observe(Packet(80, source: $"10.0.{i / 255}.{i % 255}"), LocalIp);
        Assert(monitor.TrackedSources <= 256, "Source tracking is unbounded");
        for (var i = 0; i < 1100; i++) monitor.Observe(Dns("1.1.1.1") with { DnsName = $"{i}.example.com" }, LocalIp);
        Assert(monitor.TrackedDomains <= 1024, "Domain tracking is unbounded");
        var bytes = new byte[40]; bytes[0] = 0x45; bytes[9] = 6;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 40);
        bytes[12] = 8; bytes[16] = 192; bytes[17] = 168; bytes[18] = 1; bytes[19] = 5;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(20), 51000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22), 3389); bytes[33] = 2;
        Assert(PacketCaptureService.TryParse(bytes, out var parsed) && parsed.TcpFlags == 2 && parsed.DestinationPort == 3389, "TCP flags/ports not parsed");
        bytes[7] = 1;
        Assert(PacketCaptureService.TryParse(bytes, out parsed) && parsed.TcpFlags == 0 && parsed.DestinationPort == 0, "Fragment payload interpreted as TCP header");
        bytes[3] = 50;
        Assert(!PacketCaptureService.TryParse(bytes, out _), "Truncated IP packet accepted");
        var queue = new BoundedPacketQueue(16);
        Parallel.For(0, 1000, _ => queue.TryEnqueue(Packet(443)));
        Assert(queue.Count == 16 && queue.DroppedCount == 984, "Concurrent queue capacity/loss accounting failed");
        var dequeued = 0;
        while (queue.TryDequeue(out _)) dequeued++;
        Assert(dequeued == 16 && queue.Count == 0 && queue.TryEnqueue(Packet(443)), "Queue does not recover after draining");
        Console.WriteLine("Network: normal DNS rotation, rebinding, scans, SYN bursts, RDP, expiry, cooldowns, bounds and packet parsing passed.");
    }
    private static void CheckEmail()
    {
        var normal = new EmailMessage("Person <person@example.com>", "", "me@example.com", "Meeting", "Meeting tomorrow at ten.");
        Assert(EmailSafetyService.Analyze(normal).Score == 0, "Benign message flagged");
        var phishing = normal with { ReplyTo = "steal@evil.test", Subject = "Urgent: account suspended",
            Body = "Reply with your password and verification code immediately: http://bank.test@192.168.2.1/login" };
        var risk = EmailSafetyService.Analyze(phishing);
        Assert(risk.Score >= 50 && risk.Findings.Any(reason => reason.Contains("before @")), "Credential phishing not detected");
        Assert(EmailSafetyService.Analyze(phishing, trustedSenders: "example.com").Score >= 50, "Whitelist bypassed content checks");
        Assert(EmailSafetyService.Analyze(normal, blockedSenders: "example.com").Score >= 70, "Sender blacklist ignored");
        Assert(!EmailSafetyService.Analyze(normal, "me@example.com").FilteredOut, "Matching recipient filtered out");
        Assert(EmailSafetyService.Analyze(normal, "other@example.com").FilteredOut, "Recipient filter ignored");
        Assert(EmailSafetyService.Analyze(normal, "bad-filter").FilteredOut, "Invalid watched address silently accepted");
        Assert(!EmailSafetyService.Analyze(normal, "Me <me@example.com>").FilteredOut, "Display-name recipient filter failed");
        Assert(EmailSafetyService.Analyze(normal with { Body = "Sürgős: küldd el a jelszavad és az ellenőrző kódodat azonnal." }).Score >= 35, "Hungarian credential request missed");
        var deceptive = EmailSafetyService.Analyze(normal with { Body = "<a href=\"https://evil.test\">https://bank.test</a>" });
        Assert(deceptive.Findings.Any(reason => reason.Contains("different domain than its label")), "Deceptive link not detected");
        Assert(EmailSafetyService.Analyze(normal with { Body = "https://xn--pple-43d.test/" }).Findings.Any(reason => reason.Contains("internationalized")), "IDN review missing");
        var imported = EmailSafetyService.ParseMessage("From: Person <person@example.com>\r\nTo: me@example.com\r\nSubject: Meeting\r\n continuation\r\n\r\nPlain body");
        Assert(imported.From.Contains("person@example.com") && imported.Subject == "Meeting continuation" && imported.Body == "Plain body", "Email header import failed");
        Assert(EmailSafetyService.Analyze(normal with { Body = new string('x', 128001) }).FilteredOut, "Size limit ignored");
        Console.WriteLine("Email: phishing, deceptive/IDN links, sender lists, whitelist safety, recipient filters, import and size limits passed.");
    }
    private static void CheckSettings()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "tools", "Protection.Checks", "test-protection.json");
        try
        {
            var store = new ProtectionSettingsStore(path);
            Assert(!store.Settings.ReportDnsRotation && store.Settings.MonitorAttacks && store.Settings.RainQuality == RainQuality.Eco && store.Settings.ThemeMode == ThemeMode.Dark, "Default protection/theme/graphics settings incorrect");
            var executable = Path.Combine(Directory.GetCurrentDirectory(), "test-app.exe");
            store.SetTrust(executable, ApplicationTrust.Trusted);
            Assert(new ProtectionSettingsStore(path).GetTrust(executable.ToUpperInvariant()) == ApplicationTrust.Trusted, "Whitelist path persistence/case matching failed");
            store.SetTrust(executable, ApplicationTrust.Blocked);
            Assert(new ProtectionSettingsStore(path).GetTrust(executable) == ApplicationTrust.Blocked, "Blacklist persistence failed");
            store.SetTrust(executable, ApplicationTrust.Monitor);
            Assert(new ProtectionSettingsStore(path).GetTrust(executable) == ApplicationTrust.Monitor, "Monitor policy restore failed");
        }
        finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
        Console.WriteLine("Settings: default preferences and exact-path whitelist/blacklist persistence passed (isolated test file).");
    }
    private static void CheckGraphics()
    {
        var rain = new MatrixRain();
        Layout(rain, 1120, 700);
        Assert(!rain.IsAnimating, "An unloaded effect started animation");
        Assert(rain.ColumnCount == 32 && rain.AnimationLayerCount == 3, "Eco budget incorrect");
        CheckRainImages(rain, 64);
        var palette = RainImages(rain).Select(image => image.ImageSource).Distinct().ToArray();
        var cacheBytes = rain.SpriteCacheBytes;
        Assert(cacheBytes == 442368, "Sprite cache size changed unexpectedly");
        var phase = rain.FirstDropPhase;
        Layout(rain, 3840, 2160);
        Assert(rain.ColumnCount == 32 && rain.AnimationLayerCount == 3 && rain.FirstDropPhase == phase,
            "Unloaded resize resets rain or increases workload");
        CheckRainImages(rain, 64);
        Assert(palette.SequenceEqual(RainImages(rain).Select(image => image.ImageSource).Distinct()) &&
            rain.SpriteCacheBytes == cacheBytes, "Maximization regenerates or enlarges the bitmap cache");

        var host = new Window { Content = rain, Width = 1120, Height = 700, Left = -20000, Top = -20000,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            host.Show(); PumpFor(250);
            if (SystemParameters.ClientAreaAnimation)
            {
                Assert(rain.IsAnimating && rain.MotionClock!.Children.Count == 3, "Native motion clocks did not start");
                Assert(rain.TargetFrameRate == 5 &&
                    System.Windows.Media.Animation.Timeline.GetDesiredFrameRate(rain.MotionClock!.Timeline) == 5,
                    "Inactive window frame guideline incorrect");
                phase = rain.FirstDropPhase; PumpFor(450);
                Assert(Math.Abs(rain.FirstDropPhase - phase) > .001, "Native animation does not advance");
                var clock = rain.MotionClock!;
                clock.Controller!.Pause(); PumpFor(220);
                clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(7.25), System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
                phase = rain.FirstDropPhase;
                Layout(rain, 3840, rain.ActualHeight);
                Assert(ReferenceEquals(clock, rain.MotionClock) && Math.Abs(rain.FirstDropPhase - phase) < 1e-8,
                    $"Width-only resize replaces clocks or loses phase: same clock={ReferenceEquals(clock, rain.MotionClock)}, {phase} -> {rain.FirstDropPhase}");
                Layout(rain, 3840, 2160);
                Assert(rain.MotionClock is not null && Math.Abs(rain.FirstDropPhase - phase) < 1e-8,
                    $"Maximization jumps the stream: {phase} -> {rain.FirstDropPhase}");
                CheckRainImages(rain, 64);
                Assert(rain.SpriteCacheBytes == cacheBytes, "Loaded maximization increases texture memory");
                Layout(rain, 1280, 720);
                clock = rain.MotionClock!;
                var timeline = (System.Windows.Media.Animation.ParallelTimeline)clock.Timeline;
                Assert(timeline.Children[0].Duration.TimeSpan.TotalSeconds == 22, "Resize changes the cycle speed");
                foreach (var rate in new[] { 0d, 12_000_000, 20_000_000, -1, double.NaN, double.PositiveInfinity })
                {
                    rain.SetTrafficRate(rate);
                    Assert(ReferenceEquals(clock, rain.MotionClock), "Traffic updates restart motion");
                    Assert(clock.Controller!.SpeedRatio >= .85 && clock.Controller.SpeedRatio <= 1.65,
                        "Traffic speed is invalid or unbounded");
                }
                rain.SetTrafficRate(0);
                CheckRainWrap(rain);
                // Exercise one hour of motion without accumulating drift or allocating new images.
                clock.Controller!.SeekAlignedToLastTick(TimeSpan.FromHours(1),
                    System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
                Assert(rain.FirstDropPhase is >= 0 and < 1, "Long-running phase is invalid");
                CheckRainImages(rain, 64);
                SaveImage(rain, "rain-preview.png", 1280, 720);
                rain.FlashAlert();
                Assert(rain.IsAlertActive, "Alert palette did not apply");
                SaveImage(rain, "rain-alert-preview.png", 1280, 720);
                PumpFor(1100);
                Assert(!rain.IsAlertActive, "One-shot alert did not expire");
                Console.WriteLine($"Rain: 3 native animated transforms (previously 36), {cacheBytes:N0} fixed cache bytes at 1120x700 and 3840x2160; live motion, resize phase, traffic speed, wrap and alert expiry passed.");
            }
            else Assert(!rain.IsAnimating, "Windows reduced-animation preference was ignored");

            rain.Quality = RainQuality.Balanced;
            Assert(rain.ColumnCount == 48 && rain.AnimationLayerCount == 4, "Balanced budget incorrect");
            CheckRainImages(rain, 96);
            Assert(rain.SpriteCacheBytes == cacheBytes, "Balanced creates a second sprite cache");
            if (SystemParameters.ClientAreaAnimation)
                Assert(rain.MotionClock!.Children.Count == 4, "Balanced clock budget incorrect");
            rain.Quality = RainQuality.Off;
            rain.FlashAlert();
            Assert(rain.ColumnCount == 0 && !rain.IsAnimating && !rain.IsAlertActive, "Off mode retained motion or alert");
            rain.Quality = RainQuality.Eco;
            host.WindowState = WindowState.Minimized;
            Assert(!rain.IsAnimating && rain.TargetFrameRate == 0, "Minimized rain retained clocks");
            host.WindowState = WindowState.Normal; host.Hide();
            Assert(!rain.IsAnimating, "Hidden rain retained clocks");
            rain.FlashAlert(); Assert(!rain.IsAlertActive, "Hidden effect starts an alert timer");
            host.Show(); PumpFor(220);
            if (SystemParameters.ClientAreaAnimation) Assert(rain.IsAnimating, "Visible rain did not resume");
            host.Content = null; PumpFor(50);
            Assert(!rain.IsAnimating, "Unloaded rain retained clocks");
            host.Content = rain; PumpFor(220);
            if (SystemParameters.ClientAreaAnimation)
                Assert(rain.MotionClock!.Children.Count == 3, "Reload created duplicate clocks");
        }
        finally { host.Close(); }
        Assert(!rain.IsAnimating, "Closed window retained animation clocks");
    }
    private static IEnumerable<ImageDrawing> RainImages(MatrixRain rain)
    {
        static IEnumerable<ImageDrawing> Images(Drawing drawing)
        {
            if (drawing is ImageDrawing image) yield return image;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var nested in Images(child)) yield return nested;
        }
        var scene = VisualTreeHelper.GetChild(rain, 0);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(scene); index++)
            foreach (var image in Images(((DrawingVisual)VisualTreeHelper.GetChild(scene, index)).Drawing))
                yield return image;
    }
    private static void CheckRainImages(MatrixRain rain, int count)
    {
        var images = RainImages(rain).ToArray();
        Assert(images.Length == count, "Retained tile drawing count incorrect");
        foreach (var image in images)
        {
            var bitmap = (BitmapSource)image.ImageSource;
            Assert(bitmap.IsFrozen && bitmap.PixelWidth == 18 && bitmap.PixelHeight is >= 144 and <= 240,
                "A rain texture is mutable or oversized");
            Assert(image.Rect.Size == new Size(18, bitmap.PixelHeight), "A rain texture is stretched");
        }
    }
    private static void CheckRainWrap(MatrixRain rain)
    {
        static byte[] Pixels(MatrixRain effect)
        {
            var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(effect);
            var pixels = new byte[1280 * 720 * 4]; bitmap.CopyPixels(pixels, 1280 * 4, 0); return pixels;
        }
        var clock = rain.MotionClock!;
        // Check the pixel output on both sides of each layer's loop boundary.
        foreach (var cycle in new[] { 22d, 17d, 13d })
        {
            clock.Controller!.SeekAlignedToLastTick(TimeSpan.FromSeconds(cycle - .00001),
                System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
            var before = Pixels(rain);
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(cycle + .00001),
                System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
            var after = Pixels(rain);
            var changed = 0;
            for (var index = 0; index < before.Length; index++)
                if (Math.Abs(before[index] - after[index]) > 12) changed++;
            Assert(changed < before.Length * .001, "Rain tile jumps visibly at a loop boundary");
            Assert(before.Any(value => value > 100), "Rendered rain is empty");
        }
    }
    private static void PumpFor(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private static void CheckDropdown()
    {
        using var source = File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), "Pulsatilla.Wpf", "Resources", "DarkControls.xaml"));
        var resources = (ResourceDictionary)XamlReader.Load(source);
        Application.Current.Resources.MergedDictionaries.Add(resources);
        var panel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(5, 8, 7)), Width = 360 };
        var combo = new ComboBox { Width = 200, Margin = new Thickness(12), ItemsSource = new[] { "All protocols", "TCP", "UDP", "ICMP" }, SelectedIndex = 2 };
        panel.Children.Add(combo);
        foreach (var text in new[] { "All protocols", "TCP", "UDP", "ICMP" })
            panel.Children.Add(new ComboBoxItem { Content = text, IsSelected = text == "UDP", Margin = new Thickness(12, 1, 12, 1) });
        Layout(panel, 360, 250); combo.ApplyTemplate();
        Assert(combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup, "Dropdown popup template missing");
        foreach (var foreground in new[] { "#F0F5F1", "#FFFFFF" })
            foreach (var background in new[] { "#0C1810", "#254C35", "#305C42" })
                Assert(Contrast(foreground, background) >= 4.5, "Dropdown colors have insufficient contrast");
        SaveImage(panel, "dropdown-preview.png", 360, 250);
        Console.WriteLine("Dropdown palette: normal and selected item contrast >=4.5:1 passed.");
    }
    private static double Contrast(string a, string b)
    {
        static double L(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            static double Channel(byte value) { var s = value / 255d; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var x = L(a); var y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    private static void CheckFullLayout()
    {
        // Load the actual layout without its code-behind handlers, monitoring timers or system operations.
        var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Pulsatilla.Wpf", "MainWindow.xaml"));
        xaml = Regex.Replace(xaml, @"x:Class=""[^""]+""", "");
        xaml = xaml.Replace("clr-namespace:Pulsatilla.Wpf", "clr-namespace:Pulsatilla.Wpf;assembly=Protection.Checks");
        xaml = Regex.Replace(xaml, @"\s(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|TreeViewItem\.Expanded)=""[^""]+""", "");
        var window = (Window)XamlReader.Parse(xaml);
        window.WindowStyle = WindowStyle.None; window.ShowInTaskbar = false; window.ShowActivated = false;
        window.Left = -20000; window.Top = -20000; window.Width = 1120; window.Height = 700;
        LocalizationService.SetLanguage("en", persist: false); LocalizationService.Apply(window);
        var tabs = (TabControl)window.FindName("MainTabs");
        tabs.SelectedItem = window.FindName("ProtectionTab");
        var protectionTabs = (TabControl)window.FindName("ProtectionTabs");
        ((DataGrid)window.FindName("ApplicationPoliciesGrid")).ItemsSource = new[] { new ApplicationPolicy(@"C:\Apps\Example.exe", ApplicationTrust.Trusted) };
        ((CheckBox)window.FindName("AttackMonitoringBox")).IsChecked = true;
        var qualityBox = (ComboBox)window.FindName("RainQualityBox"); qualityBox.ItemsSource = Enum.GetValues<RainQuality>(); qualityBox.SelectedItem = RainQuality.Eco;
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        CheckLiveProtocolDropdown(window);
        tabs.SelectedItem = window.FindName("ProtectionTab");
        window.Measure(new Size(1120, 700)); window.Arrange(new Rect(0, 0, 1120, 700)); window.UpdateLayout();
        for (var index = 0; index < 4; index++)
        {
            protectionTabs.SelectedIndex = index; window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            Layout(content, 1120, 660);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveImage(content, $"protection-{index}-preview.png", 1120, 660);
        }
        var background = (MatrixRain)window.FindName("MatrixBackground");
        window.WindowState = WindowState.Minimized;
        Assert(!background.IsAnimating, "Minimized window still animates");
        window.WindowState = WindowState.Normal; window.Hide();
        Assert(!background.IsAnimating, "Hidden window still animates");
        window.Close();
        Console.WriteLine("Actual Protection layout loaded and rendered at minimum window width without running system operations.");
    }
    private static void CheckLiveProtocolDropdown(Window window)
    {
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 1;
        window.UpdateLayout();
        var combo = (ComboBox)window.FindName("LiveTrafficProtocolBox");
        combo.ApplyTemplate();
        Assert(ReferenceEquals(combo.Style, Application.Current.Resources["ReadableComboBox"]), "Live traffic is not using its explicit readable style");
        Assert(ReferenceEquals(combo.ItemContainerStyle, Application.Current.Resources["ReadableComboBoxItem"]), "Live protocol item style is not explicit");
        var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
        var pane = (FrameworkElement)popup.Child;
        // Render the real template's popup child offscreen; do not open a popup on the user's desktop.
        popup.Child = null;
        var preview = new StackPanel { Width = 240, Background = new SolidColorBrush(Color.FromRgb(5, 8, 7)) };
        preview.Children.Add(pane);
        try
        {
            Layout(preview, 240, 190);
            foreach (var selected in new[] { 1, 2, 3 })
            {
                combo.SelectedIndex = selected; preview.UpdateLayout();
                foreach (ComboBoxItem item in combo.Items)
                {
                    item.ApplyTemplate();
                    Assert(item.Template is not null, "Actual protocol item template did not load");
                    var border = (Border)item.Template!.FindName("ItemBorder", item);
                    var foreground = ((SolidColorBrush)item.Foreground).Color.ToString();
                    var background = ((SolidColorBrush)border.Background).Color.ToString();
                    Assert(Contrast(foreground, background) >= 7, "Actual protocol item is unreadable: " + item.Content);
                }
            }
            combo.SelectedIndex = 2; preview.UpdateLayout();
            SaveImage(preview, "live-dropdown-preview.png", 240, 190);
        }
        finally { preview.Children.Clear(); popup.Child = pane; }
        Console.WriteLine("Actual Live traffic dropdown: normal and selected TCP/UDP/ICMP contrast >=7:1 passed.");
    }
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
    }
    internal static void SaveImage(Visual visual, string name, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Directory.GetCurrentDirectory(), "tools", "Protection.Checks", name)); encoder.Save(stream);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
