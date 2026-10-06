using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Pulsatilla.Wpf;

/// <summary>Experimental certificate-only control. Owns one direct OpenVPN child, not external VPN clients.</summary>
public sealed class OpenVpnProvider : IVpnProvider
{
    private readonly DpapiVpnProfileStore _store;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private Process? _process;
    private CancellationTokenSource? _session;
    private Task? _monitor;
    private TcpClient? _management;
    private StreamWriter? _managementWriter;
    private string? _temporaryDirectory;
    private bool _disposed;
    public VpnStatus Status { get; private set; } = new(VpnConnectionState.Disconnected, "No Pulsatilla-owned VPN session.", "", null, false);
    public event EventHandler? StatusChanged;
    public OpenVpnProvider(DpapiVpnProfileStore store) => _store = store;
    public static string? DetectInstalledExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var path = Path.Combine(programFiles, "OpenVPN", "bin", "openvpn.exe");
        return File.Exists(path) ? path : null;
    }
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public async Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken);
        var startedHere = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is not null) throw new InvalidOperationException("Disconnect the current Pulsatilla VPN session first.");
            var executable = DetectInstalledExecutable() ?? throw new InvalidOperationException("OpenVPN Community executable is not installed in the supported location.");
            LocalFileInspector.ValidateLocalFile(executable);
            if (WindowsSignatureInspector.Verify(executable).Status != LocalSignatureStatus.Valid)
                throw new InvalidOperationException("The installed OpenVPN executable does not have a verified embedded Windows signature.");
            if (!IsAdministrator()) throw new InvalidOperationException("Direct OpenVPN adapter/route changes require running Pulsatilla as Administrator. No elevation or connection was performed.");
            var check = OpenVpnProfileValidator.Validate(_store.LoadConfiguration(profile.Id));
            if (!check.Accepted || check.RequiresPassword) throw new InvalidOperationException("This integration connects only reviewed inline certificate profiles; password authentication is not yet supported.");
            cancellationToken.ThrowIfCancellationRequested();
            PrepareTemporaryProfile(check.NormalizedProfile);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                WorkingDirectory = _temporaryDirectory! };
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(Path.Combine(_temporaryDirectory!, "profile.ovpn"));
            // Password-authenticated loopback management; ephemeral port, never exposed remotely.
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
            var managementPort = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            var managementPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            start.ArgumentList.Add("--management"); start.ArgumentList.Add("127.0.0.1");
            start.ArgumentList.Add(managementPort.ToString(System.Globalization.CultureInfo.InvariantCulture)); start.ArgumentList.Add("stdin");
            start.ArgumentList.Add("--management-hold");
            _session = new CancellationTokenSource();
            _process = Process.Start(start) ?? throw new IOException("OpenVPN could not be started.");
            startedHere = true;
            // This random per-session management password enters stdin only; no credential file or argument.
            await _process.StandardInput.WriteLineAsync(managementPassword);
            _process.StandardInput.Close();
            SetStatus(VpnConnectionState.Connecting, "OpenVPN is establishing an experimental certificate-profile session.", profile.Name, null);
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _monitor = MonitorAsync(_process, profile.Name, connected, managementPort, managementPassword, _session.Token);
            try { await connected.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken); }
            catch
            {
                await StopOwnedAsync();
                if (_monitor is not null) try { await _monitor.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
                Cleanup();
                SetStatus(VpnConnectionState.Failed, "OpenVPN did not establish the session within its deadline, or connection was cancelled. The owned process was stopped.", profile.Name, null);
                throw;
            }
        }
        catch
        {
            if (startedHere)
            {
                await StopOwnedAsync();
                if (_monitor is not null) try { await _monitor.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
                Cleanup();
            }
            if (_process is null) Cleanup();
            throw;
        }
        finally { _operation.Release(); }
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            if (_process is null) { Cleanup(); SetStatus(VpnConnectionState.Disconnected, "No Pulsatilla-owned VPN session.", "", null); return; }
            SetStatus(VpnConnectionState.Disconnecting, "Stopping the Pulsatilla-owned OpenVPN process. Verify restored routes in Network / Routes.", Status.Profile, null);
            await StopOwnedAsync();
            if (_monitor is not null) try { await _monitor.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            Cleanup(); SetStatus(VpnConnectionState.Disconnected, "Owned OpenVPN process stopped. Route/DNS restoration must be checked; no kill switch is provided.", "", null);
        }
        finally { _operation.Release(); }
    }
    private async Task MonitorAsync(Process process, string profile, TaskCompletionSource<bool> connected, int port, string password, CancellationToken token)
    {
        var stderr = DrainDiscardAsync(process.StandardError, token);
        var stdout = DrainDiscardAsync(process.StandardOutput, token);
        try
        {
            using var setupDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            setupDeadline.CancelAfter(TimeSpan.FromSeconds(15));
            _management = new TcpClient();
            while (true)
            {
                try { await _management.ConnectAsync(IPAddress.Loopback, port, setupDeadline.Token); break; }
                catch (SocketException) { _management.Dispose(); _management = new TcpClient(); await Task.Delay(150, setupDeadline.Token); }
            }
            var clientPort = ((IPEndPoint)_management.Client.LocalEndPoint!).Port;
            if (!ManagementSocketOwnedBy(port, clientPort, process.Id))
                throw new IOException("The loopback management socket is not owned by the OpenVPN process started by Pulsatilla.");
            using var reader = new StreamReader(_management.GetStream(), Encoding.UTF8, false, 4096, leaveOpen: true);
            _managementWriter = new StreamWriter(_management.GetStream(), new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            await AuthenticateManagementAsync(reader, _managementWriter, password, setupDeadline.Token);
            await _managementWriter.WriteLineAsync("state on");
            await _managementWriter.WriteLineAsync("hold release");
            while (!token.IsCancellationRequested)
            {
                var line = await BoundedLineAsync(reader, token);
                if (line is null) break;
                var state = ParseManagementState(line);
                if (state == VpnConnectionState.Connected)
                {
                    var fields = line[7..].Split(',');
                    var address = fields.Length > 3 && IPAddress.TryParse(fields[3], out var assigned) ? assigned.ToString() : "";
                    var matches = address.Length == 0 ? [] : NetworkInterface.GetAllNetworkInterfaces()
                        .Where(a => a.GetIPProperties().UnicastAddresses.Any(ip => ip.Address.ToString() == address)).Take(2).ToArray();
                    SetStatus(VpnConnectionState.Connected, "OpenVPN reported initialization complete. This is not proof of anonymity or that all traffic uses the tunnel.", profile, DateTimeOffset.UtcNow,
                        address, matches.Length == 1 ? matches[0].Name : "Unknown");
                    connected.TrySetResult(true);
                }
                else if (state == VpnConnectionState.Connecting)
                    SetStatus(VpnConnectionState.Connecting, "OpenVPN reported a connection restart.", profile, null);
                else if (line.StartsWith(">PASSWORD:", StringComparison.Ordinal) || line.StartsWith(">FATAL:", StringComparison.Ordinal))
                    connected.TrySetException(new IOException("OpenVPN rejected the connection; inspect the provider configuration without sharing credentials."));
                // Raw server/profile output is never persisted or surfaced; it can contain secrets.
            }
            await process.WaitForExitAsync(token);
            if (!token.IsCancellationRequested) SetStatus(VpnConnectionState.Failed, "The owned OpenVPN process exited. Verify network routes and DNS.", profile, null);
            connected.TrySetException(new IOException("OpenVPN exited before establishing the connection."));
        }
        catch (OperationCanceledException) { connected.TrySetCanceled(); }
        catch (Exception) { connected.TrySetException(new IOException("The OpenVPN session monitor failed.")); SafeProcessRunner.TerminateOwned(process); }
        finally { try { await Task.WhenAll(stderr, stdout).WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
    }
    public static VpnConnectionState? ParseManagementState(string line)
    {
        if (!line.StartsWith(">STATE:", StringComparison.Ordinal) || line.Length > 4096) return null;
        var parts = line[7..].Split(',');
        if (parts.Length < 3 || !long.TryParse(parts[0], out _)) return null;
        return parts[1] switch { "CONNECTED" when parts[2] == "SUCCESS" => VpnConnectionState.Connected,
            "RECONNECTING" or "WAIT" or "AUTH" or "ASSIGN_IP" or "ADD_ROUTES" => VpnConnectionState.Connecting,
            "EXITING" => VpnConnectionState.Disconnecting, _ => null };
    }
    internal static async Task AuthenticateManagementAsync(StreamReader reader, StreamWriter writer, string password, CancellationToken token)
    {
        // OpenVPN sends its password prompt without CRLF (manage.c/man_prompt), not a normal line.
        var prompt = new char[15];
        var count = await reader.ReadBlockAsync(prompt.AsMemory(), token);
        if (count != prompt.Length || new string(prompt) != "ENTER PASSWORD:") throw new IOException("Unexpected OpenVPN management handshake.");
        await writer.WriteLineAsync(password.AsMemory(), token);
        var response = await BoundedLineAsync(reader, token);
        if (response is null || !response.StartsWith("SUCCESS: password is correct", StringComparison.Ordinal))
            throw new IOException("OpenVPN management authentication failed.");
    }
    [DllImport("iphlpapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    private static bool ManagementSocketOwnedBy(int serverPort, int clientPort, int processId)
    {
        var size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0) != 122 || size is < 4 or > 4_194_304) return false;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var capacity = size;
            if (GetExtendedTcpTable(buffer, ref size, false, 2, 5, 0) != 0 || size > capacity) return false;
            var count = Marshal.ReadInt32(buffer);
            if (count is < 0 or > 100_000 || 4L + count * 24L > size) return false;
            for (var i = 0; i < count; i++)
            {
                var row = IntPtr.Add(buffer, 4 + i * 24);
                var localPort = Marshal.ReadInt32(row, 8); localPort = ((localPort & 255) << 8) | ((localPort >> 8) & 255);
                var remotePort = Marshal.ReadInt32(row, 16); remotePort = ((remotePort & 255) << 8) | ((remotePort >> 8) & 255);
                if (Marshal.ReadInt32(row) == 5 && Marshal.ReadInt32(row, 4) == 0x0100007f && Marshal.ReadInt32(row, 12) == 0x0100007f &&
                    localPort == serverPort && remotePort == clientPort && Marshal.ReadInt32(row, 20) == processId) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static async Task<string?> BoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder(); var one = new char[1]; var received = false;
        while (await reader.ReadAsync(one.AsMemory(), token) != 0)
        {
            received = true; if (one[0] == '\n') break;
            if (text.Length < 4096 && one[0] != '\r') text.Append(one[0]);
        }
        return received ? text.ToString() : null;
    }
    private static async Task DrainDiscardAsync(StreamReader reader, CancellationToken token)
    { var buffer = new char[4096]; try { while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { } } catch (OperationCanceledException) { } }
    private void PrepareTemporaryProfile(string text)
    {
        if (_temporaryDirectory is not null) throw new IOException("A previous temporary profile still needs cleanup; no new session was started.");
        var root = Path.Combine(Path.GetTempPath(), "Pulsatilla-VPN");
        Directory.CreateDirectory(root);
        for (var parent = new DirectoryInfo(root); parent is not null; parent = parent.Parent)
            if (parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked temporary VPN directories are not supported.");
        _temporaryDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new IOException("Current Windows identity is unavailable.");
        security.SetOwner(sid);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(_temporaryDirectory).Create(security);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "profile.ovpn"), text, new UTF8Encoding(false));
    }
    private void SetStatus(VpnConnectionState state, string message, string profile, DateTimeOffset? connected, string address = "", string adapter = "")
    { Status = new(state, message, profile, connected, _process is not null, address, adapter); StatusChanged?.Invoke(this, EventArgs.Empty); }
    private async Task StopOwnedAsync()
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited && _managementWriter is not null)
                {
                    await _managementWriter.WriteLineAsync("signal SIGTERM").WaitAsync(TimeSpan.FromSeconds(1));
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException or InvalidOperationException) { }
            SafeProcessRunner.TerminateOwned(_process);
        }
        _session?.Cancel();
    }
    private void Cleanup()
    {
        _process?.Dispose(); _process = null; _session?.Dispose(); _session = null; _monitor = null;
        _managementWriter?.Dispose(); _managementWriter = null; _management?.Dispose(); _management = null;
        if (_temporaryDirectory is not null)
        {
            try
            {
                var file = Path.Combine(_temporaryDirectory, "profile.ovpn");
                if (File.Exists(file)) File.Delete(file);
                Directory.Delete(_temporaryDirectory, recursive: false); _temporaryDirectory = null;
            }
            catch (IOException) { SetStatus(VpnConnectionState.Failed, "Temporary VPN profile cleanup failed; remove the indicated protected session directory before retrying.", "", null); }
            catch (UnauthorizedAccessException) { SetStatus(VpnConnectionState.Failed, "Temporary VPN profile cleanup was denied; review protected temporary VPN storage.", "", null); }
        }
    }
    public async ValueTask DisposeAsync() { if (_disposed) return; await DisconnectAsync(); _disposed = true; }
}
