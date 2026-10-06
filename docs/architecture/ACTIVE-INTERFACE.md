# Active interface service

`ActiveInterfaceService` is the centralized observation selection service. AUTO is the initial mode. `NetworkChange.NetworkAddressChanged` wakes the background loop; a three-second refresh also catches route/metric changes that do not generate address notifications. A semaphore prevents overlapping native snapshots. Cancellation suppresses publication after disposal.

Selection follows the Windows result from `GetBestRoute2` for `1.1.1.1` first, then `2606:4700:4700::1111` when IPv4 is unavailable. These are local route-table queries, not DNS or network requests. They include more-specific routes such as OpenVPN's paired `/1` override, which inspecting only `0.0.0.0/0` would miss. IPv4 and IPv6 effective interfaces are shown separately. Per-destination/policy/application routing can differ, and no universal route guarantee is made.

MANUAL uses a still-operational non-loopback adapter ID. An unavailable manual interface falls back without rebinding to an arbitrary remembered index. Without a routed interface, operational fallback is explicitly unverified. Capture capabilities remain separate: Community's existing raw socket capture is IPv4-only and requires elevation; an IPv6-only route is visible without claiming IPv6 packet capture.

`GetIpForwardTable2` provides route destination/prefix/gateway/offset metrics. `GetIpInterfaceEntry` provides per-family automatic/interface metrics. The effective preference shown is route metric plus interface metric using unsigned 64-bit arithmetic; missing interface metrics remain Unknown. More-specific prefixes take precedence over default routes; equal metric defaults are displayed without claiming they prove which application path is used.

`InterfacePriorityService` uses `SetIpInterfaceEntry` only after the UI's explicit confirmation. It does not launch a shell or elevate itself. Manual metrics are constrained to 1–9999. The API stores previous state, verifies readback, attempts rollback on failure, and keeps a last-change undo record. Undo refuses stale-state overwrites.

Run synthetic checks with `dotnet run --project tools/Network.Checks -c Release`. Add `-- --native-read-only` only for local read-only ABI verification. Neither mode performs settings changes or sends probes. Real Ethernet/Wi-Fi/VPN/Hyper-V/USB/removal/sleep/resume transitions and administrator changes remain manual test cases.

Primary references: [GetBestRoute2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getbestroute2), [route row](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_ipforward_row2), [interface metrics](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_ipinterface_row).
