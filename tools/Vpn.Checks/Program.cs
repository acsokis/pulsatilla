using System.Diagnostics;
using System.Text;
using Pulsatilla.Wpf;

if (args.Contains("--child-output"))
{
    for (var i = 0; i < 2000; i++) { Console.WriteLine(new string('o', 100)); Console.Error.WriteLine(new string('e', 100)); }
    return;
}
if (args.Contains("--child-wait")) { await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString()); await Task.Delay(TimeSpan.FromMinutes(2)); return; }
if (args.Contains("--echo")) { Console.Write(args[1]); return; }
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
const string profile = "client\ndev tun\nproto udp\nremote vpn.example.com 1194\nremote-cert-tls server\n<ca>\n-----BEGIN CERTIFICATE-----\nYWJj\n-----END CERTIFICATE-----\n</ca>\n<cert>\n-----BEGIN CERTIFICATE-----\nYWJj\n-----END CERTIFICATE-----\n</cert>\n<key>\n-----BEGIN PRIVATE KEY-----\nYWJj\n-----END PRIVATE KEY-----\n</key>\n";
var validated = OpenVpnProfileValidator.Validate(profile);
Check(validated.Accepted && !validated.RequiresPassword, "narrow inline certificate profile parses; not certificate validity proof");
foreach (var directive in new[] { "up script.exe", "down script.exe", "plugin module.dll", "config other.ovpn", "script-security 3", "auth-user-pass secret.txt", "log output.txt", "management 0.0.0.0 5555", "setenv unsafe value", "ca ../../file", "redirect-gateway def1" })
    Check(!OpenVpnProfileValidator.Validate(profile + directive + "\n").Accepted, "reject unreviewed directive " + directive.Split(' ')[0]);
Check(!OpenVpnProfileValidator.Validate(profile.Replace("remote-cert-tls server\n", "")).Accepted, "server certificate purpose check required");
Check(!OpenVpnProfileValidator.Validate(profile.Replace("<key>", "<key>\nup command")).Accepted, "inline option injection rejected");
Check(!OpenVpnProfileValidator.Validate(new string('a', 262145)).Accepted, "oversized import rejected");
Check(OpenVpnProfileValidator.Validate(profile + "auth-user-pass\n").RequiresPassword, "credential-auth profile imports but cannot connect in certificate-only provider");
var strictTls = OpenVpnProfileValidator.Validate(profile + "tls-version-min 1.3\n");
Check(strictTls.Accepted && !strictTls.NormalizedProfile.Contains("tls-version-min 1.2"), "TLS 1.3 requirement is preserved instead of weakened");
Check(OpenVpnProvider.ParseManagementState(">STATE:1700000000,CONNECTED,SUCCESS,10.8.0.2,203.0.113.1") == VpnConnectionState.Connected, "connected requires actual successful management state");
Check(OpenVpnProvider.ParseManagementState("log: Initialization Sequence Completed") is null, "stdout text cannot spoof connected state");
Check(OpenVpnProvider.ParseManagementState(">STATE:1700000000,CONNECTED,ERROR") is null, "non-success state not connected");
Check(OpenVpnProvider.ParseManagementState(">STATE:1700000000,RECONNECTING,ping-restart") == VpnConnectionState.Connecting, "reconnect removes connected status");
Check(VpnProviderDirectory.Entries.Count == 0, "no unverifiable free-bandwidth/provider claims");
using (var handshakeInput = new MemoryStream(Encoding.UTF8.GetBytes("ENTER PASSWORD:SUCCESS: password is correct\r\n")))
using (var handshakeOutput = new MemoryStream())
using (var reader = new StreamReader(handshakeInput))
using (var writer = new StreamWriter(handshakeOutput) { AutoFlush = true, NewLine = "\n" })
{
    await OpenVpnProvider.AuthenticateManagementAsync(reader, writer, "fixture-management-password", CancellationToken.None);
    Check(Encoding.UTF8.GetString(handshakeOutput.ToArray()).Contains("fixture-management-password\n"), "actual management prompt without newline parses without hanging");
}
var root = Path.Combine(Path.GetTempPath(), "Pulsatilla-Vpn-Checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    var source = Path.Combine(root, "test.ovpn"); await File.WriteAllTextAsync(source, profile);
    var store = new DpapiVpnProfileStore(Path.Combine(root, "profiles")); var metadata = await store.ImportAsync(source);
    Check(store.List().Single().Id == metadata.Id && store.LoadConfiguration(metadata.Id) == profile, "current-user DPAPI profile roundtrip");
    var encrypted = File.ReadAllBytes(Directory.EnumerateFiles(store.Root).Single());
    Check(!Encoding.UTF8.GetString(encrypted).Contains("BEGIN PRIVATE KEY"), "profile key not plaintext at rest");
    var credentials = new VpnCredentialStore(Path.Combine(root, "credentials")); credentials.Save(metadata.Id, "user", "fixture-password".AsSpan());
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Directory.EnumerateFiles(Path.Combine(root, "credentials")).Single())).Contains("fixture-password"), "stored password DPAPI protected");
    credentials.Remove(metadata.Id); store.Remove(metadata.Id); Check(store.List().Count == 0, "remove own protected test data");
    var exe = Environment.ProcessPath!;
    var result = await SafeProcessRunner.RunAsync(exe, ["--child-output"], TimeSpan.FromSeconds(15), maxOutputChars: 1024);
    Check(result.ExitCode == 0 && result.OutputTruncated && result.Output.Length <= 1024 && result.Error.Length <= 1024, "both output streams drain concurrently under bounds");
    var watch = Stopwatch.StartNew(); var timedOut = false;
    var timeoutPid = Path.Combine(root, "timeout.pid");
    try { await SafeProcessRunner.RunAsync(exe, ["--child-wait", timeoutPid], TimeSpan.FromMilliseconds(500)); } catch (TimeoutException) { timedOut = true; }
    Check(timedOut && watch.Elapsed < TimeSpan.FromSeconds(5), "operation deadline terminates owned child promptly");
    Check(await ChildExitedAsync(timeoutPid), "timed-out child PID no longer runs");
    using var cancellation = new CancellationTokenSource(500); var cancelled = false; var cancelPid = Path.Combine(root, "cancel.pid");
    try { await SafeProcessRunner.RunAsync(exe, ["--child-wait", cancelPid], TimeSpan.FromSeconds(30), cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "cancellation stops owned child and preserves cancellation semantics");
    Check(await ChildExitedAsync(cancelPid), "cancelled child PID no longer runs");
    const string literal = "text with spaces & $(not-a-command) ; \"quoted\"";
    var echo = await SafeProcessRunner.RunAsync(exe, ["--echo", literal], TimeSpan.FromSeconds(5));
    Check(echo.Output == literal, "argument metacharacters remain literal and never enter a shell");
    var rejected = false; try { await SafeProcessRunner.RunAsync("cmd.exe", [], TimeSpan.FromSeconds(1)); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "relative executable search rejected");
}
finally { Directory.Delete(root, recursive: true); }
Console.WriteLine($"VPN/process checks: {checks} PASS. No real VPN, external requests, routes, DNS or firewall changes performed.");
static async Task<bool> ChildExitedAsync(string pidFile)
{
    if (!File.Exists(pidFile) || !int.TryParse(await File.ReadAllTextAsync(pidFile), out var pid)) return false;
    for (var i = 0; i < 20; i++)
    {
        try { using var child = Process.GetProcessById(pid); if (child.HasExited) return true; }
        catch (ArgumentException) { return true; }
        await Task.Delay(50);
    }
    return false;
}
