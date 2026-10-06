# Community VPN integration

The Community implementation is independent C#/.NET 8 code. It does not import the private Core implementation. OpenVPN Community must already be installed; Pulsatilla supplies neither VPN servers nor bandwidth.

## Availability

- Protected local `.ovpn` import/removal and local adapter/DNS display: **AVAILABLE**.
- Installed OpenVPN detection: the standard `Program Files/OpenVPN/bin/openvpn.exe` location only. OpenVPN Connect is a different client and is not controlled by this provider.
- Direct certificate-only OpenVPN connection/disconnection: **EXPERIMENTAL**, requires administrator rights and a valid embedded Windows signature on the installed executable. Actual driver, real server, DNS/route recovery and interrupted-session OS testing remain unverified here.
- Username/password, MFA and encrypted-private-key prompting: **COMING LATER**. A profile with `auth-user-pass` can be imported, but Connect is disabled; credential storage is not an operational authentication integration.
- Kill switch, split routing, anonymity guarantees and free provider bandwidth: unavailable, with no pretend operational controls.

## Model and lifetime

`VpnProfile`, `VpnConnectionState` and `IVpnProvider` define the provider boundary. `OpenVpnProvider` owns exactly one process it launches and never stops unrelated VPN software. Imported profile IDs are GUIDs, not user-controlled filesystem paths. Connect is an explicit confirmed UI operation.

The reviewed profile subset allows ordinary client transport settings and inline certificate/key blocks. Unknown directives are rejected, including scripts, plugins, included configurations, external credential/certificate paths, profile-defined management endpoints and output files. It requires server-certificate purpose checking and enforces TLS 1.2 minimum, script-security 1 and auth-nocache. Import validates configuration structure; it does not prove a CA/server identity is trustworthy or a certificate is valid. Providers with other legitimate directives may require a later reviewed extension.

The installed executable is addressed by absolute path, checked for linked/network paths and verified using offline Windows embedded-signature verification before launch. A random per-session loopback management password is delivered through stdin, never a process argument or password file. Before sending it, the IPv4 native TCP owner table must confirm that the established loopback server connection belongs to the child process Pulsatilla started. Management state events, not raw stdout text, determine CONNECTED. The assigned tunnel IP comes from that actual management event and an interface is named only if exactly one local adapter matches the address. A 90-second initial connection deadline bounds establishment. Disconnect requests `signal SIGTERM` through management for normal OpenVPN cleanup, then terminates only the owned process tree if the bounded grace period expires. Route/DNS restoration is not assumed; the user is directed to the Routes page afterward.

Profiles and credential storage use Windows DPAPI for the current user. OpenVPN needs a transient decrypted configuration during the owned session; it is placed in a newly created GUID directory with inheritance disabled and current-user-only access, then removed on disconnect/disposal. This includes an inline private key for certificate profiles. A crash or forced process termination can leave a protected temporary file; inspect `%TEMP%/Pulsatilla-VPN` after an interrupted session. No plaintext password is written. Raw provider/profile/server logs and private keys are never persisted or routed to development memory. Bounded status events provide only generic connection progress.

Local DNS addresses are configured DNS data, not a DNS-leak test. Tunnel underlay and route relationships are supplied by the Network/Routes workflow; unknown relationships remain unknown. Public-IP checking calls the fixed `https://api.ipify.org` endpoint only through the clearly labeled manual action, with a six-second deadline, no redirects/cookies and a 256-byte response limit. Before/after observations stay in panel memory. An IP change is not anonymity proof.

`VpnProviderDirectory` supplies a dated neutral schema with no current entries. A future entry needs source-backed verification rather than indefinitely hard-coded free-plan claims.

## Process hardening and tests

`SafeProcessRunner` uses literal `ArgumentList`, absolute executable paths, concurrent stdout/stderr drains, bounded retained output, cancellation/deadline semantics and owned-child cleanup. The existing Windows Firewall wrapper uses this runner without changing the existing confirmation/API behavior.

Run `dotnet run --project tools/Vpn.Checks -c Release`. Fixtures cover dangerous directives, required certificate-purpose checks, bounded imports, management-state parsing, DPAPI profile/credential roundtrips, output saturation, timeout/cancellation and relative-path rejection. They launch only this test program's synthetic child modes, with no real VPN, route, DNS, firewall or external requests. Real VPN connect/disconnect, route restoration, interrupted sessions and installed-driver combinations need separate explicitly authorized manual OS testing.

Primary references: [OpenVPN 2.6 manual](https://openvpn.net/community-docs/community-articles/openvpn-2-6-manual.html), [OpenVPN management interface](https://openvpn.net/community-docs/management-interface.html), [Windows CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata).

The no-newline management-password prompt is verified against the official [OpenVPN 2.6 management source](https://github.com/OpenVPN/openvpn/blob/release/2.6/src/openvpn/manage.c) and has a synthetic transcript fixture.
