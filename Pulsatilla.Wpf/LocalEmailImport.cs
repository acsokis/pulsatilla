using System.IO;
using System.Text;

namespace Pulsatilla.Wpf;

/// <summary>Bounded local import. Reads bytes without extracting or executing attachments.</summary>
public static class LocalEmailImport
{
    public static async Task<EmailMessage> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Choose a local message file.");
        path = Path.GetFullPath(path);
        var extension = Path.GetExtension(path);
        var isEmail = extension.Equals(".eml", StringComparison.OrdinalIgnoreCase);
        if (!isEmail && !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .eml and .txt files are supported.");
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Reparse paths are not supported for message import.");
        var limit = EmailSafetyService.MaximumMessageLength * (isEmail ? 1 : 4);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > limit) throw new ArgumentException("Message file is too large for the local reviewer.");
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)bytes.Length)), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (bytes.Length + count > limit) throw new ArgumentException("Message file exceeded the local review limit.");
            bytes.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isEmail) return EmailSafetyService.ParseMessage(bytes.ToArray());
            bytes.Position = 0;
            using var reader = new StreamReader(bytes, Encoding.UTF8, true, 8192, leaveOpen: true);
            return EmailSafetyService.ParseMessage(reader.ReadToEnd());
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
