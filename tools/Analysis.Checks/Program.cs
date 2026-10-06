using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pulsatilla.Wpf;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static void Main()
    {
        CheckPatterns(); CheckMime(); CheckApplications(); CheckFiles().GetAwaiter().GetResult(); CheckUi();
        Console.WriteLine($"Analysis checks PASS: {_checks}. Synthetic local samples only; no Defender scans, uploads, browser or firewall operations.");
    }
    private static void CheckPatterns()
    {
        var source = new byte[] { 0xAA, 0, 0xCC }; var mask = new byte[] { 255, 0, 255 };
        var rule = new HexPatternRule("masked", "Masked fixture", "Test", source, mask, context: HexPatternContext.Packet);
        source[0] = 0; mask[0] = 0;
        var engine = new HexPatternEngine([rule]); var sample = new byte[] { 0, 0xAA, 0x22, 0xCC, 0xAA, 0x33, 0xCC };
        var result = engine.Analyze(sample, HexPatternContext.Packet);
        Assert(result.Matches.Select(m => m.Offset).SequenceEqual(new[] { 1, 4 }), "Wildcard or immutable rule failed");
        Assert(engine.Analyze(sample, HexPatternContext.File).Matches.Count == 0, "Context was ignored");
        var offset = new HexPatternEngine([new("fixed", "Fixed", "Test", [0xAA], offset: 4)]);
        Assert(offset.Analyze(sample).Matches.Single().Offset == 4, "Fixed offset ignored");
        Throws<ArgumentException>(() => new HexPatternRule("empty", "", "", []), "Empty pattern accepted");
        Throws<ArgumentException>(() => new HexPatternRule("wild", "", "", [0], [0]), "All wildcard accepted");
        Throws<ArgumentException>(() => new HexPatternEngine([rule, rule]), "Duplicate ID accepted");
        var flood = new HexPatternEngine([new("flood", "", "", [0])]).Analyze(new byte[100_000]);
        Assert(flood.BytesInspected == 65_536 && flood.Truncated && flood.Matches.Count == 256, "Sample/match limits failed");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Throws<OperationCanceledException>(() => engine.Analyze(sample, cancellationToken: cancel.Token), "Cancellation ignored");
        Assert(HexPatternEngine.Default.Analyze(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK"), HexPatternContext.Packet).Matches.Any(m => m.Category == "Protocol signature"), "Protocol signature absent");
        Assert(HexPatternEngine.Default.Analyze([0x4D, 0x5A], HexPatternContext.EmailAttachment).Matches.Single().Category == "File signature", "Executable format marker absent");
    }
    private static void CheckMime()
    {
        var raw = "From: sender@example.test\r\nReturn-Path: <envelope@mailer.test>\r\nReceived: fixture local header\r\nAuthentication-Results: fixture; dkim=pass\r\nContent-Type: multipart/mixed; boundary=b\r\n\r\n--b\r\nContent-Type: text/plain\r\n\r\nhello\r\n--b\r\nContent-Type: application/octet-stream; name=invoice.pdf.exe\r\nContent-Disposition: attachment; filename=invoice.pdf.exe\r\nContent-Transfer-Encoding: base64\r\n\r\nTVpBQkM=\r\n--b--\r\n";
        var message = EmailSafetyService.ParseMessage(Encoding.ASCII.GetBytes(raw));
        Assert(message.Body.Contains("hello") && !message.Body.Contains("TVp"), "Attachment leaked into message body");
        Assert(message.ReturnPath.Contains("mailer.test") && message.Received.Contains("fixture") && message.AuthenticationResults.Contains("dkim=pass"), "Metadata lost");
        var attachment = message.Attachments.Single();
        Assert(attachment.FileName == "invoice.pdf.exe" && attachment.Size == 5 && attachment.Sha256 == Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("MZABC"))), "Attachment decoding/hash failed");
        Assert(attachment.Analysis.Matches.Any(m => m.Id == "pe"), "Attachment signature absent");
        var risk = EmailSafetyService.Analyze(message);
        Assert(risk.Findings.Any(f => f.Contains("extension followed")) && risk.Findings.Any(f => f.Contains("not independently verified")), "Advisory attachment/authentication findings absent");
        var invalid = EmailSafetyService.ParseMessage(raw.Replace("TVpBQkM=", "@@@"));
        Assert(invalid.Attachments.Count == 0 && invalid.ImportWarnings.Any(w => w.Contains("unsupported")), "Invalid transfer silently accepted");
        Throws<ArgumentException>(() => new EmailAttachment("oversize", "application/octet-stream", new byte[128001]), "Attachment bound ignored");
        var entropy = new EmailAttachment("test.txt", "text/plain", Encoding.ASCII.GetBytes("https://example.test/path"));
        Assert(entropy.EmbeddedUrls.Single() == "https://example.test/path" && entropy.Entropy >= 0 && entropy.Entropy <= 8, "Local URL/entropy extraction failed");
        var preview = attachment.CopyPreview(); preview[0] = 0;
        Assert(attachment.CopyPreview()[0] == 0x4D, "Attachment preview mutated original bytes");
    }
    private static async Task CheckFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulsatillaAnalysisFixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "sample.bin"); await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("synthetic fixture"));
            var inspector = new LocalFileInspector(); var result = await inspector.InspectAsync(path);
            Assert(result.Sha256 == Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("synthetic fixture"))) && result.Size == 17, "Streaming hash failed");
            result.Preview[0] = 0;
            Assert((await inspector.InspectAsync(path)).Preview[0] == 's', "Cache sample mutable through result");
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("changed synthetic fixture"));
            Assert((await inspector.InspectAsync(path)).Sha256 != result.Sha256, "File identity cache not refreshed");
            Assert(result.Reasons.Count > 0 && result.Signature.Details.Contains("Catalog signatures"), "Signature scope unexplained");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await inspector.InspectAsync(Path.Combine(dir, "missing.bin")); throw new Exception("Missing file accepted"); } catch (IOException) { _checks++; }
            try { await inspector.InspectAsync(path, cancelled.Token); throw new Exception("Cancelled cached inspection returned data"); } catch (OperationCanceledException) { _checks++; }
            var provider = new MicrosoftDefenderProvider();
            var notRequested = await provider.InspectAsync(path, false);
            Assert(notRequested.State == "Not requested", "Defender consent gate failed");
            Throws<ArgumentException>(() => LocalFileInspector.ValidateLocalFile(@"\\server\share\file.exe"), "Remote path accepted");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
    private static void CheckApplications()
    {
        var flow1 = new LiveTrafficRow("Fixture", @"C:\fixture\sample.exe", "192.0.2.1", "TCP") { ProcessId = 123, RemoteAddress = "192.0.2.1", FirstSeenUtc = DateTime.UtcNow.AddMinutes(-2) };
        var flow2 = new LiveTrafficRow("Fixture", @"C:\fixture\sample.exe", "192.0.2.2", "UDP") { ProcessId = 123, RemoteAddress = "192.0.2.2", FirstSeenUtc = DateTime.UtcNow.AddMinutes(-1) };
        flow1.AddPacket(20, false); flow2.AddPacket(40, true);
        var summary = ApplicationTrafficPanel.Aggregate([flow1, flow2]).Single();
        Assert(summary.ObservedFlows == 2 && summary.DownloadBytes == 20 && summary.UploadBytes == 40 && summary.RemoteHosts.Contains("192.0.2.2"), "Captured application aggregate failed");
        Assert(summary.Signature == "Not inspected" && summary.Sha256 == "Not calculated" && summary.Risk == "Unknown", "Application trust invented without inspection");
        var flood = Enumerable.Range(0, 1000).Select(i => new LiveTrafficRow("Fixture" + i, @"C:\fixture\sample" + i + ".exe", "192.0.2.1", "TCP") { ProcessId = i + 1 });
        Assert(ApplicationTrafficPanel.Aggregate(flood).Count == 256, "Application groups unbounded");
        var identity = new ProcessIdentity(123, "Fixture", @"C:\fixture\sample.exe");
        var tcp = new OwnedSocketSnapshot(identity, "TCP", "192.0.2.10", 4000, "192.0.2.1", 443, "Established", true);
        var listen = new OwnedSocketSnapshot(identity, "TCP", "0.0.0.0", 8080, "0.0.0.0", 0, "Listen", false);
        var udp = new OwnedSocketSnapshot(identity, "UDP", "0.0.0.0", 5353, "", 0, "Bound", false);
        var native = ApplicationTrafficPanel.Aggregate([], [tcp, tcp, listen, udp, udp]).Single();
        Assert(native.EstablishedTcp == 1 && native.BoundUdpEndpoints == 1 && native.ObservedFlows == 0 && native.DownloadBytes == 0 && native.UploadBytes == 0, "Native endpoints miscounted as captures or duplicates");
        Assert(native.RemoteHosts == "192.0.2.1" && native.Countries == "Unknown" && native.Policy == "Not queried" && native.Signature == "Not inspected", "Native-only metadata fabricated");
        var combined = ApplicationTrafficPanel.Aggregate([flow1, flow1, flow2], [tcp, listen, udp]).Single();
        Assert(combined.EstablishedTcp == 1 && combined.BoundUdpEndpoints == 1 && combined.ObservedFlows == 2 && combined.DownloadBytes == 20 && combined.UploadBytes == 40, "Native counts replaced/doubled retained captured bytes");
        var inconsistentUdp = udp with { IsActiveConnection = true, Status = "Established" };
        var unknownTcp = tcp with { Status = "Unknown" };
        Assert(ApplicationTrafficPanel.Aggregate([], [inconsistentUdp, unknownTcp]).Single().EstablishedTcp == 0, "UDP or unestablished TCP classified as active connection");
        var capSockets = Enumerable.Range(0, 9000).Select(i => tcp with { LocalPort = i + 1 }).ToArray();
        Assert(ApplicationTrafficPanel.Aggregate([], capSockets).Single().EstablishedTcp == 8192, "Native snapshot input cap ignored");
        var prioritized = ApplicationTrafficPanel.Aggregate(flood, [tcp]);
        Assert(prioritized.Count == 256 && prioritized.Any(a => a.Key == native.Key && a.EstablishedTcp == 1), "Current native application lost behind capture group cap");
    }
    private static void CheckUi()
    {
        var application = new Application();
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        CheckLateInspectionResults();
        foreach (var light in new[] { false, true })
        {
            application.Resources["Theme_E6F2E8"] = light ? Brushes.Black : Brushes.White;
            application.Resources["Theme_0C1810"] = light ? Brushes.White : Brushes.Black;
            application.Resources["Theme_305C42"] = light ? Brushes.LightGreen : Brushes.DarkGreen;
            var panel = new FileInspectionPanel(); panel.SetSample(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\nfixture"), "Synthetic packet", HexPatternContext.Packet);
            panel.Measure(new Size(800, 500)); panel.Arrange(new Rect(0, 0, 800, 500)); panel.UpdateLayout();
            var bitmap = new RenderTargetBitmap(800, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
            SaveImage(bitmap, "sample-" + (light ? "light" : "dark"));
            Assert(panel.ActualHeight == 500 && bitmap.PixelWidth == 800, "Reusable sample panel layout failed");
            var attachments = new EmailAttachmentsPanel(); attachments.SetMessage(new EmailMessage("", "", "", "", "") { Attachments = [new("fixture.bin", "application/octet-stream", [0x4D, 0x5A])] });
            attachments.Measure(new Size(800, 500)); attachments.Arrange(new Rect(0, 0, 800, 500)); attachments.UpdateLayout(); bitmap.Render(attachments);
            var attachmentImage = new RenderTargetBitmap(800, 500, 96, 96, PixelFormats.Pbgra32); attachmentImage.Render(attachments); SaveImage(attachmentImage, "attachments-" + (light ? "light" : "dark"));
            Assert(attachments.ActualHeight == 500, "Attachment panel layout failed");
            var apps = new ApplicationTrafficPanel(); var flow = new LiveTrafficRow("Fixture", @"C:\fixture\app.exe", "192.0.2.1", "TCP");
            apps.Refresh([flow]); var retained = apps.Applications.Single(); flow.AddPacket(55, false); apps.Refresh([flow]);
            Assert(ReferenceEquals(retained, apps.Applications.Single()) && retained.DownloadBytes == 55, "Application refresh rebuilt existing row or lost bytes");
            apps.Measure(new Size(800, 500)); apps.Arrange(new Rect(0, 0, 800, 500)); apps.UpdateLayout(); bitmap.Render(apps);
            var appImage = new RenderTargetBitmap(800, 500, 96, 96, PixelFormats.Pbgra32); appImage.Render(apps); SaveImage(appImage, "applications-" + (light ? "light" : "dark"));
            Assert(apps.ActualHeight == 500, "Application panel render failed");
        }
        application.Shutdown();
    }
    private static void CheckLateInspectionResults()
    {
        static FileInspection Fixture(string path) => new(path, 2, DateTime.UtcNow, new string('A', 64), new(LocalSignatureStatus.Unknown, "", "Synthetic"), "Unknown", ["Fixture"], [0x4D, 0x5A], HexPatternEngine.Default.Analyze([0x4D, 0x5A], HexPatternContext.File));
        var requests = new List<TaskCompletionSource<FileInspection>>();
        var panel = new FileInspectionPanel((_, _) => { var request = new TaskCompletionSource<FileInspection>(); requests.Add(request); return request.Task; });
        var first = panel.InspectPathAsync(@"C:\fixture\first.exe");
        panel.SetSample([0x50, 0x4B], "Latest sample", HexPatternContext.Scanner);
        requests[0].SetResult(Fixture(@"C:\fixture\first.exe")); Pump(first);
        Assert(panel.CurrentDetails.Contains("Latest sample") && !panel.CurrentDetails.Contains("first.exe"), "Late inspection overwrote newer sample");
        var old = panel.InspectPathAsync(@"C:\fixture\old.exe"); var current = panel.InspectPathAsync(@"C:\fixture\current.exe");
        requests[2].SetResult(Fixture(@"C:\fixture\current.exe")); Pump(current);
        requests[1].SetResult(Fixture(@"C:\fixture\old.exe")); Pump(old);
        Assert(panel.CurrentDetails.Contains("current.exe") && !panel.CurrentDetails.Contains("old.exe"), "Old inspection generation overwrote current result");
        var closing = panel.InspectPathAsync(@"C:\fixture\closing.exe");
        panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        requests[3].SetResult(Fixture(@"C:\fixture\closing.exe")); Pump(closing);
        Assert(!panel.CurrentDetails.Contains("closing.exe"), "Unloaded panel accepted late result");
    }
    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => frame.Continue = false; timer.Start();
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
            if (!task.IsCompleted) throw new TimeoutException("Synthetic UI continuation did not finish.");
        }
        task.GetAwaiter().GetResult();
    }
    private static void SaveImage(BitmapSource bitmap, string name)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "dist", "analysis-ui"); Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
    }
    private static void Assert(bool passed, string reason) { if (!passed) throw new InvalidOperationException(reason); _checks++; }
    private static void Throws<T>(Action action, string reason) where T : Exception { try { action(); } catch (T) { _checks++; return; } throw new InvalidOperationException(reason); }
}
