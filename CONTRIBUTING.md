# Contributing to Pulsatilla

Install the .NET 8 SDK on Windows, then run `dotnet build Pulsatilla.sln -c Release`.
Run the two check projects under `tools/Protection.Checks` and `tools/Localization.Checks`.
Protection checks use synthetic data and do not change firewall rules or make network requests.

Keep changes focused. Explain the user-visible behavior, limitations and verification in pull requests.
Use synthetic packet/email fixtures; do not commit personal logs, real email bodies, passwords,
payment tokens, private keys or breach datasets. Public product documentation must describe
implemented behavior without competitor rankings, endorsements or unverified protection claims.

Preserve MIT notices and creator attribution. Contributions submitted for inclusion are provided
under this repository's MIT license. Paid backend integrations must be optional, independently
consented and isolated from offline monitoring. See `docs/MONETIZATION.md` and `docs/PRIVACY.md`.

Localization is currently paused for feature development. Branding updates must still keep
resource keys consistent. Native-language editorial review remains welcome.
