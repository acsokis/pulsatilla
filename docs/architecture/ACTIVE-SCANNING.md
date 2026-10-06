# Active scanning and service-banner inspection

Active operations belong to **Scan**, separate from the shared passive **Inspect** session. Existing host discovery, TCP port scans and optional Nmap remain explicit operations; this module adds a narrowly bounded TCP service-banner workflow.

`ServiceBannerPanel.SetTarget(ip)` supplies a selected literal IP without starting anything. Its page displays **ACTIVE NETWORK OPERATION** and requires a confirmation for the concrete IP/port before calling `ActiveServiceScanner.InspectAsync`. Ports are 1–65535; only literal IPv4/IPv6 addresses are accepted. Hostnames, ranges, shorthand/ambiguous IPv4 addresses and unspecified/multicast/broadcast targets are rejected. No arbitrary public-range probes or page-load connections occur.

The scanner opens one TCP connection at a time, reads at most 16 KiB within a five-second operation deadline and then disposes its own connection. It sends **no application payload**. TCP connection establishment is itself active network traffic; authorization to inspect the target is required. Some protocols send an initial greeting, while HTTP/TLS and many others wait for a request: no banner is an honest result, not proof that a service is unavailable. No TLS or application-protocol handshake is initiated by this receive-only module.

Cancellation closes the owned connection. Deadline expiration retains any partial banner already received. Reaching the preview limit reports that additional bytes may exist; no multi-megabyte UI buffer is created. Calls are serialized with the same deadline bounding time spent waiting for an existing operation. The default transport honors cancellation for both connect and reads.

Returned bytes feed `FileInspectionPanel.SetSample(bytes, label, HexPatternContext.Scanner)`. The shared HEX/ASCII view and byte-pattern engine identify protocol/file/suspicious signatures as advisory interpretations, never confirmed attacks. Samples are not executed, browser-rendered or uploaded. Hash lookups and file inspection remain separate explicitly requested actions of the shared sample panel.

## Validation

Run `dotnet run --project tools/Scanner.Checks -c Release`. Injected read-only streams cover target/port rejection before transport use, exact banner bytes, empty responses, oversize bounds, silence deadlines, retained partial banners, cancellation/disposal and single-connection serialization. Tests perform no TCP connections, DNS lookups or external requests. Real authorized service servers, firewall refusal, IPv6 scoped targets and actual network interruption need separate manual checks.

Primary implementation contracts: [TcpClient.ConnectAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.tcpclient.connectasync), [Stream.ReadAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.readasync).
