using System.Windows;
using System.Windows.Controls;

namespace Pulsatilla.Wpf;

public sealed class EmailAttachmentsPanel : UserControl
{
    private readonly ListBox _attachments = new() { DisplayMemberPath = nameof(EmailAttachment.FileName), MinHeight = 40, MaxHeight = 100 };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly HexViewer _hex = new() { MinHeight = 60 };
    private readonly Button _copy = new() { Content = "Copy attachment SHA-256", Margin = new Thickness(0, 6, 0, 0), IsEnabled = false };
    public EmailAttachmentsPanel()
    {
        var grid = new Grid { Margin = new Thickness(12) }; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
        var detailsScroll = new ScrollViewer { Content = _details, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        grid.Children.Add(_attachments); Grid.SetRow(detailsScroll, 1); grid.Children.Add(detailsScroll); Grid.SetRow(_copy, 2); grid.Children.Add(_copy); Grid.SetRow(_hex, 3); grid.Children.Add(_hex); Content = grid;
        SetResourceReference(ForegroundProperty, "Theme_E6F2E8");
        _details.Text = "Import an .eml in Email Inspector to inspect decoded attachments locally. Attachments stay in bounded memory and are never saved, opened or executed.";
        _attachments.SelectionChanged += (_, _) =>
        {
            _copy.IsEnabled = _attachments.SelectedItem is EmailAttachment;
            if (_attachments.SelectedItem is not EmailAttachment attachment) return;
            _details.Text = $"{attachment.FileName}\n{attachment.Size:N0} bytes • Extension {attachment.Extension} • Declared MIME {attachment.MimeType}\nSHA-256: {attachment.Sha256}\nEntropy: {attachment.Entropy:F2} bits/byte (not a maliciousness verdict)\n{attachment.Signature}\n" +
                string.Join("\n", attachment.Analysis.Matches.Select(m => m.Category + ": " + m.Name)) + "\nEmbedded URLs (not opened): " + string.Join(", ", attachment.EmbeddedUrls);
            _hex.SetBytes(attachment.CopyPreview(), attachment.Analysis);
        };
        _copy.Click += (_, _) => { if (_attachments.SelectedItem is EmailAttachment attachment) Clipboard.SetText(attachment.Sha256); };
    }
    public void SetMessage(EmailMessage? message)
    {
        _attachments.ItemsSource = message?.Attachments ?? [];
        _details.Text = message is null ? "No imported message." : $"{message.Attachments.Count} decoded attachment(s).\nReturn-Path: {message.ReturnPath}\nReceived: {message.Received[..Math.Min(message.Received.Length, 2000)]}\nAuthentication-Results (unverified message header): {message.AuthenticationResults[..Math.Min(message.AuthenticationResults.Length, 2000)]}\n" + string.Join("\n", message.ImportWarnings);
        _hex.SetBytes([]); if (message?.Attachments.Count > 0) _attachments.SelectedIndex = 0;
    }
}
