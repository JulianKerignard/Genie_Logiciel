# Recette V3 — Parallélisme (jobs simultanés)

ClickUp task: `[Recette V3] Parallélisme (3 jobs simultanés)` — P4 Finition V3
Tags: `grille-tuteur`, `recette-v3`, `dev2-backup`

## Goal

Verify that EasySave v3 runs multiple backup jobs concurrently — bounded by
`MaxParallelJobs` from `appsettings.json` — and that the orchestrator
isolates per-job lifecycle: a Pause / Stop / failure on one job leaves the
others untouched. Also confirm that `state.json` ends in a consistent
multi-job snapshot with timestamps that prove genuine concurrency.

## How the parallel runner works

- The engine hosts `ParallelBackupOrchestrator` (`src/EasySave/Services/`).
  Its constructor takes `maxParallelJobs` and creates a `SemaphoreSlim` of
  that size; jobs submitted beyond the cap wait for a slot to free up.
- Each running job gets its own `JobExecutionContext` (per-job
  `CancellationTokenSource` + `IDailyLogger` instance), so `Pause(name)`,
  `Resume(name)`, and `Stop(name)` only touch the targeted job.
- `RunAsync(jobNames, ct)` returns one `JobResult` per submitted name in
  submission order, with the outcome (`Succeeded`, `Failed`, `Cancelled`)
  and the elapsed time. The completion of every job is awaited before
  `RunAsync` returns.
- The shared `JsonDailyLogger` (PR #146) batches concurrent appends into
  a single channel writer, so 3 jobs writing in lock-step produce a valid
  daily file with interleaved entries — not 3 corrupted writes.
- Failure isolation lives in the runner: a `JobOutcome.Failed` on one job
  releases its slot and the next queued job starts; siblings are not
  cancelled.

## Pre-requisites

- Build everything once:
  ```bash
  dotnet build EasySave.sln
  ```
- Bump the cap in `appsettings.json` next to the EasySave executable
  (`src/EasySave.UI/bin/Debug/net8.0/appsettings.json` for `dotnet run`).
  Default is 4; the recette uses 3 and 2 to exercise both the
  all-parallel and the queueing case:
  ```json
  "max_parallel_jobs": 3
  ```
- Configure 5 backup jobs (`job-1` ... `job-5`) via the GUI Jobs view,
  each pointing at a source with ≥ 200 small files or ~50 MB so the
  parallel window is observable.
- **Pre-condition — wiring:** `ParallelBackupOrchestrator` is fully
  implemented and unit-tested (PRs #136, #143) but the GUI still routes
  job execution through `BackupManagerAdapter` (sequential per Run).
  This recette runs after the wiring task lands `ParallelBackupOrchestrator`
  into `EasySave.UI/App.axaml.cs` and switches the "Run All" path to
  call `RunAsync(jobs, ct)`. The CLI path
  (`EasySave/Program.cs:33`) currently uses
  `ParallelBackupOrchestratorStub`; it must be swapped for the real
  implementation too for the LAN / headless replay.

## Scenarios

Each scenario uses an empty result table at the bottom — fill it in during
the manual run and paste the screenshot link if relevant.

### 1. 3 jobs en parallèle, MaxParallelJobs=3

| Step | Action | Expected |
|---|---|---|
| 1.1 | Set `"max_parallel_jobs": 3` in `appsettings.json`. | — |
| 1.2 | Launch the engine: `dotnet run --project src/EasySave.UI`. | Window appears. |
| 1.3 | Run `job-1`, `job-2`, `job-3` in quick succession (or click **Run All** if it submits all of them at once). | All three job cards flip to `Running` within ~1 s. Progress bars all advance — not just one. |
| 1.4 | Watch the cards for 5–10 s. | `FilesLeft` on **all three** cards is decreasing concurrently. If only one card advances at a time, parallelism is broken. |
| 1.5 | Open today's daily log (`%AppData%\ProSoft\EasySave\Logs\<yyyy-MM-dd>.json` on Windows; `~/.config/ProSoft/EasySave/Logs/` on Linux/macOS). | Entries from `job-1`, `job-2`, `job-3` are **interleaved** — `JobName` values alternate within any 1-second window of `Timestamp`. Sequential execution would group entries by job. |
| 1.6 | When all three finish, open `state.json` (`%AppData%\ProSoft\EasySave\state.json`). | Three entries, all `"State": "Inactive"`, `FilesRemaining: 0`, distinct `LastActionTime` values within seconds of each other. |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 2. MaxParallelJobs=2, lancer 5 jobs

| Step | Action | Expected |
|---|---|---|
| 2.1 | Set `"max_parallel_jobs": 2` in `appsettings.json`, relaunch the engine. | — |
| 2.2 | Submit all 5 jobs (`job-1` ... `job-5`). | Exactly **2** cards flip to `Running` immediately. The other 3 stay `Idle` (or show a `Queued` indicator if the GUI surfaces one). |
| 2.3 | Wait for the first running job to finish. | The next queued job flips to `Running` within ~1 s. The cap is **strictly 2 simultaneously** — never 3. |
| 2.4 | Repeat until all 5 are done. | The 2-active / N-queued pattern holds across the whole run; no job is silently dropped. |
| 2.5 | Daily log timestamp window check. | At any timestamp `T`, no more than 2 distinct `JobName` values appear within the same second range. |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 3. Crash simulé sur job 2 (source supprimée)

| Step | Action | Expected |
|---|---|---|
| 3.1 | Set `"max_parallel_jobs": 3`. Restore `job-2`'s source to a real directory. | — |
| 3.2 | Submit `job-1`, `job-2`, `job-3`. | All three flip to `Running`. |
| 3.3 | While `job-2` is mid-copy, **delete its source directory** (`rm -rf` on the source path) so the next `FileInfo` access throws. | `job-2` fails at the next file boundary. Its card flips to `Failed` (or `Inactive` with an error in the log) within seconds. |
| 3.4 | Watch `job-1` and `job-3`. | Both **continue running and finish** normally. They are not cancelled, paused, or stuck. |
| 3.5 | Daily log. | A `FileTransferTimeMs: -1` entry (cahier error signal) appears for `job-2`'s last attempted file. No error entries for `job-1` or `job-3`. |
| 3.6 | `state.json`. | `job-1` and `job-3` end at `"State": "Inactive"`. `job-2` ends at `"State": "Inactive"` with `FilesRemaining > 0` (mid-run failure). |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 4. Pause un job pendant que les 2 autres tournent

| Step | Action | Expected |
|---|---|---|
| 4.1 | `"max_parallel_jobs": 3`. Submit `job-1`, `job-2`, `job-3`. | All running. |
| 4.2 | Click **Pause** on `job-2` only (or send a remote Pause command if the remote console is connected). | `job-2` flips to `Paused` within ~1 file boundary. |
| 4.3 | Watch `job-1` and `job-3`. | Both **keep running at the same pace** — no slowdown, no pause. The pause is strictly isolated. |
| 4.4 | Click **Play** on `job-2`. | `job-2` resumes from where it stopped (FilesLeft does not jump back). |
| 4.5 | All three finish. | `state.json` shows all three at `"State": "Inactive"`. |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 5. `state.json` final cohérent

After scenario 1 (3 jobs parallel, all succeed):

| Step | Action | Expected |
|---|---|---|
| 5.1 | Open `state.json`. | Exactly 3 entries (one per job submitted), no orphan or duplicate. |
| 5.2 | Inspect each entry. | `"State": "Inactive"`, `FilesRemaining: 0`, `SizeRemaining: 0`, `CurrentSource: ""`, `CurrentTarget: ""`. |
| 5.3 | Compare `LastActionTime` across the 3 entries. | All three timestamps are **within a few seconds of each other** — the difference is at most one job's runtime, not 3× the runtime. If they are spread out by 3× the per-job duration, execution was sequential, not parallel. |
| 5.4 | Compare to the daily log. | `Timestamp` of the last entry per job in the daily log matches the `LastActionTime` in `state.json` within ~1 s. |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

## Out of scope

- Stress test beyond `MaxParallelJobs > 10` — the cahier expects a
  reasonable operator workload, not a load benchmark.
- CryptoSoft contention between parallel jobs (separate semaphore in
  `BigFileGate`) — covered by the BigFileGate recette.
- Mixed Full / Differential parallelism — both strategies are valid and
  the orchestrator does not care; this recette uses Full for simplicity.

## Sign-off

| Tester | Engine OS | All 5 scenarios pass? | Date |
|---|---|---|---|
| ____ | ____ | ☐ Yes ☐ No | ____ |

If any scenario fails, file an issue with the scenario number, the
screenshot, and the relevant excerpts from the daily log + `state.json`.
Tag the issue with `recette-v3` and `dev2-backup`.
