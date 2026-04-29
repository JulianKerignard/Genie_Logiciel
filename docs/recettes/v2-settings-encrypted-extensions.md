# Recette V2 — Settings UI : éditer encrypted_extensions → fichier crypté

ClickUp task: `869d036ce` — `[Recette V2] Settings UI Avalonia : éditer encrypted_extensions → fichier crypté`
Tags: `grille-tuteur`, `recette-v2`, priority **urgent**

## Goal

Verify the end-to-end chain "GUI Settings → settings.json → BackupManager
encrypts the matching files via CryptoSoft" so the v2.0 grille item
"gestion des paramètres généraux" passes.

Until this task, `SettingsViewModel.Save` was a TODO and the GUI never
wrote to `settings.json`. The wiring lives in this PR; the manual recette
below validates the full chain.

## Pre-requisites

- A working CryptoSoft binary (built from the team CryptoSoft repo).
- A path to that binary configured **either** in `appsettings.json`
  under `crypto_soft.path` (initial seed) **or** entered later via the
  Settings GUI file picker.
- A source directory containing **at least one `.txt` file** of any
  size, plus a writable target directory.
- `settings.json` reset between runs if you want a clean state. It lives
  next to the other user files:
  - Windows: `%APPDATA%\ProSoft\EasySave\settings.json`
  - Linux: `~/.config/ProSoft/EasySave/settings.json`
  - macOS: `~/Library/Application Support/ProSoft/EasySave/settings.json`

## Manual scenario (Avalonia GUI)

### Steps

1. Launch the GUI: `dotnet run --project src/EasySave.UI`.
2. Open the **Settings** tab.
3. Confirm the screen lists the seeded encrypted extensions from
   `appsettings.json` (e.g. `.docx`, `.pdf`).
4. Type `.txt` in the "Add extension" field and click **Add** — the entry
   appears in the list.
5. Click **Save**.
6. Stop the application (close the main window).
7. Inspect `settings.json` in the user data directory listed above and
   confirm `encrypted_extensions` now contains `.txt` alongside the
   previously seeded values.
8. Define a backup job (`Full`) pointing to your test source/target.
9. Relaunch the GUI: `dotnet run --project src/EasySave.UI` (the
   restart is required — `BackupManager` snapshots the encrypted-extensions
   list at startup).
10. Run the job from the **Jobs** tab and wait for completion.
11. Open today's daily log file in the configured log directory.

### Expected result

- `settings.json` contains `.txt` in `encrypted_extensions` after step 7.
- After the run, the daily log entry for the `.txt` file contains a
  positive `EncryptionTimeMs` (i.e. the file went through CryptoSoft).
- Non-eligible files (e.g. a `.bin` next to the `.txt`) have **no**
  `EncryptionTimeMs` field, mirroring the v1 byte-shape contract.

## Known limitation

GUI edits saved during runtime do not affect the running `BackupManager`
because the encrypted-extensions list is captured once at startup. A
restart is required for changes to take effect. This is documented in
the GUI Settings screen blurb. If the team later wants live-reload, the
fix is to expose the list through a getter on `SettingsRepository` and
read it on every `BackupManager.RunJob` call.

## Result (filled by tester)

| Field | Value |
|---|---|
| Tester | _to fill_ |
| Date | _to fill_ |
| App version | _v2.0-staging @ <commit>_ |
| Operating system | _to fill_ |
| CryptoSoft path | _to fill_ |
| Outcome | ☐ PASS  ☐ FAIL |
| Notes | _to fill_ |

If the outcome is FAIL, attach the daily log file content for the run
and the resulting `settings.json` to the ClickUp task.
