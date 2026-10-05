# Development setup

## Build and checks

Use Windows with the .NET 8 SDK. `powershell -NoProfile -File tools/Validate.ps1` restores,
builds Release and runs the synthetic protection/presentation and localization checks.
`-Offline` disables the NuGet vulnerability-audit request for restricted-network machines;
an installed SDK/targeting pack and cached dependencies are still required. CI uses normal auditing.

`tools/Publish.ps1` creates the portable Windows ZIP, documents, metadata and checksums.
It refuses to overwrite an existing release and restricts output to this project's `dist`.
Signing credentials belong outside the repository. No key or signing identity is configured.

## Ruflo MCP

Ruflo is an optional development tool and is not shipped with Pulsatilla. This machine uses
an isolated installation in `%USERPROFILE%\.codex\tools\ruflo`: the `ruflo` launcher is
3.38.23 with dependencies resolved in its npm lockfile. The Codex MCP entry invokes the
installed local JavaScript entry through Node, with a 120-second startup/tool timeout.
Existing Codex settings and other servers are retained. No API key was added.

`node tools/Ruflo.Check.mjs` performs an MCP handshake, discovers tools and calls the real
system-health check. `--remember` additionally stores/retrieves the public product decisions
in the local `pulsatilla` memory namespace. Native MCP tools become visible when Codex loads
the updated configuration; restart the Codex extension/session after changing MCP configuration.
This protocol check also works during a session whose initial tool catalog predates installation.

State in `.claude-flow`, `.swarm` or `.ruflo` is local and ignored by Git. The project uses
runtime-only initialization; no broad automatic edit hooks or background agents are enabled.
Keep private captures, real email contents, watched email lists and credentials out of memory.
`PULSATILLA_RUFLO_ROOT` can point the checker to another isolated installation directory.

Other developers can install the pinned launcher separately with:

```powershell
npm install --prefix "$env:USERPROFILE\.codex\tools\ruflo" --ignore-scripts --no-audit --no-fund --save-exact ruflo@3.38.23
```

Register its local `node_modules/ruflo/bin/ruflo.js mcp start` command with Codex, using the
actual absolute Node/install paths on that machine. Consult [OpenAI's MCP documentation](https://developers.openai.com/codex/mcp/)
for current configuration behavior. Initialization is per project; installing the launcher
does not by itself enable every optional Ruflo plugin.

## Standalone GitHub repository

Use the `pulsatilla` folder as the repository root, not the surrounding `_gabor-web` collection.
The local preparation does not push code or create a public release. Before the first public
push, review tracked files, enable private vulnerability reporting and add the actual repository
URL to release metadata if desired. Do not publish local profile data or development memory.
The Windows workflow builds and packages the checked commit as an artifact; it does not publish
a GitHub Release automatically. Dependabot keeps the pinned GitHub Actions revisions reviewable.

Windows may keep the old workspace directory locked while an IDE or terminal still uses it.
After a content-preserving transfer to `pulsatilla`, `tools/FinishFolderRename.ps1` can remove
only the empty sibling `netw_tool_v05` once its lock is released. It never removes a folder
that contains files and never terminates an IDE or terminal. Reopen the IDE from the new root.
