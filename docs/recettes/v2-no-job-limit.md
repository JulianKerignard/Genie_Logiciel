# Recette V2 — 6+ jobs acceptés

ClickUp task: `869d036ab` — `[Recette V2] 6+ jobs acceptés (plus de limite 5 en V2)`
Tags: `grille-tuteur`, `recette-v2`

## Goal

Verify that the v1.0 cap of 5 backup jobs has been lifted in v2.0. The v2.0
grille requires the application to accept an unlimited number of jobs.

Reference: cahier des charges v2.0, "Suppression de la limite de 5 jobs".
Reference commit: `0d157fe` — `feat(backup): remove 5-job limit per v2.0 grille`.

## Programmatic evidence

Locked at the service layer by the unit test
`EasySave.Tests.BackupManagerAddJobTests.AddJob_NoLimit_AcceptsManyJobs`,
which calls `BackupManager.AddJob` ten times in a row and asserts the final
job count is 10. The test runs on every CI build, so a regression is
detected before reaching the UI.

## Manual scenario (Avalonia GUI)

### Pre-requisites

- Build the UI project once: `dotnet build src/EasySave.UI/EasySave.UI.csproj`.
- Start with an empty `jobs.json` (delete or rename the existing one for a
  clean run; the file location is shown in the Settings tab).
- Pick a source directory with a few small files and a writable target
  directory; both can be reused across all 10 jobs.

### Steps

1. Launch the GUI: `dotnet run --project src/EasySave.UI`.
2. Open the **Jobs** tab.
3. Click **Add Job** and create the first job:
   - Name: `recette-job-01`
   - Source: the source directory chosen above
   - Target: `<target>/job-01`
   - Type: `Full`
4. Repeat step 3 nine more times, with names `recette-job-02` through
   `recette-job-10` and target subfolders `job-02` through `job-10`.
5. Confirm the jobs list shows all 10 entries with no error toast or
   `Max 5 jobs` message.
6. Click **Run All**.
7. Wait for every job to reach the `Inactive` state (the progress column
   returns to 100% / idle).
8. Open the daily log file in the configured log directory and check that
   each job has produced its file-copy entries.

### Expected result

- All 10 jobs are created without any "Max 5 jobs" or similar error message.
- `jobs.json` on disk contains the 10 job definitions.
- After **Run All**, every job has executed; each source file appears under
  its corresponding target subfolder.
- The daily log file contains entries for every copied file across the 10 jobs.

## Result (filled by tester)

| Field | Value |
|---|---|
| Tester | _to fill_ |
| Date | _to fill_ |
| App version | _v2.0-staging @ <commit>_ |
| Operating system | _to fill_ |
| Outcome | ☐ PASS  ☐ FAIL |
| Notes | _to fill_ |

If the outcome is FAIL, attach a screenshot of the error toast or the log
output to the ClickUp task.
