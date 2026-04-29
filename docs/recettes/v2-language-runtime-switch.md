# Recette V2 — Switch langue FR↔EN runtime sans restart

ClickUp task: `869d036e7` — `[Recette V2] Switch langue FR↔EN runtime sans restart`
Tags: `grille-tuteur`, `recette-v2`

## Goal

Verify that EasySave honors the v2.0 grille requirement
"locale switch without restart": the user toggles FR↔EN at any time and
every UI string updates immediately, including modals already open.

## How the runtime switch works

- `{markup:T Key=foo}` (custom XAML extension) returns a OneWay `Binding` to
  `TranslationSource.Instance[foo]`.
- `LanguageService.SetLanguage(locale)` reloads the JSON resource and raises
  `LanguageChanged`.
- `TranslationSource` translates that into `PropertyChanged("Item[]")`,
  which invalidates every binding registered against the singleton —
  including the ones owned by modal windows opened earlier.
- The chosen locale is saved to `settings.json` (`language` key) so the
  next launch opens in the user's preference instead of the FR default.

## Pre-requisites

- Build the UI once: `dotnet build src/EasySave.UI/EasySave.UI.csproj`.
- For a clean reading, delete or rename `settings.json`. The file lives
  next to the other user data:
  - Windows: `%APPDATA%\ProSoft\EasySave\settings.json`
  - Linux: `~/.config/ProSoft/EasySave/settings.json`
  - macOS: `~/Library/Application Support/ProSoft/EasySave/settings.json`
- Note: with `settings.json` absent, the app starts in **English** —
  `appsettings.json` ships `"language": "en"` and seeds `settings.json`
  on first run. To start in French, edit the seeded `settings.json` and
  set `"language": "fr"` before launching, or just toggle once via the
  sidebar.

## Manual scenario

### Steps

1. Launch the GUI: `dotnet run --project src/EasySave.UI`.
2. Click **FR** in the bottom-left language toggle.
3. **Critical:** confirm every sidebar label flips to French immediately
   (`Sauvegardes`, `Paramètres`, `Journaux`, `À propos`) — no flash, no
   restart.
4. Click `À propos` to open the modal. Confirm the text is in French.
5. Without closing the modal, switch back to **EN** in the sidebar.
6. **Critical:** confirm the open About modal flips to English at the
   same time as the main window (single shared `TranslationSource.Instance`).
7. Close the About modal. Open **Backups** → **Add Job** to reach the
   JobEdit view. Repeat the FR↔EN toggle and confirm `edit.name`,
   `edit.source`, `edit.destination`, `edit.cancel`, `edit.save` all
   refresh immediately.
8. Close the app. Inspect `settings.json` — the `language` key must hold
   the last locale you selected (`fr` or `en`).
9. Relaunch the app. The window must open in the persisted locale, not
   the `appsettings.json` seed.

### Expected result

- Every visible label updates within the same UI tick on toggle, no flash,
  no restart.
- The About modal and JobEdit view follow the toggle exactly like the
  main window — no orphaned French text in an English UI or vice versa.
- `settings.json` reflects the last selected locale after closing the app.
- The app reopens in that locale on next launch.

## Known scope

- The toggle is in the main window sidebar, not inside the Settings tab.
  This is intentional — it keeps the locale switch one click away from
  any screen instead of buried in Settings. The recette spec wording
  ("Settings → Language") is loose; the deliverable is the runtime switch
  behavior, not a specific menu path.
- Placeholder texts in `JobEditView.axaml` (e.g. example paths in the
  `TextBox.PlaceholderText` attribute) are not localized. They are
  illustrative only and disappear once the user types.

## Result (filled by tester)

| Field | Value |
|---|---|
| Tester | _to fill_ |
| Date | _to fill_ |
| App version | _v2.0-staging @ <commit>_ |
| Operating system | _to fill_ |
| Outcome | ☐ PASS  ☐ FAIL |
| Open-modal switch worked | ☐ Yes  ☐ No |
| Persistence across restart worked | ☐ Yes  ☐ No |
| Notes | _to fill_ |

If the outcome is FAIL, attach a screenshot of the affected window and
the resulting `settings.json` content to the ClickUp task.
