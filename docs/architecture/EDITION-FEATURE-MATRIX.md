# Community / Core feature matrix

Audit date: 2026-10-06. This is the inspected implementation baseline and the requested target, not a release announcement. No source was moved before this matrix was produced.

| Feature | Community inspected baseline | Community target | Core inspected baseline / target | Shared code | Premium style |
|---|---|---|---|---|---|
| Network Path, active route, physical / virtual / tunnel classification | Available | Keep | Older UI; inherit current Community implementation | Yes | No |
| Live Traffic, connections, packet inspection | Available | Keep | Existing base; synchronize current workflows | Yes | No |
| Applications, protocol analysis, HEX / pattern analysis | Available | Keep | Inherit current Community workflows | Yes | No |
| Host discovery, ports, service scanning | Available | Keep | Inherit current Community workflows | Yes | No |
| Local security, firewall review, Defender integration | Available with existing OS permissions | Keep | Available; retain private extensions | Base only | Private extensions only |
| Email, local analysis, attachment review | Available | Keep | Inherit current Community workflows | Yes | No |
| History, settings, themes, optional Matrix background | Available | Keep | Available; synchronize | Yes | No |
| GeoIP, Ollama explanations, optional Npcap / Nmap | Existing optional integrations | Keep; no new service claim | Retain shared optional integrations | Yes | No |
| VPN / tunnel adapter detection and passive traffic on existing tunnel | Available | Keep | Keep | Yes | No |
| VPN page, OpenVPN profile import / connection controls | Experimental implementation currently present | Remove completely | Secure profile import exists; connection implementation must be integrated and validated | No new management code in Community | Core / experimental |
| VPN credentials | Existing protected-store implementation | Remove | Credential Manager / DPAPI workflow exists | No | Core |
| VPN route / DNS / public-IP observations | Experimental / partial | Remove management-specific UI | Partial; integrate factual observations, no anonymity guarantees | Generic networking observations only | Core / experimental |
| WireGuard management, WFP VPN kill switch | Not a stable implemented feature | Absent | Future; require recovery / OS validation | No | Future; no dead menus |
| Advanced threat providers, commercial integrations | No bundled commercial entitlement | Absent | Future / licensed integration boundaries; no stable-service claim | Interfaces where appropriate | Core |
| Secure P2P / messaging, advanced reporting | Not implemented as stable features | Absent | Future | No | Future; no dead menus |

## Source and licensing boundary

The new Community working tree will contain no VPN management pages, commands, credentials, profiles or services. Tunnel identification is ordinary networking and remains available. Core reuses the MIT Community foundation inward; proprietary Core implementation must never enter the public repository.

Previously published MIT source and release history retain their original rights. Removing management from a later Community version does not revoke the license of earlier releases. Existing released assets must not be overwritten to hide this history.

## Implementation gates

The present audit is complete; migration, capabilities, shared UI composition, premium styles and edition-boundary checks are pending. Build results for this migration: NOT RUN. Actual VPN connections, Windows DPI switching and firewall recovery are not verified by this source audit.
## Post-audit implementation checkpoint — 6 October 2026

The matrix above records the pre-migration state. The current Community assembly and
navigation exclude VPN management; Core composes the moved experimental module over
the common UI. Edition capabilities/styles and both synthetic workflow checks pass.
See [edition report](PULSATILLA-EDITION-REPORT.md) for evidence and remaining real-OS gates.
