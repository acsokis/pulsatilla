# Pulsatilla development

The desktop application is C# / WPF on .NET 8, MIT-licensed, with optional paid services
kept separate. Run `tools/Validate.ps1` for the Release build and synthetic checks.
Localization feature work is paused; preserve existing resources when changing branding.

Keep the current model unless the user explicitly asks to change it.
Do not put personal traffic, email bodies or credentials into development logs.

Public product documentation describes implemented Pulsatilla behavior and limitations;
omit competitor comparisons, rankings, promotional competitor links and unverified guarantees.
Preserve creator attribution. Profile migration is copy-only and may not overwrite an
existing profile. Firewall actions require the existing explicit UI confirmation.

## Development policy

Use one primary agent and targeted deterministic tools for routine work.
Keep reads and logs bounded; run relevant local build/check scripts.
Paid API use needs explicit owner opt-in. Preserve existing authentication and model.
Do not launch billed autonomous workflows or add an orchestration runtime dependency.
