using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Pulsatilla.Wpf;

public partial class MainWindow
{
    private CancellationTokenSource? _emailOperation;
    private bool _emailClosed;

    private void InitializeEmailWorkflow()
    {
        // Use the retained email form; a drop/paste is an explicit local action.
        if (EmailFromInput.Parent is Panel form)
        {
            form.AllowDrop = true;
            form.PreviewDragOver += (_, e) =>
            {
                e.Effects = TryGetEmailDrop(e, out var droppedPath) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            form.PreviewDrop += async (_, e) =>
            {
                e.Handled = true;
                if (TryGetEmailDrop(e, out var path)) await ImportEmailPathAsync(path);
            };
            var paste = new Button { Content = "Paste complete message", Margin = new Thickness(0, 0, 8, 6) };
            paste.Click += async (_, _) =>
            {
                try
                {
                    if (!Clipboard.ContainsText()) { EmailResultText.Text = "Clipboard has no message text."; return; }
                    var text = Clipboard.GetText();
                    if (text.Length > EmailSafetyService.MaximumMessageLength) { EmailResultText.Text = "Message is too large for the local reviewer."; return; }
                    await ImportEmailAsync(token => Task.Run(() => { token.ThrowIfCancellationRequested(); return EmailSafetyService.ParseMessage(text); }, token), "Clipboard message");
                }
                catch (System.Runtime.InteropServices.ExternalException) { EmailResultText.Text = "Clipboard is currently unavailable."; }
            };
            form.Children.Insert(0, paste);
        }
    }

    private static bool TryGetEmailDrop(DragEventArgs e, out string path)
    {
        path = "";
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return false;
        var extension = Path.GetExtension(files[0]);
        if (!extension.Equals(".eml", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) return false;
        path = files[0]; return true;
    }

    private CancellationTokenSource BeginEmailOperation()
    {
        InvalidateEmailOperation();
        return _emailOperation = new CancellationTokenSource();
    }
    private void InvalidateEmailOperation()
    {
        var previous = _emailOperation; _emailOperation = null;
        previous?.Cancel(); // The owning async operation disposes its source after it completes.
    }
    private bool IsCurrentEmailOperation(CancellationTokenSource operation) => !_emailClosed && ReferenceEquals(operation, _emailOperation) && !operation.IsCancellationRequested;
    private void FinishEmailOperation(CancellationTokenSource operation)
    { if (ReferenceEquals(operation, _emailOperation)) _emailOperation = null; operation.Dispose(); }
    private void DisposeEmailWorkflow() { _emailClosed = true; InvalidateEmailOperation(); }

    private async void ImportEmailButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Email or text (*.eml;*.txt)|*.eml;*.txt", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await ImportEmailPathAsync(dialog.FileName);
    }
    private Task ImportEmailPathAsync(string path) => ImportEmailAsync(token => Task.Run(() => LocalEmailImport.ReadAsync(path, token), token), Path.GetFileName(path));

    private async Task ImportEmailAsync(Func<CancellationToken, Task<EmailMessage>> load, string label)
    {
        if (_emailClosed) return;
        var operation = BeginEmailOperation();
        EmailResultText.Text = "Reading message locally…";
        try
        {
            var message = await load(operation.Token);
            if (!IsCurrentEmailOperation(operation)) return;
            _importedEmail = message; _attachmentsPanel?.SetMessage(message);
            EmailFromInput.Text = message.From; EmailReplyInput.Text = message.ReplyTo;
            EmailRecipientsInput.Text = message.Recipients; EmailSubjectInput.Text = message.Subject; EmailBodyInput.Text = message.Body;
            EmailImportDetailsText.Text = "Imported locally: " + label +
                (message.AttachmentNames.Count > 0 ? "\nAttachment names: " + string.Join(", ", message.AttachmentNames) : "");
            EmailResultText.Text = "Message decoded locally. Select Analyze to inspect it. Nothing was executed or uploaded.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or RegexMatchTimeoutException)
        { if (IsCurrentEmailOperation(operation)) EmailResultText.Text = "Could not import message: local file is inaccessible, unsupported, oversized or too complex."; }
        finally { FinishEmailOperation(operation); }
    }

    private async void AnalyzeEmailButton_Click(object sender, RoutedEventArgs e)
    {
        if (_emailClosed) return;
        var message = new EmailMessage(EmailFromInput.Text, EmailReplyInput.Text, EmailRecipientsInput.Text, EmailSubjectInput.Text, EmailBodyInput.Text)
        { AttachmentNames = _importedEmail?.AttachmentNames ?? [], ImportWarnings = _importedEmail?.ImportWarnings ?? [],
            Attachments = _importedEmail?.Attachments ?? [], ReturnPath = _importedEmail?.ReturnPath ?? "",
            Received = _importedEmail?.Received ?? "", AuthenticationResults = _importedEmail?.AuthenticationResults ?? "" };
        var watched = WatchedEmailsInput.Text; var trusted = TrustedSendersInput.Text; var blocked = BlockedSendersInput.Text;
        var operation = BeginEmailOperation(); EmailResultText.Text = "Analyzing locally…";
        try
        {
            var result = await Task.Run(() => { operation.Token.ThrowIfCancellationRequested(); return EmailSafetyService.Analyze(message, watched, trusted, blocked); }, operation.Token);
            if (IsCurrentEmailOperation(operation)) EmailResultText.Text = result.Summary + (result.FilteredOut ? "" : $"\nIndicator score: {result.Score}/100") +
                "\n\n" + string.Join("\n\n", result.Findings.Select(finding => "• " + finding));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        { if (IsCurrentEmailOperation(operation)) EmailResultText.Text = "Message is oversized or too complex for this local reviewer. No links were opened."; }
        finally { FinishEmailOperation(operation); }
    }
}
