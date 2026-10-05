using System.IO;
using Pulsatilla.Wpf;

internal static class ProductChecks
{
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void CheckMigrationAndProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), "Pulsatilla.Checks." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var old = Path.Combine(root, "old"); var current = Path.Combine(root, "current");
            Directory.CreateDirectory(Path.Combine(old, "logs")); Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(old, "settings.json"), "{\"culture\":\"hu\"}");
            File.WriteAllText(Path.Combine(old, "protection.json"), "old policy");
            File.WriteAllText(Path.Combine(current, "protection.json"), "new policy");
            File.WriteAllText(Path.Combine(old, "secret.txt"), "ignore unrelated file");
            File.WriteAllText(Path.Combine(old, "logs", "netw-20261005.log"), "previous events");
            Assert(AppStorage.Migrate(old, current).Count == 0, "Profile migration generated warnings");
            Assert(File.ReadAllText(Path.Combine(current, "settings.json")).Contains("hu"), "Language preference lost");
            Assert(File.ReadAllText(Path.Combine(current, "protection.json")) == "new policy", "Existing Pulsatilla policy overwritten");
            Assert(File.Exists(Path.Combine(old, "settings.json")) && !File.Exists(Path.Combine(current, "secret.txt")), "Migration deleted old data or copied an unrelated file");
            Assert(File.Exists(Path.Combine(current, "logs", "pulsatilla-20261005.log")), "Old logs were not renamed during migration");
            Assert(AppStorage.Migrate(old, current).Count == 0, "Repeated migration was not idempotent");
            var names = WindowsFirewallRuleService.GetManagedRuleNames(@"C:\Apps\browser.exe");
            Assert(names.Count == 4 && names.Take(2).All(name => name.StartsWith("Pulsatilla-")) && names.Skip(2).All(name => name.StartsWith("NetWTool-")), "Firewall removal aliases missing");
            Assert(names.SequenceEqual(WindowsFirewallRuleService.GetManagedRuleNames(@"c:\apps\BROWSER.exe")), "Firewall identifiers changed with path casing");
            var fake = new FakeProvider(); var coordinator = new ExposureProviderCoordinator(fake);
            static void Rejected(Func<Task> call)
            {
                try { call().GetAwaiter().GetResult(); throw new Exception("Provider call was not rejected"); }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OperationCanceledException) { }
            }
            Rejected(() => coordinator.LookupAsync(new(["me@example.com"], false)));
            Rejected(() => coordinator.LookupAsync(new(["invalid", "me@example.com"], true)));
            Rejected(() => coordinator.LookupAsync(new([], true)));
            Rejected(() => coordinator.LookupAsync(new(Enumerable.Range(0, 21).Select(i => $"person{i}@example.com").ToArray(), true)));
            Rejected(() => new ExposureProviderCoordinator().LookupAsync(new(["me@example.com"], true)));
            Rejected(() => coordinator.LookupAsync(new(["me@example.com"], true), new CancellationToken(true)));
            Assert(fake.Calls == 0, "Provider dispatched without consent, valid addresses, configuration or cancellation check");
            var result = coordinator.LookupAsync(new(["ME@example.com", "me@example.com"], true)).GetAwaiter().GetResult();
            Assert(fake.Calls == 1 && fake.LastAddresses.SequenceEqual(new[] { "me@example.com" }) && result.Matches.Count == 0, "Provider addresses were not normalized and deduplicated");
        }
        finally { Directory.Delete(root, true); }
        Assert(ProductInfo.Name == "Pulsatilla" && ProductInfo.License == "MIT" && ProductInfo.Company == "Gabor Web", "Product identity changed unexpectedly");
        Console.WriteLine("Product: copy-only/idempotent profile migration, legacy firewall aliases and consent/configuration/cancellation provider boundaries passed; no external provider was contacted.");
    }
    private sealed class FakeProvider : IExposureProvider
    {
        public string Name => "Synthetic provider";
        public int Calls { get; private set; }
        public IReadOnlyList<string> LastAddresses { get; private set; } = [];
        public Task<ExposureLookupResult> LookupAsync(IReadOnlyList<string> emailAddresses, CancellationToken cancellationToken)
        { Calls++; LastAddresses = emailAddresses; return Task.FromResult(new ExposureLookupResult(Name, [], "Synthetic empty response")); }
    }
}
