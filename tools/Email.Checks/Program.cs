using System.Text;
using Pulsatilla.Wpf;

var directory = Path.Combine(Path.GetTempPath(), "Pulsatilla-email-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var passed = 0;
void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); passed++; }
async Task Reject(Func<Task> action, string label)
{ try { await action(); } catch (ArgumentException) { passed++; return; } throw new InvalidOperationException(label); }
try
{
    var messagePath = Path.Combine(directory, "message.eml");
    await File.WriteAllTextAsync(messagePath, "From: sender@example.invalid\r\nSubject: fixture\r\n\r\nlocal text", new UTF8Encoding(false));
    var message = await LocalEmailImport.ReadAsync(messagePath);
    Check(message.From.Contains("sender@example.invalid") && message.Body.Contains("local text"), "EML import");
    var textPath = Path.Combine(directory, "unicode.txt");
    await File.WriteAllTextAsync(textPath, "From: fixture@example.invalid\r\nSubject: Árvíztűrő\r\n\r\nMagyar szöveg", Encoding.Unicode);
    var unicode = await LocalEmailImport.ReadAsync(textPath);
    Check(unicode.Subject == "Árvíztűrő" && unicode.Body.Contains("Magyar szöveg"), "BOM-aware text import");
    await Reject(() => LocalEmailImport.ReadAsync("relative.eml"), "Relative path rejected");
    await Reject(() => LocalEmailImport.ReadAsync(@"\\example.invalid\share\message.eml"), "Network path rejected without accessing it");
    await Reject(() => LocalEmailImport.ReadAsync(Path.Combine(directory, "file.exe")), "Unsupported extension rejected");
    var large = Path.Combine(directory, "large.eml");
    await File.WriteAllBytesAsync(large, new byte[EmailSafetyService.MaximumMessageLength + 1]);
    await Reject(() => LocalEmailImport.ReadAsync(large), "Oversized EML rejected");
    var oversizedText = Path.Combine(directory, "large.txt");
    await File.WriteAllTextAsync(oversizedText, new string('x', EmailSafetyService.MaximumMessageLength + 1));
    await Reject(() => LocalEmailImport.ReadAsync(oversizedText), "Decoded text character bound");
    var exact = Path.Combine(directory, "exact.eml");
    await File.WriteAllBytesAsync(exact, Encoding.ASCII.GetBytes(new string('x', EmailSafetyService.MaximumMessageLength)));
    Check((await LocalEmailImport.ReadAsync(exact)).Body.Length == EmailSafetyService.MaximumMessageLength, "Exact bound accepted");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await LocalEmailImport.ReadAsync(messagePath, cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
    catch (OperationCanceledException) { passed++; }
    Console.WriteLine($"Local email import checks PASS: {passed}. Synthetic messages only; no uploads/execution.");
}
finally
{
    // Delete only this exact newly-created fixture directory under the system temp root.
    var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture outside temp root");
    Directory.Delete(directory, true);
}
