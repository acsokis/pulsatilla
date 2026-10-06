using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Pulsatilla.Wpf;

public enum LocalSignatureStatus { Valid, Unsigned, Invalid, Unknown }
public sealed record SignatureInspection(LocalSignatureStatus Status, string Publisher, string Details);
public sealed record FileInspection(string Path, long Size, DateTime ModifiedUtc, string Sha256, SignatureInspection Signature,
    string Risk, IReadOnlyList<string> Reasons, byte[] Preview, HexAnalysisResult Analysis);

/// <summary>Explicit local inspection. Cache identity includes path, length and write time.</summary>
public sealed class LocalFileInspector
{
    public const long MaximumFileBytes = 512L * 1024 * 1024;
    private readonly SemaphoreSlim _workers = new(2);
    private readonly Dictionary<string, (DateTime At, FileInspection Value)> _cache = new(StringComparer.OrdinalIgnoreCase);
    public async Task<FileInspection> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = ValidateLocalFile(path); var info = new FileInfo(path);
        var key = path + "\0" + info.Length + "\0" + info.LastWriteTimeUtc.Ticks;
        lock (_cache) if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(5))
        { cancellationToken.ThrowIfCancellationRequested(); return cached.Value with { Preview = (byte[])cached.Value.Preview.Clone() }; }
        await _workers.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await Task.Run(async () =>
            {
                var before = new FileInfo(path); var size = before.Length; var modified = before.LastWriteTimeUtc;
                if (size > MaximumFileBytes) throw new IOException("File exceeds the 512 MiB inspection limit.");
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[65_536]; var preview = new byte[Math.Min(size, HexPatternEngine.MaximumSampleBytes)]; var readTotal = 0L; var previewCount = 0;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); if (read == 0) break;
                    readTotal += read; if (readTotal > MaximumFileBytes) throw new IOException("File grew beyond the inspection limit.");
                    hash.AppendData(buffer, 0, read);
                    var copy = Math.Min(read, preview.Length - previewCount); if (copy > 0) { buffer.AsSpan(0, copy).CopyTo(preview.AsSpan(previewCount)); previewCount += copy; }
                }
                cancellationToken.ThrowIfCancellationRequested();
                var signature = WindowsSignatureInspector.Verify(path);
                cancellationToken.ThrowIfCancellationRequested();
                var after = new FileInfo(path);
                if (after.Length != size || after.LastWriteTimeUtc != modified || readTotal != size) throw new IOException("File changed during inspection; retry.");
                var reasons = new List<string>();
                if (signature.Status == LocalSignatureStatus.Invalid) reasons.Add("Embedded signature did not pass Windows trust verification.");
                if (signature.Status == LocalSignatureStatus.Unsigned) reasons.Add("No embedded signature was found; a catalog signature was not checked.");
                if (signature.Status == LocalSignatureStatus.Unknown) reasons.Add("Signature trust could not be established locally.");
                var temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
                if (path.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) reasons.Add("File is located in the temporary directory.");
                if (signature.Status == LocalSignatureStatus.Valid) reasons.Add("Windows verified an embedded signature using local trust information; signing is not proof of safety.");
                var risk = signature.Status == LocalSignatureStatus.Invalid ? "High" : signature.Status == LocalSignatureStatus.Unknown ? "Unknown" : reasons.Any(r => r.StartsWith("File is located")) || signature.Status == LocalSignatureStatus.Unsigned ? "Medium" : "Low";
                return new FileInspection(path, size, modified, Convert.ToHexString(hash.GetHashAndReset()), signature, risk, reasons,
                    preview, HexPatternEngine.Default.Analyze(preview, HexPatternContext.File, cancellationToken));
            }, cancellationToken).ConfigureAwait(false);
            lock (_cache) { if (_cache.Count >= 128) _cache.Remove(_cache.MinBy(e => e.Value.At).Key); _cache[key] = (DateTime.UtcNow, result); }
            return result with { Preview = (byte[])result.Preview.Clone() };
        }
        finally { _workers.Release(); }
    }
    public static string ValidateLocalFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Choose an absolute local file path.");
        path = System.IO.Path.GetFullPath(path);
        if (new DriveInfo(System.IO.Path.GetPathRoot(path)!).DriveType == DriveType.Network) throw new ArgumentException("Network files are not inspected.");
        var info = new FileInfo(path); if (!info.Exists || info.Length > MaximumFileBytes) throw new IOException("File is missing or exceeds 512 MiB.");
        for (FileSystemInfo? current = info; current is not null; current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse points are not inspected.");
        return path;
    }
}

/// <summary>Embedded Authenticode verification through Windows; no network retrieval, UI or catalog claim.</summary>
public static class WindowsSignatureInspector
{
    public static SignatureInspection Verify(string path)
    {
        if (!OperatingSystem.IsWindows()) return new(LocalSignatureStatus.Unknown, "", "Windows trust API is unavailable.");
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer, StateAction = 1,
                // Whole-chain verification restricted to cached information. Offline failures remain Unknown.
                Revocation = 1, Flags = 0x1000 | 0x80 };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            int code;
            try { code = WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
            finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
            var status = code == 0 ? LocalSignatureStatus.Valid : code == unchecked((int)0x800B0100) ? LocalSignatureStatus.Unsigned :
                code is unchecked((int)0x80096010) or unchecked((int)0x800B0111) ? LocalSignatureStatus.Invalid : LocalSignatureStatus.Unknown;
            var publisher = "";
            if (status == LocalSignatureStatus.Valid)
            {
#pragma warning disable SYSLIB0057 // PE signer metadata extraction; Windows trust is evaluated separately above.
                try { using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)); publisher = cert.GetNameInfo(X509NameType.SimpleName, false); }
#pragma warning restore SYSLIB0057
                catch (CryptographicException) { }
            }
            return new(status, publisher, $"Embedded signature / offline Windows trust result 0x{code:X8}. Catalog signatures and current online revocation are not checked.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or CryptographicException)
        { return new(LocalSignatureStatus.Unknown, "", "Windows signature verification is unavailable."); }
        finally { Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer); }
    }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle; public IntPtr Subject; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustData
    {
        public uint Size; public IntPtr Policy; public IntPtr Sip; public uint Ui; public uint Revocation; public uint Choice; public IntPtr File;
        public uint StateAction; public IntPtr State; public IntPtr Url; public uint Flags; public uint Context; public IntPtr SignatureSettings;
    }
}
