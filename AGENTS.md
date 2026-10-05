# Pulsatilla development

The desktop application is C# / WPF on .NET 8, MIT-licensed, with optional paid services
kept separate. Run `tools/Validate.ps1` for the Release build and synthetic checks.
Localization feature work is paused; preserve existing resources when changing branding.

For multi-file tasks or complex features, use ToolSearch to find Ruflo MCP tools when
that capability is exposed. Useful tools include `memory_search`, `memory_store`,
`hooks_route`, `swarm_init`, `agent_spawn` and `system_health`. Read intelligence pattern
suggestions when present. Use delegation when it actually helps the task.

Ruflo is a development tool, never an application runtime dependency. If native MCP tools
are not loaded in a session, `node tools/Ruflo.Check.mjs` checks the configured local
installation. Its optional `--remember` flag stores the public product decisions locally.
Do not route personal traffic, email bodies, watched addresses or secrets into development memory.
Keep the current model unless the user explicitly asks to change it.

Public product documentation describes implemented Pulsatilla behavior and limitations;
omit competitor comparisons, rankings, promotional competitor links and unverified guarantees.
Preserve creator attribution. Profile migration is copy-only and may not overwrite an
existing profile. Firewall actions require the existing explicit UI confirmation.
