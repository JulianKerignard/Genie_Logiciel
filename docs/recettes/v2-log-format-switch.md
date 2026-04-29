# Recette V2 — Log : EncryptionTimeMs présent + format XML vs JSON switchable

ClickUp task: `869d036gw` — `[Recette V2] Log : EncryptionTimeMs présent + format XML vs JSON switchable`
Tags: `grille-tuteur`, `recette-v2`, priority **urgent**

## Goal

Verify the v2.0 grille items "log journalier au format XML" and "temps de
cryptage" end-to-end:

- A run with `log_format = "json"` produces `Logs/yyyy-MM-dd.json` where
  encrypted files carry `EncryptionTimeMs > 0` and plain copies omit the
  field (v1 byte-shape compat).
- A run with `log_format = "xml"` produces `Logs/yyyy-MM-dd.xml` valid
  against the embedded XSD with `EncryptionTimeMs` present where
  applicable.
- Both files coexist in the same directory — switching format does not
  erase the previous day's data.

## Programmatic evidence

Locked at the EasyLog level on every CI build:

- `JsonDailyLoggerEncryptionTests` (4 cases): `EncryptionTimeMs` survives
  `Append`, the v1 file shape is preserved when null, multiple appends
  accumulate.
- `XmlDailyLoggerTests` (7 cases, added in this PR): daily file extension,
  `EncryptionTimeMs` omit/include, append-only across calls, XSD
  validation of the produced document, corrupted-file quarantine, and
  JSON/XML coexistence in the same directory.
- `JsonFormatterTests` and `XmlFormatterTests` cover the formatter level
  including v1-reader compat and special-character escaping.

## Pre-requisites

- Build the UI: `dotnet build src/EasySave.UI/EasySave.UI.csproj`.
- A working CryptoSoft binary, with its absolute path either in
  `appsettings.json:crypto_soft.path` or set later via the Settings GUI.
- A source directory containing at least one **encryption-eligible**
  file (default extensions: `.docx`, `.pdf`, `.xlsx`) AND one plain file
  (e.g. a `.txt` outside the encrypted-extensions list).
- A writable target directory.
- For a clean reading, delete the today's daily files in the user log
  directory before each run:
  - Windows: `%APPDATA%\ProSoft\EasySave\Logs\`
  - Linux: `~/.config/ProSoft/EasySave/Logs/`
  - macOS: `~/Library/Application Support/ProSoft/EasySave/Logs/`

## Manual scenario

### Phase 1 — JSON format

1. Launch the GUI: `dotnet run --project src/EasySave.UI`.
2. Open **Settings**, set `Log Format` to `json`, click **Save**, close
   the app.
3. Inspect `settings.json`: `log_format` must be `"json"`.
4. Relaunch the app. Open **Backups**, define a job pointing at the
   source/target prepared above.
5. Run the job and wait for completion.
6. Open `Logs/yyyy-MM-dd.json` and confirm:
   - The encryption-eligible file's entry has `EncryptionTimeMs > 0`.
   - The plain file's entry has **no** `EncryptionTimeMs` field at all
     (omitted, not `null`).
   - Every entry has the v1 fields: `Timestamp`, `JobName`, `SourceFile`,
     `TargetFile`, `FileSize`, `FileTransferTimeMs`.

### Phase 2 — XML format

7. Reopen the GUI. Open **Settings**, switch `Log Format` to `xml`,
   click **Save**, close the app.
8. Inspect `settings.json`: `log_format` must be `"xml"`.
9. Relaunch the app and run the same job again.
10. Confirm `Logs/yyyy-MM-dd.xml` has been created **alongside** the
    `.json` file from Phase 1 (no overwrite).
11. Open `Logs/yyyy-MM-dd.xml` and confirm:
    - Root element `<Logs>` with one `<Entry>` per logged operation.
    - The encryption-eligible file's entry carries `<EncryptionTimeMs>`
      with a positive integer.
    - The plain file's entry has **no** `<EncryptionTimeMs>` element.
12. Validate the file against the embedded schema (optional, command-line
    via xmllint or the .NET test suite already does this):
    ```bash
    xmllint --noout --schema docs/schemas/easysave-log.xsd "Logs/yyyy-MM-dd.xml"
    ```
    Expected output: `Logs/yyyy-MM-dd.xml validates`.

### Expected result

- Two daily files coexist in the log directory: one `.json`, one `.xml`.
- Both contain `EncryptionTimeMs > 0` for the encrypted file's entry.
- Both omit the field for the plain file's entry.
- The XML file passes XSD validation.

## Known scope

- Switching `log_format` requires restarting the app. The `IDailyLogger`
  is a singleton snapshotted at startup; live-reload would require
  exposing the logger behind a getter. Documented for the team — not
  in scope for v2.0.
- `appsettings.json` ships `log_format: "json"` as the default. The
  Settings GUI persists the user choice into `settings.json` which
  takes precedence on next launch.

## Result (filled by tester)

| Field | Value |
|---|---|
| Tester | _to fill_ |
| Date | _to fill_ |
| App version | _v2.0-staging @ <commit>_ |
| Operating system | _to fill_ |
| CryptoSoft path | _to fill_ |
| JSON Phase outcome | ☐ PASS  ☐ FAIL |
| XML Phase outcome | ☐ PASS  ☐ FAIL |
| Coexistence verified | ☐ Yes  ☐ No |
| XSD validation | ☐ PASS  ☐ FAIL |
| Notes | _to fill_ |

If any phase fails, attach the relevant daily log file and the
`settings.json` content to the ClickUp task.
