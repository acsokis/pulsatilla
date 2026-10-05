# Interface translation catalog

Each UTF-8 JSON file maps the original English source text to its translation.
`en.json` is the canonical source catalog. Keep keys case-sensitive: `SENT` and `Sent`
are separate entries. Use a JSON parser that preserves case-sensitive keys when editing
programmatically (Windows PowerShell 5.1 `ConvertFrom-Json` does not).

Preserve every format placeholder exactly, including its format suffix, e.g.
`{0:N0}` or `{0:HH:mm:ss}`. You may reorder placeholders for natural grammar.
Use `\n` for line breaks. Keep product names, protocols, addresses and units intact.
Missing translation entries fall back to English. Additional locales need a native
display name and culture code in `LocalizationService.Languages`; the project embeds
all JSON files automatically.

XAML text uses dynamic application resource keys: `Loc_` plus the first 12 uppercase
hexadecimal characters of the SHA-256 hash of the English UTF-8 source. The service
computes these with `ResourceKey(source)` and updates resources at language changes.
Data-grid columns instead use `local:LocalizationService.Source="English header"`
alongside an initial English `Header`, because columns do not inherit application
resource lookup context reliably. Runtime UI messages use `Translate(source)` or
`Format(FormattableString)` before assigning a control value; avoid translating
interpolated values after formatting, since that loses the reusable translation key.

Coverage: `en`, `de`, `hu`, `fa`, `ur`, `hi` contain the whole 196-entry catalog.
The remaining packs contain 88 navigation/action entries and use English fallback
for the rest. System command output, service diagnostics and event explanations
are not part of this catalog. New runtime messages should also be added to the
source catalog and translation files.

Run `dotnet run --project tools/Localization.Checks/Localization.Checks.csproj -c Release`
from the repository root to verify embedded packs, source-key coverage, placeholders,
XAML resource references, dynamic label/header switching, formatted strings, fallback
and RTL layout selection. This check creates an unshown WPF window and does not
change saved language settings. Native-speaker review and visual review of every
screen are separate release checks.
