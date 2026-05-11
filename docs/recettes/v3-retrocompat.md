# Recette V3 — Rétrocompatibilité V1 + V2 + V3

ClickUp task: `[Recette V3] Rétrocompat V1 + V2 + V3` — P4 Finition V3
Tags: `grille-tuteur`, `recette-v3`, `cross-team`

## Goal

Verify that the v3 release does not break any persistence format, public
API or end-user feature that v1.x and v2.x operators rely on. The cahier
explicitly freezes `EasyLog.dll`'s v1.0 public API and the existing
`appsettings.json` / `state.json` shapes; v3 adds fields **additively** —
this recette is the cross-team check that the addition stays additive
under real-world inputs.

## How additive evolution is preserved

- **`AppSettings`** properties are declared with `init` accessors and
  default values (`src/EasySave/Models/AppSettings.cs`). `System.Text.Json`
  populates the defaults when a field is absent in the JSON payload — so
  a v1 / v2 `settings.json` loads cleanly under the v3 engine.
  Defaults of interest: `LargeFileThresholdKb = 4096`,
  `RemoteConsoleEnabled = false`, `RemoteConsolePort = 9000`,
  `MaxParallelJobs = 4`.
- **`state.json`** has been a JSON array (`List<StateEntry>`) since
  v1.0.0 (`src/EasySave/Services/StateTracker.cs:254`). v2 typically
  ends up with **one** entry per run because jobs are sequential; v3
  can have **N** entries simultaneously when the parallel orchestrator
  is wired. The on-disk schema is identical — no migration code needed.
- **`LogEntry.EventType`** is `LogEvent?` with
  `[JsonIgnore(Condition = WhenWritingNull)]` and
  `[JsonConverter(JsonStringEnumConverter)]`. v1 / v2 entries leave it
  null, the JSON / XML output omits the field, and existing readers see
  the exact byte shape they had.
- **`EasySave.Schemas.easysave-log.xsd`** declares the new `EventType`
  element with `minOccurs="0"`, so the v2 daily logs (no `EventType`)
  still validate against the v3 schema.

## Pre-requisites

- Build the solution once: `dotnet build EasySave.sln`.
- Have one v2.x backup of test data available (a job with ≥ 50 small
  files so the run is observable, and a `state.json` / daily log
  produced by a v2 build). If you don't, the recette walks through
  fabricating an equivalent input.
- Have CryptoSoft binary available (`src/Tools/CryptoSoft/...` or the
  path you configured in v2). For scenario 4 — skip if CryptoSoft is
  not on the operator's machine.

## Scenarios

Each scenario uses an empty result table at the bottom — fill it in
during the manual run and paste the screenshot link if relevant.

### 1. Vieux `settings.json` (sans champs V3) charge sans erreur

| Step | Action | Expected |
|---|---|---|
| 1.1 | Stop the engine. Locate `settings.json` (next to `state.json`: `%AppData%\ProSoft\EasySave\settings.json` on Windows, `~/.config/ProSoft/EasySave/settings.json` on Linux/macOS). | — |
| 1.2 | Replace it with a v2 payload that has **no** `large_file_threshold_kb`, **no** `remote_console_enabled`, **no** `remote_console_port`, **no** `max_parallel_jobs`. Example: `{"language":"fr","encrypted_extensions":[".pdf"],"business_software":["calc.exe"],"log_format":"json","crypto_soft":{"path":"","timeout_ms":30000}}`. | — |
| 1.3 | Launch the engine: `dotnet run --project src/EasySave.UI`. | Window opens normally — no `JsonException` in stderr, no crash. |
| 1.4 | Open **Settings** in the GUI. | All v2 fields show their saved values (`encrypted_extensions`, business software, etc.). |
| 1.5 | Inspect `settings.json` after the GUI Settings screen re-saves (if needed). | New v3 fields now appear with their defaults: `"max_parallel_jobs": 4`, `"remote_console_enabled": false`, `"remote_console_port": 9000`, `"large_file_threshold_kb": 4096`. |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 2. `state.json` legacy mono-entrée lu correctement

| Step | Action | Expected |
|---|---|---|
| 2.1 | Stop the engine. Locate `state.json` and replace it with a v2-shaped payload (single entry): `[{"Name":"legacy","LastActionTime":"2026-05-01T08:00:00+02:00","State":1,"TotalFilesEligible":10,"TotalSize":1024,"FilesRemaining":0,"SizeRemaining":0,"CurrentSource":"","CurrentTarget":"","PauseReason":""}]`. | — |
| 2.2 | Launch the engine. | No JSON parse error. The single entry is preserved on disk until the first state write. |
| 2.3 | Run any job (`Run` on a configured job). | The new run **adds** an entry for that job. After the run, `state.json` contains both the `legacy` entry (intact, never modified because no job named `legacy` ran) **and** the entry for the just-ran job. The format stays a JSON array (no dict conversion, no schema change). |
| 2.4 | Re-run the same job. | Existing entry for that job is replaced (single entry per job name, case-insensitive); the `legacy` entry stays intact. |

> Note: the task description mentions "migration vers dict multi-jobs" — there is **no migration**. v1/v2/v3 all store `state.json` as a JSON array; multiple entries coexist naturally because that's what the format always supported.

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 3. Job V2 séquentiel : un seul job fonctionne identique à v2

| Step | Action | Expected |
|---|---|---|
| 3.1 | Configure one Full job (source ~50 MB, target on an external drive or network share). | — |
| 3.2 | Run it. | Job progresses normally; FilesLeft decreases monotonically; `state.json` shows it `Active` then `Inactive`. |
| 3.3 | Inspect the daily log entry per file. | All v1 fields present (`Timestamp`, `JobName`, `SourceFile`, `TargetFile`, `FileSize`, `FileTransferTimeMs`). `EventType` is **absent** from these rows because v2-style file copies leave it null. |
| 3.4 | Source paths in the log. | UNC format on Windows (`\\?\` prefix for local paths) — same as v1 / v2. |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 4. CryptoSoft + extensions chiffrées

| Step | Action | Expected |
|---|---|---|
| 4.1 | Settings GUI → set `crypto_soft.path` to the CryptoSoft binary. Add `.pdf` (and any other test extension) to `encrypted_extensions`. Save. | — |
| 4.2 | Run a job whose source contains both `.pdf` files and non-`.pdf` files. | `.pdf` files routed through CryptoSoft (encryption time logged in `EncryptionTimeMs`), other files copied normally (`EncryptionTimeMs` absent from JSON / XML on those rows). |
| 4.3 | Inspect the daily log entry for a `.pdf` file. | `EncryptionTimeMs` present and > 0 on success, < 0 on failure (cahier signal). `FileTransferTimeMs` reflects the wrapping copy time. |
| 4.4 | Run again. The target `.pdf` already exists. | Differential strategy: file size differs from source (encrypted), but the mtime alignment lands the source mtime on the target on the first copy, so subsequent runs skip it (Diff = same mtime → no copy). |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 5. Switch FR/EN runtime

| Step | Action | Expected |
|---|---|---|
| 5.1 | Launch the GUI in EN. | Sidebar in English. |
| 5.2 | Click **FR** in the sidebar language toggle. | All sidebar labels flip to French immediately (`Sauvegardes`, `Paramètres`, ...) — no restart needed. |
| 5.3 | Open **About** modal, leave it open, click **EN**. | About modal flips back to English at the same time as the main window. |
| 5.4 | Close the app, relaunch. | App opens in the last-selected locale (persisted in `settings.json` → `language`). |

> This scenario is identical to the existing v2 recette
> (`docs/recettes/v2-language-runtime-switch.md`). The v3 changes touch
> none of `LanguageService`, `TranslationSource`, or the `{markup:T}`
> XAML extension — the test is here to prove regression-free, not to
> exercise new code.

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 6. Logs XML + JSON V2 toujours valides avec champs V3

| Step | Action | Expected |
|---|---|---|
| 6.1 | Settings → `log_format: "json"`. Run a job. | Today's `<yyyy-MM-dd>.json` is a valid JSON array. v2 readers (any consumer that parses `List<LogEntry>` without `EventType`) load it cleanly — the v3 field is **absent** from rows that didn't tag an event. |
| 6.2 | Trigger a V3-tagged event (start the remote console server if wired, connect a client, send a command). | A new row in the daily log carries `"EventType": "CommandReceived"` (or `"RemoteConsoleConnected"`, etc.). v2-shape rows in the same file stay unchanged. |
| 6.3 | Settings → `log_format: "xml"`. Restart, run a job. | Today's `<yyyy-MM-dd>.xml` is a `<Logs>` root with `<Entry>` children. Validate against the embedded XSD (`EasyLog.Schemas.easysave-log.xsd` — accessible via `XmlFormatter.LoadSchema()`). |
| 6.4 | Repeat 6.2 in XML mode. | A V3-tagged `<Entry>` carries a `<EventType>BigFileEnqueued</EventType>` (or similar) child. The XSD's `LogEventName` `xs:string` enumeration validates the element value. v2-shape entries without `<EventType>` still validate because the XSD has `minOccurs="0"`. |
| 6.5 | Take a v2-produced daily log file (no `EventType` field / element anywhere) and validate it against the v3 XSD. | Validation passes. v2 → v3 forward compat preserved. |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

## Out of scope

- v3 → v2 forward compat (running a v3 daily log through a v2 reader):
  v2 readers ignore unknown JSON fields by default
  (`System.Text.Json`); v2 XML readers strict-validating against the v2
  XSD would fail on `<EventType>` because the v2 XSD doesn't list it.
  Operators downgrading to v2 are expected to delete the daily file
  with v3 markers and start fresh — out of scope for the cahier.
- Migrating an **encrypted** v2 backup with a different CryptoSoft
  version installed — covered by the CryptoSoft compatibility tests
  inside `docs/cryptosoft-integration.md`.

## Sign-off

| Tester | Engine OS | All 6 scenarios pass? | Date |
|---|---|---|---|
| ____ | ____ | ☐ Yes ☐ No | ____ |

If any scenario fails, file an issue with the scenario number, the
screenshot, and the relevant excerpts from `settings.json` /
`state.json` / the daily log. Tag the issue with `recette-v3` and
the suspected zone.
