# HEX, file and local email analysis

Community .NET 8 implements independent `HexPatternEngine`, `HexViewer`, `LocalFileInspector`, `FileInspectionPanel` and `EmailAttachmentsPanel` components. Packet, scanner, file and email contexts share the same matcher; no capture, network probe, browser or Defender scan is started by matching bytes.

## Rule contract and limits

Rules contain an ID, name, category, bytes, 00/FF wildcard mask, optional absolute offset, severity, description and context flags. Rule construction copies byte arrays. Rules are limited to 256 unique IDs, each 1–128 bytes, with bounded metadata. All-wildcard patterns are rejected. Built-ins identify common file prefixes and HTTP/SSH markers. A DOS `MZ` prefix is an executable candidate, not verification of a valid PE image.

Each analysis examines at most 65,536 bytes, returns at most 256 matches and performs at most eight million byte comparisons. Cancellation is checked between rules and every 256 candidate offsets. The result reports truncation when a sample, match or work budget is reached. A marker can occur in ordinary text; format matches and review indicators never establish an attack or safety.

## Reusable viewer

The WPF viewer presents 16-byte offset/HEX/ASCII rows using a recycling virtualized ListBox and a bounded preview. Matched bytes are highlighted; mouse and arrow keys select a byte and show its absolute preview offset and applicable rules. It never renders arbitrary HTML or executes sample content. Host it in a constrained Grid row rather than an unbounded outer scrolling panel. Left-to-right byte ordering remains intentional in RTL UI.

`FileInspectionPanel.SetSample(bytes, label, context)` accepts scanner/banner or packet preview data. `InspectPathAsync(path)` performs explicit file review. `HexViewer.SetBytes(sample, analysis)` provides direct packet integration. Previews are bounded independently of full file hashing.

## File inspection and signatures

On explicit selection, hashing streams a local file off the UI thread, with two concurrent workers, a 512 MiB ceiling and 128 cache entries. Cache identity uses normalized path, size and last-write time, with a five-minute lifetime. Network paths, mapped network drives and reparse-point ancestors are refused. Changing files are rejected; returned previews cannot mutate cache bytes. The SHA-256 is for the whole inspected file, while the HEX preview is only its first 64 KiB.

`WindowsSignatureInspector` uses the supported Windows `WinVerifyTrust` Authenticode action, closes trust state and restricts retrieval to cached local information. Status is Valid, Unsigned, Invalid or Unknown. The initial implementation verifies embedded signatures only; it does not claim catalog trust or fresh online revocation. Catalog-signed or unavailable offline-chain files may therefore remain Unsigned/Unknown. Publisher text is exposed only after embedded verification succeeds. A valid signature is not proof that software is safe.

Risk labels are advisory, with reasons: failed embedded trust, unsigned/unverified signature, or temporary-directory location. They do not diagnose malware. Observed application connection counts and events remain separate network observations rather than invented file reputation.

File panels use an operation identity as a result generation: replacing the sample, starting another inspection or unloading the panel invalidates previous callbacks even if a provider ignores cancellation. Completed operations dispose their own token sources; a cancelled cached inspection cannot return a result. Application signature/risk columns are explicitly labeled as the **last inspection** and show an inspection time; the UI does not continuously verify executable bytes.

`ApplicationTrafficPanel.Refresh(rows, sockets)` combines at most 8,192 retained captured flows and 8,192 owner-table rows into 256 executable/PID groups, reusing existing bound objects. Native owner identities take priority at the group cap so currently network-bound applications can appear while packet inspection is stopped. Duplicate native endpoint tuples and repeated capture row references are not double-counted. **Established TCP** requires a TCP row explicitly marked Established/active; **bound UDP endpoints** is a separate count and never called a connection count. Listening/closing/unknown TCP states do not count as established. These counts describe retained bounded native rows, not a guarantee of complete system socket coverage.

Captured flow counts and download/upload bytes remain solely sums of captured observations; native rows do not invent traffic volumes. A native-only application therefore has zero **captured** bytes, unknown country, unqueried configured policy and uninspected signature/hash/risk until a user chooses inspection. Up to 12 observed remote addresses/countries are shown. First/last timestamps represent local observation and are preserved across row refreshes, not process launch times. Policy buttons emit requests to the existing MainWindow confirmation/firewall path; this control does not change Windows rules itself. Unknown ownership stays unattributed and geographic information is not inferred. This is session/bounded-window data, not lifetime application traffic.

## Local email attachments

The existing parser retains its 128,000-character input, 64-part and depth-six bounds. From, Reply-To, Return-Path, Received and Authentication-Results are available; imported authentication headers are explicitly unverified. MIME attachments and named inline parts are decoded into local memory, at most 64 attachments and 128,000 aggregate decoded bytes. Invalid/unsupported encoding and omitted content generate warnings.

Each attachment supplies filename, extension, declared MIME, decoded size, SHA-256, bounded format-pattern results, entropy and up to 20 embedded URL strings. URLs are not followed. Decoded data is never written or executed. Authenticode stays Unknown for in-memory attachments because this workflow does not extract files. Double extensions and executable-format bytes add explained review indicators. Entropy is descriptive and not a maliciousness verdict. Nested archives, encrypted attachments, `.msg`, macros and mailbox authentication are not inspected.

## Threat intelligence and explicit Defender

`IThreatIntelProvider` separates local analysis from future external providers. No API key or automatic upload exists. Copy hash is local; opening a VirusTotal hash page requires confirmation and sends the selected hash through the user's browser.

The Defender adapter locates a bounded set of installed Microsoft Defender platform commands and accepts only a locally verified Microsoft embedded signature. It opens a read-only lock on the selected executable, re-verifies its exact Microsoft publisher before process creation and keeps that lock through command completion, preventing ordinary leaf-file writes/replacement in the verification/execution gap. This does not establish protection against a compromised administrator or changed parent-directory namespace.

An explicit confirmed custom scan uses literal `ArgumentList` arguments, `-Scan -ScanType 3 -File ... -DisableRemediation`, a two-minute wait limit and output draining without accumulating/logging contents. It does not alter Defender settings. Defender may use its configured cloud protection; the confirmation states this. Cancellation stops the owned command process but Windows Security remains authoritative for actual service scan state and results. Exit codes are shown without asserting a clean file. Command presence or signature does not prove the Defender service is enabled.

Primary API documentation: [Windows trust verification](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust), [Defender command-line scanning](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus).

## Validation and limits

Run `dotnet run --project tools/Analysis.Checks -c Release`. Synthetic fixtures cover masks, offsets, contexts, rule immutability, match/sample limits, cancellation, MIME metadata and decoding, whole-file hashes and cache invalidation, no-consent Defender behavior, and offscreen reusable controls in dark/light colors. No real Defender scan, firewall operation, browser or upload runs in these checks. Real Windows Defender enabled/disabled behavior, policy-restricted systems, catalog trust, DPI and accessibility require separate manual validation.
