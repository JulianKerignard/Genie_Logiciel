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

> **Note terminologie** : l'enum `JobState` n'a pas de valeur dédiée
> `Stopped`. Après un Stop **comme après un run complet**, le job
> retombe à `Inactive` (`0`) et `BackupManager.ExecuteJob` reset
> `FilesRemaining = 0` dans son `finally` (cf. `BackupManager.cs:360`).
> Les deux états sont **identiques** dans `state.json`.
>
> Le signal observable qui distingue les deux est dans le **log
> journalier** : comparer le **nombre de lignes `FileTransfer`** du
> job (entrées avec `EventType = null`, voir
> `[JsonIgnore(WhenWritingNull)]`) à `TotalFilesEligible` enregistré
> sur le `StateEntry` au début du run :
> - Stop mid-run → moins de lignes `FileTransfer` que
>   `TotalFilesEligible` ;
> - Run complet → exactement `TotalFilesEligible` lignes.
>
> Le log ne contient aucun marqueur "Cancelled" — `JobOutcome` est un
> type interne renvoyé par `IParallelBackupOrchestrator.RunAsync`,
> jamais persisté.

## Procédure — Mix : pause d'un seul job parmi plusieurs actifs

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Configurer **2 jobs** Full avec ~50 fichiers chacun (sources distinctes pour éviter la contention sur le BigFileGate). `max_parallel_jobs ≥ 2`. | 2 jobs `Idle` dans la GUI. |
| 2 | Cliquer **Run All** (ou Run sur chaque job individuellement). | Les 2 passent à `Active` dans `state.json`. La GUI affiche les 2 barres de progression qui montent en parallèle. |
| 3 | Pendant que les 2 jobs copient activement, cliquer **Pause** sur **Job A uniquement**. | Sous 1 s : `state.json` montre `Job A : State = 2 (Paused)`, **`Job B : State = 1 (Active)`, FilesRemaining qui continue à décroître**. Aucun nouveau fichier n'apparaît dans la cible de A. La cible de B continue à se remplir. Log : `JobPaused` pour A uniquement. |
| 4 | Inspecter la timeline de copie (mtimes des fichiers cible). | Les nouveaux fichiers cibles n'apparaissent que dans le dossier de B. Le dernier fichier de A est complet (jamais coupé en deux). |
| 5 | Attendre que Job B termine **avant** de reprendre A. | `state.json` : `Job B : State = 0 (Inactive)`, `FilesRemaining = 0`. `Job A : State = 2 (Paused)` toujours — la pause de A n'a pas été affectée par la fin de B. |
| 6 | Cliquer **Play** sur Job A. | `state.json` : `Job A : State = 1 (Active)`. La copie reprend au fichier suivant celui paused. Log : `JobResumed` pour A. |
| 7 | Attendre la fin de A. | Les 2 jobs `Inactive` avec `FilesRemaining = 0`. Tous les fichiers présents dans les deux sources sont en cible. |

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
- [ ] **Isolation pause par job** : Pause sur Job A pendant que Job B
      tourne ne ralentit ni n'interrompt Job B. Job B peut même
      terminer avant que A reprenne ; la pause de A reste effective.
- [ ] **"Stopped" : pas de valeur d'enum dédiée**. Stop et Complete
      terminent tous les deux à `State = Inactive` avec
      `FilesRemaining = 0` (`state.json` identique). Pour les
      distinguer, compter les lignes `FileTransfer` du job dans le log
      journalier et comparer à `TotalFilesEligible` capturé au début
      du run.

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
| Pause sur Job A ralentit Job B | Les 2 jobs partagent le même `PauseGate` au lieu d'un gate par job. | `ParallelBackupOrchestrator` doit créer un `JobExecutionContext` (et donc un `ManualResetEventSlim`) **par job** — vérifier le ConcurrentDictionary `_running` dans l'orchestrateur. |
