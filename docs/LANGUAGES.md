# Language support / 语言支持

v0.1.3 supports 简体中文、繁體中文、English、日本語、Español、Português.

Click the globe next to the appearance icon. The first option follows the Windows display language; each other option uses its own native name. Changes apply immediately without restarting or discarding the selected GIF. The selected option is checked when the menu opens again.

The default is `system`. The app reads `GetUserDefaultUILanguage` rather than the region/number format. Traditional Chinese locales (Taiwan, Hong Kong, Macau and Hant) map to Traditional Chinese; other Chinese locales map to Simplified Chinese. English, Japanese, Spanish and Portuguese locales map to their respective language, including regional variants such as es-MX, pt-BR and pt-PT. Unsupported languages fall back to English.

A manual choice is saved to `%LocalAppData%\DuckDuckMove\preferences.json`. Choosing the first option restores system matching. Invalid or missing preferences fall back to system matching. If saving fails, the current window still changes language and displays a warning. Demo/render verification never modifies the user's preferences.

The app UI, tooltips, avatar operation messages and app-owned validation errors are translated. Windows UAC, native dialog buttons and original operating-system diagnostic text remain controlled by Windows. The MSI installation wizard currently remains Chinese; it does not set the app's language.

Translations live in `src/DuckDuckMove.App/Assets/translations.txt`, embedded in the EXE. Each line has six fields separated by `¦`: Simplified Chinese source, Traditional Chinese, English, Japanese, Spanish, Portuguese. Field order and existing source keys are stable; append new entries so XAML resource indices remain unchanged. Literal Windows diagnostics are retained for troubleshooting.

`--render` verifies all six menu choices, immediate UI updates, language matching, preference round-trip, damaged preference recovery, translation completeness and format placeholder consistency. It also renders each language without changing the account avatar or restarting the real Start menu.
