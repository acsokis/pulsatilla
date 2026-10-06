using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pulsatilla.Wpf;

internal static class VpnDataProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    public static byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows() || bytes.Length is < 1 or > 524288) throw new InvalidOperationException("Windows DPAPI requires a bounded nonempty payload.");
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = protect ? CryptProtectData(ref input, "Pulsatilla VPN", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) :
                CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success || output.Length is < 1 or > 524288) throw new CryptographicException("Windows could not protect or read this VPN data for the current user.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            var zero = new byte[bytes.Length]; Marshal.Copy(zero, 0, input.Data, zero.Length); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
}

public sealed class DpapiVpnProfileStore
{
    private sealed record StoredProfile(VpnProfile Metadata, string Configuration);
    public string Root { get; }
    public DpapiVpnProfileStore(string? root = null) => Root = root ?? AppStorage.FilePath("vpn-profiles");
    public async Task<VpnProfile> ImportAsync(string file, CancellationToken token = default)
    {
        var info = new FileInfo(file);
        LocalFileInspector.ValidateLocalFile(file);
        if (!info.Exists || info.Length > OpenVpnProfileValidator.MaximumBytes || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            !string.Equals(info.Extension, ".ovpn", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a regular .ovpn file no larger than 256 KiB.");
        var raw = await ReadBoundedAsync(file, OpenVpnProfileValidator.MaximumBytes, token);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(raw).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new InvalidDataException("OpenVPN profiles must use valid UTF-8 text."); }
        finally { CryptographicOperations.ZeroMemory(raw); }
        var check = OpenVpnProfileValidator.Validate(text);
        if (!check.Accepted) throw new InvalidDataException(string.Join(" ", check.Findings));
        EnsureDirectory();
        if (Directory.EnumerateFiles(Root, "*.vpn").Take(51).Count() >= 50) throw new InvalidDataException("The local profile limit is 50.");
        var name = Path.GetFileNameWithoutExtension(file); name = name[..Math.Min(name.Length, 80)];
        var profile = new VpnProfile(Guid.NewGuid(), name, DateTimeOffset.UtcNow, check.RequiresPassword, check.Endpoint);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new StoredProfile(profile, text));
        try { var cipher = VpnDataProtection.Transform(bytes, true); await File.WriteAllBytesAsync(PathFor(profile.Id), cipher, token); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        return profile;
    }
    public IReadOnlyList<VpnProfile> List()
    {
        if (!Directory.Exists(Root)) return [];
        EnsureDirectory(); var profiles = new List<VpnProfile>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.vpn").Take(50))
            if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id))
                try { profiles.Add(Read(id).Metadata); } catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or InvalidDataException) { }
        return profiles;
    }
    public string LoadConfiguration(Guid id) => Read(id).Configuration;
    public void Remove(Guid id) { EnsureDirectory(); File.Delete(PathFor(id)); }
    private StoredProfile Read(Guid id)
    {
        EnsureDirectory(); var path = PathFor(id); var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 1 or > 524288 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("The protected profile is unavailable.");
        var encrypted = ReadBoundedAsync(path, 524288, CancellationToken.None).GetAwaiter().GetResult();
        var bytes = VpnDataProtection.Transform(encrypted, false);
        try
        {
            var result = JsonSerializer.Deserialize<StoredProfile>(bytes) ?? throw new InvalidDataException("Invalid profile data.");
            if (result.Metadata.Id != id) throw new InvalidDataException("Profile identity mismatch.");
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private string PathFor(Guid id) => Path.Combine(Root, id.ToString("N") + ".vpn");
    private static async Task<byte[]> ReadBoundedAsync(string file, int maximum, CancellationToken token)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        var buffer = new byte[maximum + 1]; var count = 0;
        while (count < buffer.Length)
        { var read = await stream.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false); if (read == 0) break; count += read; }
        if (count > maximum) { CryptographicOperations.ZeroMemory(buffer); throw new InvalidDataException("The profile exceeds its bounded read limit."); }
        var result = buffer[..count]; CryptographicOperations.ZeroMemory(buffer); return result;
    }
    private void EnsureDirectory()
    {
        Directory.CreateDirectory(Root);
        var current = new DirectoryInfo(Path.GetFullPath(Root));
        while (current is not null) { if (current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked VPN storage directories are not supported."); current = current.Parent; }
    }
}

/// <summary>DPAPI current-user credential storage; the initial certificate-only provider does not consume these.</summary>
public sealed class VpnCredentialStore
{
    private readonly string _root;
    public VpnCredentialStore(string? root = null) => _root = root ?? AppStorage.FilePath("vpn-credentials");
    public void Save(Guid id, string username, ReadOnlySpan<char> password)
    {
        if (username.Length > 256 || password.Length is < 1 or > 1024 || username.Any(char.IsControl) || password.Contains('\n') || password.Contains('\r'))
            throw new ArgumentException("Invalid credential length or control character.");
        Directory.CreateDirectory(_root);
        if (new DirectoryInfo(_root).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked credential storage is not supported.");
        var bytes = Encoding.UTF8.GetBytes(username + "\n" + password.ToString());
        try { File.WriteAllBytes(Path.Combine(_root, id.ToString("N") + ".credential"), VpnDataProtection.Transform(bytes, true)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Remove(Guid id) => File.Delete(Path.Combine(_root, id.ToString("N") + ".credential"));
}
