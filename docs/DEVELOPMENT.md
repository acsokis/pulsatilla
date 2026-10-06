# Development setup

## Build and checks

Use Windows with the .NET 8 SDK. `powershell -NoProfile -File tools/Validate.ps1` restores,
builds Release and runs the synthetic protection/presentation and localization checks.
`-Offline` disables the NuGet vulnerability-audit request for restricted-network machines;
an installed SDK/targeting pack and cached dependencies are still required. CI uses normal auditing.

`tools/Publish.ps1` creates the portable Windows ZIP, documents, metadata and checksums.
It refuses to overwrite an existing release and restricts output to this project's `dist`.
Signing credentials belong outside the repository. No key or signing identity is configured.

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
