# VPN edition boundary

Current Community preview: 1.1.0-beta.2. VPN management is absent from this edition.
Adapter classification, physical/virtual/tunnel inventory, routed network-path evidence
and passive inspection of traffic on an existing tunnel remain available.

The previously published 1.1.0-beta.1 included an experimental certificate-only OpenVPN
implementation. Its release and MIT rights remain unchanged. That implementation and
its isolated synthetic checks now reside in private Core, with the MIT notice preserved.
Core's username/password vault does not supply credentials to this cert-only connector.
Real VPN connection, route/DNS recovery and interrupted-session testing remain unverified.
No WFP kill switch, split routing or WireGuard implementation is implied.

See [edition matrix](EDITION-FEATURE-MATRIX.md) and
[edition report](PULSATILLA-EDITION-REPORT.md).
