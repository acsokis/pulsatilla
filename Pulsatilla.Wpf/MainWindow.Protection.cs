using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Pulsatilla.Wpf;

public partial class MainWindow
{
    private readonly ProtectionSettingsStore _protection = new();
    private readonly NetworkThreatMonitor _threatMonitor = new();
    private readonly Dictionary<string, DateTime> _lastBlockedActivity = new(StringComparer.OrdinalIgnoreCase);
    private bool _protectionInitialized;
    private EmailMessage? _importedEmail;
    public ObservableCollection<ApplicationPolicy> ApplicationPolicies { get; } = [];

    private void InitializeProtection()
    {
        AttackMonitoringBox.IsChecked = _protection.Settings.MonitorAttacks;
        DnsRotationBox.IsChecked = _protection.Settings.ReportDnsRotation;
        RainQualityBox.ItemsSource = Enum.GetValues<RainQuality>();
        RainQualityBox.SelectedItem = _protection.Settings.RainQuality;
        MatrixBackground.Quality = _protection.Settings.RainQuality;
        WatchedEmailsInput.Text = _protection.Settings.WatchedEmails;
        TrustedSendersInput.Text = _protection.Settings.TrustedSenders;
        BlockedSendersInput.Text = _protection.Settings.BlockedSenders;
        _threatMonitor.Enabled = _protection.Settings.MonitorAttacks;
        RefreshApplicationPolicies();
        _protectionInitialized = true;
    }

    private void RefreshApplicationPolicies()
    {
        ApplicationPolicies.Clear();
        foreach (var rule in _protection.Settings.Applications) ApplicationPolicies.Add(rule);
        foreach (var row in LiveTrafficRows) row.Trust = _protection.GetTrust(row.ExecutablePath);
    }

    private void RainQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RainQualityBox.SelectedItem is not RainQuality quality) return;
        MatrixBackground.Quality = quality;
        if (!_protectionInitialized) return;
        _protection.Settings.RainQuality = quality;
        SaveProtectionSettings();
    }

    private void ProtectionOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_protectionInitialized) return;
        _protection.Settings.MonitorAttacks = AttackMonitoringBox.IsChecked == true;
        _protection.Settings.ReportDnsRotation = DnsRotationBox.IsChecked == true;
        _threatMonitor.Enabled = _protection.Settings.MonitorAttacks;
        if (!_threatMonitor.Enabled) _threatMonitor.Reset();
        SaveProtectionSettings();
    }

    private void SaveProtectionSettings()
    {
        try { _protection.Save(); ProtectionStatus.Text = "Protection preferences saved locally."; }
        catch (Exception ex) { ProtectionStatus.Text = "Could not save protection preferences: " + ex.Message; }
    }

    private void OpenApplicationRulesButton_Click(object sender, RoutedEventArgs e)
    {
        UseLiveAppButton_Click(sender, e);
        MainTabs.SelectedItem = ProtectionTab;
        ProtectionTabs.SelectedIndex = 0;
    }

    private void UseLiveAppButton_Click(object sender, RoutedEventArgs e)
    {
        var path = (LiveTrafficGrid.SelectedItem as LiveTrafficRow)?.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path)) path = (UsageGrid.SelectedItem as UsageSummary)?.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path)) { ProtectionStatus.Text = "Select an attributed app in Live traffic or Usage history first, or choose an executable."; return; }
        PolicyPathInput.Text = path;
    }

    private void ChooseExecutableButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) PolicyPathInput.Text = dialog.FileName;
    }

    private void ApplicationPoliciesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApplicationPoliciesGrid.SelectedItem is ApplicationPolicy policy) PolicyPathInput.Text = policy.ExecutablePath;
    }

    private async void ApplicationPolicyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } || !Enum.TryParse<ApplicationTrust>(value, out var trust)) return;
        var path = PolicyPathInput.Text.Trim();
        if (!Path.IsPathFullyQualified(path) || trust != ApplicationTrust.Monitor && !File.Exists(path))
        { ProtectionStatus.Text = "Choose an existing executable with a full path; a removed executable can still have its block rules cleared."; return; }
        var needsFirewall = trust == ApplicationTrust.Blocked || _firewallRules.IsBlocked(path);
        if (needsFirewall && MessageBox.Show(this, (trust == ApplicationTrust.Blocked
            ? "Block inbound and outbound traffic for this application?" : "Remove Pulsatilla's block rules for this application?") +
            "\n\n" + path, "Application network policy", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        PolicyActions.IsEnabled = false;
        try
        {
            if (needsFirewall)
            {
                var result = await _firewallRules.SetBlockedAsync(path, trust == ApplicationTrust.Blocked);
                ProtectionStatus.Text = result.Message;
                AddEvent(result.Success ? "FIREWALL" : "FIREWALL ERROR", $"{Path.GetFileName(path)}: {result.Message}");
                if (!result.Success) return;
            }
            RecordApplicationPolicy(path, trust);
            ProtectionStatus.Text = trust switch
            {
                ApplicationTrust.Trusted => "Whitelisted: routine new-app notices are quiet. Attack monitoring stays active; other firewall rules are unchanged.",
                ApplicationTrust.Blocked => "Blacklisted: Pulsatilla inbound/outbound block rules are installed. Administrator rights are required.",
                _ => "Monitor mode: Pulsatilla block rules are removed; routine app notices and attack monitoring remain active."
            };
        }
        catch (Exception ex) { ProtectionStatus.Text = "Policy update failed: " + ex.Message + (needsFirewall ? " Check Windows Firewall: its rules may already have changed." : ""); }
        finally { PolicyActions.IsEnabled = true; }
    }

    private void RecordApplicationPolicy(string path, ApplicationTrust trust)
    {
        _protection.SetTrust(path, trust);
        _lastBlockedActivity.Remove(path);
        RefreshApplicationPolicies();
    }

    private void ObserveApplicationActivity(string application, string path, string host, DateTime observedAt)
    {
        var first = _knownApplicationPaths.Add(path);
        var trust = _protection.GetTrust(path);
        if (trust == ApplicationTrust.Blocked)
        {
            if (_lastBlockedActivity.TryGetValue(path, out var previous) && observedAt - previous < TimeSpan.FromMinutes(2)) return;
            if (_lastBlockedActivity.Count >= 512) _lastBlockedActivity.Remove(_lastBlockedActivity.MinBy(pair => pair.Value).Key);
            _lastBlockedActivity[path] = observedAt;
            AddEvent("BLOCKLIST REVIEW", $"Traffic observed for blacklisted app {application} ({host}). Verify Windows Firewall rules; capture can observe attempted traffic before filtering.");
        }
        else if (first && trust != ApplicationTrust.Trusted) AddEvent("NEW APP", $"{application} started network activity ({host})");
    }

    private void SaveEmailFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _protection.Settings.WatchedEmails = WatchedEmailsInput.Text;
        _protection.Settings.TrustedSenders = TrustedSendersInput.Text;
        _protection.Settings.BlockedSenders = BlockedSendersInput.Text;
        SaveProtectionSettings();
    }

    private void ImportEmailButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Email or text (*.eml;*.txt)|*.eml;*.txt", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > EmailSafetyService.MaximumMessageLength * 4)
                throw new ArgumentException("File is too large for the local reviewer.");
            var message = Path.GetExtension(dialog.FileName).Equals(".eml", StringComparison.OrdinalIgnoreCase)
                ? EmailSafetyService.ParseMessage(File.ReadAllBytes(dialog.FileName)) : EmailSafetyService.ParseMessage(File.ReadAllText(dialog.FileName));
            _importedEmail = message;
            EmailFromInput.Text = message.From; EmailReplyInput.Text = message.ReplyTo;
            EmailRecipientsInput.Text = message.Recipients; EmailSubjectInput.Text = message.Subject;
            EmailBodyInput.Text = message.Body;
            EmailImportDetailsText.Text = "Imported locally: " + Path.GetFileName(dialog.FileName) +
                (message.AttachmentNames.Count > 0 ? "\nAttachment names: " + string.Join(", ", message.AttachmentNames) : "");
            EmailResultText.Text = "Message text decoded locally. Select Analyze to inspect it.";
        }
        catch (Exception ex) { EmailResultText.Text = "Could not import message: " + ex.Message; }
    }

    private void AnalyzeEmailButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var message = new EmailMessage(EmailFromInput.Text, EmailReplyInput.Text, EmailRecipientsInput.Text, EmailSubjectInput.Text, EmailBodyInput.Text)
            { AttachmentNames = _importedEmail?.AttachmentNames ?? [], ImportWarnings = _importedEmail?.ImportWarnings ?? [] };
            var result = EmailSafetyService.Analyze(message,
                WatchedEmailsInput.Text, TrustedSendersInput.Text, BlockedSendersInput.Text);
            EmailResultText.Text = result.Summary + (result.FilteredOut ? "" : $"\nIndicator score: {result.Score}/100") +
                "\n\n" + string.Join("\n\n", result.Findings.Select(finding => "• " + finding));
        }
        catch (RegexMatchTimeoutException) { EmailResultText.Text = "Message is too complex for this local reviewer. No links were opened."; }
        catch (Exception ex) { EmailResultText.Text = "Analysis could not finish: " + ex.Message; }
    }

    private void ClearEmailButton_Click(object sender, RoutedEventArgs e)
    {
        EmailFromInput.Clear(); EmailReplyInput.Clear(); EmailRecipientsInput.Clear(); EmailSubjectInput.Clear(); EmailBodyInput.Clear();
        _importedEmail = null;
        EmailImportDetailsText.Text = "";
        EmailResultText.Text = "Paste a message or import an .eml/.txt file to review it locally.";
    }

    private void ImportExposureReportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Local exposure reports (*.csv;*.json)|*.csv;*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > LocalExposureService.MaximumReportLength * 4) throw new ArgumentException("Report file is too large.");
            var result = LocalExposureService.Review(File.ReadAllText(dialog.FileName), Path.GetExtension(dialog.FileName), WatchedEmailsInput.Text);
            ExposureResultsGrid.ItemsSource = result.Matches;
            ExposureStatusText.Text = $"Reviewed {result.RowsReviewed:N0} local records: {result.Matches.Count:N0} matching entries" +
                (result.OmittedMatches > 0 ? $" ({result.OmittedMatches:N0} additional matches omitted)" : "") +
                ". This checks the supplied report only; no live breach search was performed." +
                (result.Matches.Count > 0 ? " Change affected passwords in the official service, use unique passwords and enable MFA." : " No match does not prove that an address has never been exposed.");
        }
        catch (Exception ex) { ExposureResultsGrid.ItemsSource = null; ExposureStatusText.Text = "Report could not be reviewed: " + ex.Message; }
    }
    private void ClearExposureReportButton_Click(object sender, RoutedEventArgs e)
    { ExposureResultsGrid.ItemsSource = null; ExposureStatusText.Text = "No local report loaded."; }
}
