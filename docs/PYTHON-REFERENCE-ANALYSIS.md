# Python reference analysis

Review date: **6 October 2026**. Subject: the owner-supplied netw_tool_v05 Python reference
and an additional supplied source-family/search analysis. This report preserves substantive
observations without converting hypotheses into authorship, copying or AI-use findings.

## Evidence and scope

The archive was opened read-only, with no extraction or execution and no redistribution
of its Python files/logo. Verified archive size: **89,268 bytes**. SHA-256:
**90e7e063aca3b2096ed1695c2faf350a341cd30475227f368b601c62efe1c1e5**.
Ten entries contain nine Python modules (105,283 source bytes) and logo.png.
Names/sizes/date labels match the prior [archive review](ORIGIN-REVIEW.md).

A capped in-memory UTF-8 scan found no dedicated license/readme entry or source copyright/
license declaration. Original byte identity does not establish individual authorship,
copyright ownership or permission. No original upstream repository was established.
No new execution/performance benchmark or line-by-line commercial-clearance audit was performed.

API constructs in core.py, scanner.py, rightpanel.py and ui.py were checked statically.
The checked primary documentation confirms API meanings. It is current documentation,
not proof of the author's reading history or a dated source from which code was copied.
Exact 2018 gist/old StackOverflow links and historical snapshots were not supplied or verified.

The observations below also appear in [ORIGIN.md](ORIGIN.md), embedded for offline About.

## Python reference: source-family analysis

An additional analysis supplied by the owner suggested a mixture of standard API/tutorial
patterns, local changes and possible iterative AI assistance. The following ten observations
retain that report's substance while separating it from verified evidence. On 6 October
2026, bounded read-only inspection confirmed the API constructions below in the original
archive; current primary documentation was checked for their technical meaning. Neither
inspection nor API similarity proves copying, individual authorship or permission.

1. **Scapy ARP discovery — core.py.** The source builds a broadcast Ethernet/ARP request,
   calls srp and reads returned IP/MAC fields. This matches the documented
   [Scapy ARP-ping construction](https://scapy.readthedocs.io/en/stable/usage.html#arp-ping).
   The supplied report calls the example resemblance very strong. The API pattern is
   verified; use of a particular tutorial or copied expression is not.

2. **OpenHardwareMonitor GPU sensors — rightpanel.py.** The source queries
   root\OpenHardwareMonitor, enumerates Sensor objects and checks type/name for GPU
   temperature/load. The project has a documented
   [WMI implementation](https://github.com/openhardwaremonitor/openhardwaremonitor/tree/master/WMI).
   The supplied report proposes a **20 February 2018 Python gist** as a strong source-family
   match. Its exact URL/version was not supplied or verified; no direct-source attribution
   or historical gist date is established.

3. **Windows ping sweep — scanner.py.** The source requests one ping, a 200 ms timeout,
   and checks TTL text. [Microsoft's ping reference](https://learn.microsoft.com/en-gb/windows-server/administration/windows-commands/ping)
   documents count/timeout options. The report's proposed **2012–2014 StackOverflow**
   ancestry has no exact links or dated snapshots and remains unverified. Ordinary
   Windows command usage does not identify an author.

4. **Python TCP scanning — scanner.py.** Socket creation, settimeout, connect and close,
   including a 1–1024 port loop, are present.
   [Python socket documentation](https://docs.python.org/3.12/library/socket.html)
   explains those operations. The report describes a familiar tutorial family, while
   acknowledging no unique older full_scan source. No specific tutorial/repository or
   literal whole-function copy is verified.

5. **NumPy/pyqtgraph histogram — rightpanel.py.** The source uses a 40-bin histogram over
   packet sizes 0–1500 and sends edge/count arrays to a step plot. The
   [pyqtgraph 0.13.4 contract](https://pyqtgraph.readthedocs.io/en/pyqtgraph-0.13.4/api_reference/graphicsItems/plotcurveitem.html)
   requires one more x edge than y values for centered step mode. This explains the
   41/40 array-shape comment; the report's example-origin inference remains unproven.
   Matching an API-required shape is not evidence of copied text.

6. **Qt protocol pie drawing — ProtocolPieChart.** The code multiplies degree angles by
   16 when calling drawPie. [Qt's QPainter reference](https://doc.qt.io/qt-6/qpainter.html#drawPie)
   specifies sixteenths of a degree. This is an API requirement and supports the technical
   explanation, rather than proving which documentation/example the author used.

7. **IP-API GeoIP — GeoIPPanel.** The Python source explicitly addresses ip-api.com's JSON
   endpoint and selects fields. The
   [provider's JSON API reference](https://www.ip-api.com/docs/api%3Ajson)
   confirms the endpoint/field interface. Service usage is observed; copied implementation,
   service entitlement and authorship are separate questions. This is the Python reference's
   provider, not a claim that Community's ipapi.co or Core's disabled GeoIP path uses it.

8. **psutil endpoints/process names — PortActivitySummary.** net_connections(kind='inet'),
   local/remote addresses, state, PID and Process(pid).name() appear in the source.
   [psutil documentation](https://psutil.readthedocs.io/stable/index.html)
   defines that model. The report itself treats this as an API pattern, not proven
   copy-and-paste; using documented fields cannot identify the implementation's author.

9. **Windows adapter/GUID mapping — core.py.** get_if_list, NPF-prefixed device names
   and Get-NetAdapter.InterfaceGuid queries are present.
   [Scapy's Windows source](https://github.com/secdev/scapy/blob/master/scapy/arch/windows/__init__.py)
   contains GUID/NPF mapping, while
   [Microsoft's adapter reference](https://learn.microsoft.com/en-us/powershell/module/netadapter/get-netadapter?view=windowsserver2025-ps)
   documents adapter/CIM inventory. The report suggests a combination of Windows examples
   and Scapy troubleshooting. A particular 2019 issue or older Microsoft snippet was not
   verified as the original source.

10. **Windows Firewall logging — rightpanel.py.** The source invokes
    Set-NetFirewallProfile with LogAllowed/LogBlocked flags.
    [Microsoft's reference](https://learn.microsoft.com/powershell/module/netsecurity/set-netfirewallprofile)
    documents those controls. This is a verified API-family observation, without proving
    a copied routine, permission to change a user's machine or individual authorship.

### Search and AI hypotheses

The supplied report says it did not find a public repository containing the whole application
or public matches for names including VizisikiUI, UltraRightPanel, PacketHexInspector,
HeatmapEngine and SecurityEngine, and distinctive build/repair labels. Exact queries, complete results and
dated snapshots were not supplied. That is a reported search outcome, not independently
reproduced proof that no upstream project exists.

The supplied analysis describes AI involvement as highly likely; this review cannot assign
that probability. Its examples include FINAL FIX / clean-build labels, an emphatic Nmap
safe-mode comment and the histogram-update wording. It also cites unfinished branches and
protocol guesses from port numbers as assembly clues; these observations concern development
style/heuristic limits and do not establish the development tool or author.

The report interprets repeated imports, emphatic fix/version comments, mixed Hungarian/English
text and assigning functions to VizisikiUI methods after class definition as signs of iterative
LLM-assisted assembly. Repeated imports, the method assignments and a forceful histogram-update
comment are visible in the inspected source. Such patterns also occur in human patching and
experimentation: **AI use in the original Python project remains an unverified hypothesis**.
No AI provider, probability, conversation, generation date or per-line authorship is established.
Gábor separately acknowledges AI coding assistance in Pulsatilla's redevelopment.

The archive's November 2025 date labels are a saved-snapshot clue. They do not independently
date first authorship, so the supplied report's exclusion of later search results is a
chronology assumption, not a complete source-provenance proof. Current documentation confirms
API behavior, not that today's page/version existed or was read before those stored dates.

The ten observations do not establish infringement, ownership transfer or commercial clearance.
Credits remain based on the owner's account. The detailed
[Python reference analysis](PYTHON-REFERENCE-ANALYSIS.md) records scope and limitations;
this substantive summary is included here so it is available in the application's offline About.

## Owner-supplied delivery and iteration evidence

Gábor reports receiving the original Python source through Facebook. The owner-supplied
screenshot shows a Windows calendar date of **5 October 2026** and these visible labels:

| Visible item | Displayed time | Displayed size |
| --- | --- | --- |
| Received netw_tool_v05.zap | approximately 13:36 | 87.18 KB |
| Returned NetWTool.zop | 15:16 | 105.04 KB |
| Returned NetWTool.zop | 15:33 | 123.97 KB |
| Returned NetWTool.zop | 16:17 | 136.05 KB |

The .zap/.zop spellings and KB labels are recorded as supplied, without silently normalizing
them to .zip or verifying what those files contain. The image supports the owner's account
of source delivery and several visible returns; its underlying message metadata, timezone,
file identities/hashes, participants and unseen conversation were not independently verified.
The approximate 87.18 KB display is consistent with the inspected ZIP size after binary-unit
rounding but does not identify that file conclusively. No private screenshot or Messenger URL
is redistributed here.

This does not replace the canonical **2026-10-05T13:55:00+02:00** Pulsatilla family start.
Returns/file sizes do not log active work, establish which changes were present, or grant
commercial reuse rights. There is no inferred assignment or license from Facebook delivery.
