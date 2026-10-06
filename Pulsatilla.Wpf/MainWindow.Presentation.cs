using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Pulsatilla.Wpf;

public sealed record ThemeChoice(ThemeMode Mode, string Name);

public partial class MainWindow
{
    private bool _presentationReady;
    private void InitializePresentation()
    {
        ThemeSelector.ItemsSource = new[] { new ThemeChoice(Pulsatilla.Wpf.ThemeMode.Dark, "Dark"), new ThemeChoice(Pulsatilla.Wpf.ThemeMode.Light, "Light"), new ThemeChoice(Pulsatilla.Wpf.ThemeMode.System, "Windows") };
        ThemeSelector.SelectedValue = _protection.Settings.ThemeMode;
        ThemeService.Changed += OnPresentationThemeChanged;
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        Closed += (_, _) => ThemeService.Changed -= OnPresentationThemeChanged;
        var creator = AboutContent.Creator;
        CreatorNameText.Text = creator.Name;
        CreatorLocationText.Text = creator.Location;
        InstagramButton.Tag = creator.Instagram; LinkedinButton.Tag = creator.Linkedin;
        FacebookButton.Tag = creator.Facebook; GithubButton.Tag = creator.Github;
        foreach (var button in new[] { InstagramButton, LinkedinButton, FacebookButton, GithubButton }) button.ToolTip = button.Tag;
        ProductTaglineText.Text = ProductInfo.Tagline;
        AboutVersionText.Text = ProductInfo.Name + " " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0") + " · " + ProductInfo.Edition + " · " + ProductInfo.License;
        ReadmeViewer.Document = AboutContent.CreateReadmeDocument();
        LicenseViewer.Document = AboutContent.CreateDocument("# MIT License\n\n" + AboutContent.LicenseText);
        ServicesViewer.Document = AboutContent.CreateDocument(AboutContent.Services);
        PrivacyViewer.Document = AboutContent.CreateDocument(AboutContent.Privacy);
        OriginViewer.Document = AboutContent.CreateDocument(AboutContent.Origin);
        OnPresentationThemeChanged(null, EventArgs.Empty);
        _presentationReady = true;
        ThroughputRangeBox.SelectedIndex = 0;
    }
    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_presentationReady || ThemeSelector.SelectedValue is not ThemeMode mode) return;
        ThemeService.Apply(mode);
        _protection.Settings.ThemeMode = mode;
        SaveProtectionSettings();
    }
    private void OnPresentationThemeChanged(object? sender, EventArgs e)
    {
        MatrixBackground.Opacity = ThemeService.IsLight ? .13 : .26;
        foreach (ListBoxItem item in EventsList.Items)
        {
            if (item.Content is not TextBlock text || item.Tag is not string line) continue;
            var start = line.IndexOf('['); var end = line.IndexOf(']');
            var level = start >= 0 && end > start ? line[(start + 1)..end] : "";
            var alarm = SecurityEventPolicy.IsAlarm(level);
            text.Foreground = ThemeService.Brush(alarm ? "FF707C" : level is "WARN" or "FIREWALL" ? "FFD26F" : level is "NEW APP" or "HOST" ? "89E7AC" : "CDDCD2");
            item.Background = ThemeService.Brush(alarm ? "200E12" : "09100B");
        }
        foreach (ListBoxItem item in AlertsList.Items)
            if (item.Content is TextBlock text) text.Foreground = ThemeService.Brush("FF969E");
    }
    private void CreatorLinkButton_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string url }) AboutContent.OpenLink(url); }
    private void ThroughputRangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThroughputChart is null || ThroughputSummaryText is null) return;
        ThroughputChart.WindowSeconds = ThroughputRangeBox.SelectedIndex switch { 1 => 300, 2 => 900, _ => 60 };
        ThroughputSummaryText.Text = ThroughputChart.Summary;
    }
    private void ThroughputFreezeButton_Click(object sender, RoutedEventArgs e)
    {
        ThroughputChart.IsFrozen = !ThroughputChart.IsFrozen;
        ThroughputFreezeButton.Content = ThroughputChart.IsFrozen ? "Resume live view" : "Freeze view";
        ThroughputSummaryText.Text = ThroughputChart.Summary;
    }
    private void ThroughputSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        ThroughputChart.ShowDownload = ThroughputDownloadToggle.IsChecked == true;
        ThroughputChart.ShowUpload = ThroughputUploadToggle.IsChecked == true;
    }
}
