# Shared passive traffic inspection

Milestone M2026-10-NETWORK-FIRST. Community .NET 8/WPF.

Dashboard and Network Path actions call the same `TrafficInspectionService` as the
Packet Inspector buttons. There is one capture lifetime, not one socket per page.
The persistent shell strip shows Starting / Inspecting / Stopping / Stopped / Failed,
the actual bound IPv4 address, current-session received packet count and elapsed time.
Flows/application totals are retained app-lifetime observations and labeled separately.

The existing Windows IPv4 raw socket backend requires administrator access; readiness
is announced only after bind/receive-all succeeds. It is not Npcap, and it does not
provide IPv6, loopback or complete endpoint visibility. Missing attribution is Unknown.
No user-independent capture, public-IP lookup, active scan or Internet latency probe starts.
Local counter/socket/route reads remain available without packet capture.

Windows route lookup selects AUTO observation. Manual selection changes observation,
not OS routing. A selected ID/address change or disappearance cancels the active lifetime;
restarting requires a new user action. Generation/state fencing rejects old callbacks.
Queued observations carry adapter ID and capture address so stale packets cannot be
attributed to a newly selected interface. A new explicit start clears pending samples;
already presented flow/history records remain available.

Capture feeds a bounded 4096-sample queue; UI analysis drains at most 200 packets or
12 ms every 150 ms. Backlog loss is disclosed. Live rows cap at 2000, application view
at 256, passive DNS names at 1024. UI hashing, external DNS and per-packet persistence
are not part of the callback. Existing history writes remain batched.

The packet viewer uses a bounded captured preview, decoded IPv4 endpoint/header metadata
and the shared HEX rule engine. Source/protocol views consume the same processed records.
Magic bytes and heuristics are review indicators, never confirmed attack evidence alone.

`tools/Inspection.Checks` uses injected fake backends for concurrent starts, cancellation,
late callbacks, subscriber faults, disposal and restart. UI checks strip operation handlers.
Actual elevated capture, high-traffic loss, sleep/resume, VPN/hardware transitions and
firewall recovery remain manual acceptance scenarios, not claimed by synthetic checks.
