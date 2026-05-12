# Recette V3 — Pause / Play / Stop (par job et Run-All)

**Critère grille tuteur V3** : « Pour chaque travail (ou l'ensemble des
travaux), l'utilisateur doit pouvoir Pause / Play / Stop. » La pause est
effective au prochain *file boundary* (jamais en plein milieu d'une copie),
elle est reflétée dans `state.json`, et chaque transition est tracée dans
le log journalier (`LogEvent.JobPaused`, `LogEvent.JobResumed`).

## Pré-requis

- EasySave v3 buildé en Release (`dotnet build EasySave.sln -c Release`).
- GUI Avalonia (`dotnet run --project src/EasySave.UI`) **ou** Remote
  Console (`dotnet run --project EasySave.RemoteConsole`).
- Un job Full configuré sur **≥ 50 fichiers** ou un job avec des fichiers
  de taille moyenne — assez pour laisser le temps de cliquer Pause avant
  la fin.

## Procédure — pause / play par job

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Lancer le job depuis la GUI. | `state.json` : `"State": 1` (Active). |
| 2 | Cliquer **Pause** sur la ligne du job. | Sous **1 seconde**, `state.json` passe à `"State": 2` (Paused). Le log journalier reçoit une entrée `EventType = JobPaused` (`8`). Aucun nouveau fichier n'apparaît dans le dossier cible. |
| 3 | Inspecter le dossier cible. | Le compteur de fichiers ne progresse plus. Le fichier en cours d'écriture juste avant le Pause est **complet** (jamais coupé en deux). |
| 4 | Cliquer **Play** sur la même ligne. | `state.json` repasse à `"State": 1`. Le log journalier reçoit `EventType = JobResumed` (`9`). La copie reprend **au fichier suivant** celui copié juste avant la pause. |
| 5 | Attendre la fin. | `state.json` : `"State": 0` (Inactive), `FilesRemaining = 0`. Tous les fichiers présents dans la source apparaissent dans la cible. |

## Procédure — Stop par job

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Relancer un job. | `state.json` : Active. |
| 2 | Cliquer **Stop** sur la ligne. | Sous **1 seconde**, le job se termine. `JobOutcome.Cancelled` côté orchestrateur, `state.json` : `"State": 0` (Inactive). La cible contient les fichiers déjà copiés (pas de rollback). |
| 3 | Variante — Stop pendant un Pause. | Même comportement : le `Wait(ct)` token-aware lance OCE et le job sort Inactive. |

## Procédure — Run-All (Pause All / Resume All)

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Démarrer **3 jobs en parallèle** (cap = 2 ou 4 selon `MaxParallelJobs`). | 2 jobs Active, 1 en file. |
| 2 | Cliquer **Pause All** dans la GUI (ou ouvrir un logiciel métier surveillé — `calc.exe` par défaut). | Sous 1 s, les 2 jobs Active passent en Paused (`state.json` reflète Paused pour les deux). Le 3ᵉ job reste queued. Le log reçoit un `JobPaused` par job pausé. |
| 3 | Cliquer **Resume All** (ou fermer `calc.exe`). | Les 2 jobs reprennent simultanément ; le 3ᵉ démarre dès qu'un slot se libère. Log : un `JobResumed` par job repris. |
| 4 | Attendre la fin. | Les 3 jobs terminent Inactive. Tous les fichiers présents en source sont en cible. |

## Critères d'acceptation

- [ ] Le Pause prend effet **après le fichier en cours**, jamais en plein
      milieu (vérifiable via la taille du dernier fichier cible).
- [ ] `state.json` reflète fidèlement `Active → Paused → Active → Inactive`.
- [ ] Chaque transition apparaît dans le log journalier avec
      `EventType: JobPaused` ou `JobResumed`.
- [ ] Le Resume reprend **au fichier suivant** (pas depuis le début).
- [ ] Le Stop pendant un Pause termine bien le job (pas de thread bloqué).
- [ ] `PauseAll` / `ResumeAll` opèrent sur tous les jobs sans toucher aux
      jobs queued.

## Couverture automatique

- `tests/EasySave.Tests/BackupManagerPauseResumeTests.cs` — couvre la
  mécanique de la pause gate dans `BackupManager.ExecuteJob` :
  - `ExecuteJob_PauseGateReset_StateBecomesPausedThenResumes` — gate
    fermée → state `Paused`, gate ouverte → reprise et fin Inactive.
  - `ExecuteJob_PauseGateAndStopTogether_StateBecomesInactive` — Stop
    pendant un Pause termine bien Inactive (pas Paused).
- `tests/EasySave.Tests.V2/ParallelBackupOrchestratorTests.cs` — couvre
  l'orchestrateur :
  - `PauseAll_ResetsEveryRunningJobsGate`
  - `ResumeAll_SetsEveryPausedJobsGate`
  - `StopAll_CancelsEveryJob_AllResultsAreCancelled`
  - `PauseResumeStopAll_OnEmptyOrchestrator_AreNoOps`

## Si la recette échoue

| Symptôme | Cause probable | Vérification |
|---|---|---|
| Pause ne fait rien (job continue) | Le runner concret n'a pas reçu le `pauseGate` du context. | Vérifier que `BackupManagerJobRunner.RunAsync` passe bien `context.PauseGate` à `ExecuteJob`. |
| Pause termine le job (Inactive au lieu de Paused) | L'OCE est interprétée comme Stop alors que le user voulait Pause. | Vérifier que `IJobController.Pause` reset le gate, **pas** `Cancel()` le CTS. |
| Stop pendant Pause hang la console | Le `Wait` n'est pas token-aware. | `BackupManager.ExecuteJob` doit utiliser `pauseGate.Wait(ct)`, pas `pauseGate.Wait()`. |
| Le log ne contient pas les transitions | Sans `pauseGate`, BackupManager ne logue jamais. | Vérifier que la GUI / l'orchestrateur instancient un `JobExecutionContext` avec son `PauseGate`. |
