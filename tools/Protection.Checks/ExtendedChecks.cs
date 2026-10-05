using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Pulsatilla.Wpf;

internal static class ExtendedChecks
{
    private static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    internal static void CheckServices()
    {
        Assert(!SecurityEventPolicy.IsAlarm("DNS REVIEW") && !SecurityEventPolicy.IsAlarm("DNS"), "Routine DNS entered security alerts");
        Assert(SecurityEventPolicy.IsAlarm("DNS ANOMALY") && SecurityEventPolicy.IsAlarm("ATTACK REVIEW") && SecurityEventPolicy.IsAlarm("BLOCKLIST REVIEW"), "Attack alarms were silenced");
        var start = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        PacketObservation Dns(string[] addresses, int seconds) => new("8.8.8.8", "192.168.1.5", "UDP", 80, 53, 40000, "", "example.com", addresses) { CapturedAtUtc = start.AddSeconds(seconds) };
        var monitor = new NetworkThreatMonitor();
        monitor.Observe(Dns(["1.1.1.1", "10.0.0.1"], 0), "192.168.1.5");
        Assert(monitor.Observe(Dns(["1.1.1.1", "10.0.0.1"], 1), "192.168.1.5").Count == 0, "Unchanged mixed DNS answer produced rebinding noise");
        monitor.Reset(); monitor.Observe(Dns(["1.1.1.1"], 5), "192.168.1.5");
        Assert(monitor.Observe(Dns(["10.0.0.1"], 4), "192.168.1.5").Count == 0, "Older DNS packet caused a false transition");
        Assert(monitor.Observe(Dns(["10.0.0.1"], 6), "192.168.1.5").Any(), "Real rebinding review was lost after an old packet");
        var body = "Sürgős: küldd el a jelszavad és az ellenőrző kódodat azonnal. http://evil.test/login";
        var encoded = "From: Person <person@example.com>\r\nTo: me@example.com\r\nSubject: =?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Sürgős")) + "?=\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        var mail = EmailSafetyService.ParseMessage(Encoding.ASCII.GetBytes(encoded));
        Assert(mail.Body == body && mail.Subject == "Sürgős" && EmailSafetyService.Analyze(mail).Score >= 50, "Encoded phishing message missed");
        var multipart = "From: person@example.com\nTo: me@example.com\nContent-Type: multipart/mixed; boundary=sample\n\n--sample\nContent-Type: text/html; charset=utf-8\nContent-Transfer-Encoding: quoted-printable\n\nPlease send your pass=\nword: <a href=3D\"https://evil.test\"><b>https://bank.test</b></a>\n--sample\nContent-Type: application/octet-stream; name=invoice.pdf.exe\nContent-Disposition: attachment; filename=invoice.pdf.exe\nContent-Transfer-Encoding: base64\n\nAAAA\n--sample--\n";
        mail = EmailSafetyService.ParseMessage(multipart);
        var result = EmailSafetyService.Analyze(mail);
        Assert(mail.Body.Contains("password") && mail.AttachmentNames.Contains("invoice.pdf.exe") && !mail.Body.Contains("AAAA") &&
            result.Findings.Any(value => value.Contains("different domain")) && result.Findings.Any(value => value.Contains("Executable")), "Multipart phishing or attachment-name review failed");
        var broken = EmailSafetyService.ParseMessage("From: person@example.com\nContent-Transfer-Encoding: base64\n\nINVALID%%%");
        Assert(broken.ImportWarnings.Count > 0 && EmailSafetyService.Analyze(broken).Score >= 20, "Invalid encoding reported low risk without warning");
        var deceptive = new EmailMessage("person@example.com", "", "me@example.com", "", "Please send your p<b>ass</b>word. <a href=\"//evil.test\"><b>https://bank.test</b></a>");
        Assert(EmailSafetyService.Analyze(deceptive).Score >= 65, "HTML-obfuscated secret request missed");
        Assert(EmailSafetyService.Analyze(deceptive with { Body = "Approve this sign-in notification immediately." }).Findings.Any(value => value.Contains("sign-in notification")), "MFA approval lure missing");
        var idn = new EmailMessage("person@bücher.test", "", "me@example.com", "", "Meeting tomorrow.");
        Assert(EmailSafetyService.Analyze(idn, blockedSenders: "bücher.test").Score >= 70, "Unicode sender-domain rule mismatch");
        var csv = "email,service,date,password\nme@example.com,\"Example, Inc\",2024-01-01,DO-NOT-EXPOSE\nother@example.com,Other,2025,SECRET\nME@example.com,\"Example, Inc\",2024-01-01,SECRET\n";
        var review = LocalExposureService.Review(csv, ".csv", "me@example.com");
        Assert(review.RowsReviewed == 3 && review.Matches.Count == 1 && review.Matches[0].Service == "Example, Inc" && !review.ToString()!.Contains("SECRET"), "Exposure filters, CSV quoting, deduplication or secret exclusion failed");
        review = LocalExposureService.Review("[{\"email\":\"me@example.com\",\"service\":\"Example\",\"date\":\"2024\",\"password\":\"SECRET\"}]", ".json", "me@example.com");
        Assert(review.Matches.Count == 1, "JSON exposure report failed");
        try { LocalExposureService.Review(csv, ".csv", ""); throw new InvalidOperationException("Empty watch list accepted"); } catch (ArgumentException) { }
        var tracker = new TrafficSourceTracker();
        for (var index = 0; index < 10; index++) tracker.Observe(new("8.8.8.8", "192.168.1.5", "TCP", 100, 443, 40000, "", null, []), "192.168.1.5", "Browser", @"C:\Apps\browser.exe");
        tracker.Observe(new("192.168.1.5", "1.1.1.1", "UDP", 3000, 40001, 443, "", null, []), "192.168.1.5", "Uploader", @"C:\Apps\uploader.exe");
        Assert(tracker.TopIps()[0].Name == "8.8.8.8" && tracker.TopIps()[0].Packets == 10, "IP ranking no longer uses source packet count");
        Assert(tracker.TopApplications()[0].Name == "Uploader" && tracker.TopApplications()[0].UploadBytes == 3000 && tracker.TopApplications()[1].DownloadBytes == 1000, "Software ranking or direction totals incorrect");
        for (var index = 0; index < 1100; index++) tracker.Observe(new($"10.0.{index / 255}.{index % 255}", "192.168.1.5", "TCP", 10, 443, 40000, "", null, []), "192.168.1.5", "App" + index, @"C:\Apps\" + index + ".exe");
        Assert(tracker.IpCount <= 1024 && tracker.ApplicationCount <= 256, "Source aggregation is unbounded");
        Console.WriteLine("Extended protection: DNS noise suppression, MIME decoding, hidden phishing, MFA lures, IDN rules, offline exposure filters and bounded IP/software attribution passed.");
    }

    internal static void CheckVisuals()
    {
        var chart = new VisualTrafficChart { IsFrozen = true };
        var start = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 2000; index++) chart.AddSample(1000, 500, start.AddMilliseconds(index * 500));
        Assert(chart.SampleCount == 1800 && chart.VisiblePoints.Count == 0, "Frozen chart did not continue bounded collection");
        chart.IsFrozen = false;
        Assert(chart.VisiblePoints.Count == 121 && chart.Summary.Contains("58.59 KiB"), "One-minute chart range or byte integration incorrect");
        chart.WindowSeconds = 900; Assert(chart.VisiblePoints.Count == 1800, "15-minute history is incomplete");
        chart.AddSample(double.NaN, double.PositiveInfinity, start.AddSeconds(1000));
        Assert(double.IsFinite(chart.DisplayScale) && chart.VisiblePoints[^1].Download == 0 && chart.VisiblePoints[^1].Upload == 0, "Invalid samples poisoned the plot");
        var profile = AboutContent.Creator;
        Assert(profile.Name == "Gábor Kocsis" && profile.Github == "https://github.com/acsokis" && profile.Facebook == "https://www.facebook.com/gabor.carter" &&
            profile.Instagram == "https://www.instagram.com/gabor_carter/" && profile.Linkedin == "https://www.linkedin.com/in/gabor-web/", "Creator credit incorrect");
        var document = AboutContent.CreateReadmeDocument();
        Assert(new TextRange(document.ContentStart, document.ContentEnd).Text.Length > 1000, "Embedded README is empty");
        Assert(AboutContent.LicenseText.Contains("MIT License") && AboutContent.LicenseText.Contains("Gábor Kocsis"), "Embedded license/credit missing");
        Assert(!AboutContent.Readme.Contains("GlassWire") && AboutContent.Services.Contains("not available subscriptions") && AboutContent.Privacy.Contains("no telemetry"), "Public wording or service/privacy disclosure incorrect");
        // Load the actual layout with test data, with all operation handlers removed.
        var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Pulsatilla.Wpf", "MainWindow.xaml"));
        xaml = Regex.Replace(xaml, @"x:Class=""[^""]+""", "").Replace("clr-namespace:Pulsatilla.Wpf", "clr-namespace:Pulsatilla.Wpf;assembly=Protection.Checks");
        xaml = Regex.Replace(xaml, @"\s(?:Click|Checked|Unchecked|TextChanged|SelectionChanged|TreeViewItem\.Expanded)=""[^""]+""", "");
        var window = (Window)XamlReader.Parse(xaml);
        window.WindowStyle = WindowStyle.None; window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = -20000; window.Top = -20000; window.Width = 1500; window.Height = 900;
        var tabs = (TabControl)window.FindName("MainTabs");
        Assert((string)((Button)window.FindName("SupportButton")).Tag == ProductInfo.SupportUrl &&
            ProductInfo.SupportUrl == "https://paypal.me/gaborcarter", "Voluntary support link is incorrect");
        ((TextBlock)window.FindName("CreatorNameText")).Text = profile.Name;
        ((TextBlock)window.FindName("CreatorLocationText")).Text = profile.Location;
        ((FlowDocumentScrollViewer)window.FindName("ReadmeViewer")).Document = AboutContent.CreateReadmeDocument();
        ((FlowDocumentScrollViewer)window.FindName("LicenseViewer")).Document = AboutContent.CreateDocument("# MIT License\n\n" + AboutContent.LicenseText);
        ((FlowDocumentScrollViewer)window.FindName("ServicesViewer")).Document = AboutContent.CreateDocument(AboutContent.Services);
        ((FlowDocumentScrollViewer)window.FindName("PrivacyViewer")).Document = AboutContent.CreateDocument(AboutContent.Privacy);
        ((TextBlock)window.FindName("AboutVersionText")).Text = "Pulsatilla 1.0.0 · Community · MIT";
        var actualChart = (VisualTrafficChart)window.FindName("ThroughputChart");
        var tracker = new TrafficSourceTracker();
        for (var index = 0; index < 120; index++)
        {
            var received = 300_000 + 2_500_000 * Math.Pow(Math.Max(0, Math.Sin(index * .11)), 5);
            var sent = 80_000 + 800_000 * Math.Pow(Math.Max(0, Math.Cos(index * .15)), 5);
            actualChart.AddSample(received, sent, start.AddMilliseconds(index * 500));
            tracker.Observe(new("192.168.1.20", "192.168.1.5", "TCP", 1400, 443, 40000, "", null, []), "192.168.1.5", "Browser", @"C:\Apps\browser.exe");
        }
        ((TextBlock)window.FindName("ThroughputSummaryText")).Text = actualChart.Summary;
        ((TextBlock)window.FindName("SelectedAdapterLabel")).Text = "Ethernet · live adapter throughput";
        ((TextBlock)window.FindName("DownloadRate")).Text = "2.7 MiB/s"; ((TextBlock)window.FindName("UploadRate")).Text = "860 KiB/s";
        ((TextBlock)window.FindName("AdapterStatus")).Text = "Up";
        var nodes = tracker.TopIps().Select(row => { var node = new TrafficSourceNode(row.Key) { IsExpanded = true }; node.Update(row, 120, "Office device (DNS label)"); return node; }).ToArray();
        ((TreeView)window.FindName("TopIpSourcesTree")).ItemsSource = nodes;
        ((TreeView)window.FindName("TopSoftwareSourcesTree")).ItemsSource = tracker.TopApplications().Select(row => { var node = new TrafficSourceNode(row.Key); node.Update(row, 120); return node; }).ToArray();
        try
        {
            window.Show();
            foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
            {
                ThemeService.Apply(mode); window.UpdateLayout();
                Assert(((SolidColorBrush)window.Background).Color == ThemeService.Color("050807"), "Window theme did not update dynamically: " + mode);
                foreach (var (index, name) in new[] { (0, "dashboard"), (6, "sources"), (9, "about") })
                {
                    tabs.SelectedIndex = index; window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Program.SaveImage(window, $"{name}-{mode.ToString().ToLowerInvariant()}-preview.png", 1500, 900);
                }
                tabs.SelectedIndex = 9;
                var aboutTabs = (TabControl)window.FindName("AboutDocumentsTabs");
                for (var documentIndex = 1; documentIndex < 4; documentIndex++)
                {
                    aboutTabs.SelectedIndex = documentIndex; window.UpdateLayout();
                    Program.SaveImage(window, $"about-{documentIndex}-{mode.ToString().ToLowerInvariant()}-preview.png", 1500, 900);
                }
                aboutTabs.SelectedIndex = 0;
                var combo = (ComboBox)window.FindName("LiveTrafficProtocolBox"); tabs.SelectedIndex = 1; window.UpdateLayout(); combo.ApplyTemplate();
                foreach (ComboBoxItem item in combo.Items)
                {
                    item.ApplyTemplate();
                    var foreground = ((SolidColorBrush)item.Foreground).Color;
                    Assert(mode == ThemeMode.Dark ? foreground.R > 200 : foreground.R < 100, "Protocol dropdown foreground did not follow theme");
                }
            }
        }
        finally { window.Close(); ThemeService.Apply(ThemeMode.Dark); }
        Console.WriteLine("Presentation: bounded/frozen timeline, ranges/totals, embedded creator/README, full Dashboard/Sources/About rendering and dynamic dark/light controls passed.");
    }
}
