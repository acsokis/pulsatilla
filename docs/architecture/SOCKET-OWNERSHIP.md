# Socket ownership and observed traffic

Community's `ProcessConnectionResolver` reads Windows' local TCP/UDP owner-PID tables for IPv4 and IPv6. Native imports are limited to the System32 IP Helper DLL; no process, shell or remote request is launched to enumerate sockets.

TCP resolution matches the complete local address/port and remote address/port tuple. A local port alone cannot identify a TCP owner when multiple applications use different remote endpoints. Conflicting exact owner records remain Unknown. Listener wildcard endpoints are not invented as established packet owners.

UDP is not a connection table. A packet can match one exact local address/port or a wildcard bind. Different owner PIDs sharing that wildcard, or conflicting exact/wildcard owners, remain Unknown. Unknown ports, unrelated addresses and unsupported protocols remain Unknown. Process executable paths may be unavailable because of process exit or access rights.

Each native buffer is bounded to 32 MiB and each native table to 100,000 rows before decoding. Retained sockets are bounded to 8192 across all tables. Invalid native lengths/counts are rejected; truncated snapshots cannot be used to guess packet ownership, because an omitted record could hide a shared binding. Refresh swaps bounded owner maps atomically.

`CurrentOwnedSockets` is a read-only bounded snapshot. `IsActiveConnection` is true only when the native TCP state is Established. UDP snapshots describe **bound endpoints**, not active connections. `SnapshotAtUtc` and `SnapshotTruncated` explain snapshot freshness and completeness. Packets and Live Traffic flow rows are a different measurement: a previously observed flow is not proof that a socket is still active.

`LiveTrafficRow` retains the existing constructor/properties and adds PID, both endpoint addresses and ports, passive hostname, country, status, timestamps and a flow key. Hostname and country default to Unknown. Packet rows default to **Observed packet (socket state unknown)**; they are not automatically labeled Established. Byte counters reject negative sizes and saturate safely.

`tools/Network.Checks` contains synthetic exact TCP tuple, inbound direction, wildcard/shared UDP, IPv6 layout/address/port, malformed table, overflow and row-cap tests. It sends no packets and changes no OS settings. Native state and PID ownership are point-in-time observations and can change between refreshes.

Primary references: [GetExtendedTcpTable](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable), [TCP owner row](https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcp6row_owner_pid), [UDP owner row](https://learn.microsoft.com/en-us/windows/win32/api/udpmib/ns-udpmib-mib_udp6row_owner_pid).
