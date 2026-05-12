# Recette V3 — Pause / reprise auto sur logiciel métier

**Critère grille tuteur V3** : « Si le logiciel détecte le fonctionnement
d'un logiciel métier, il doit obligatoirement mettre en pause les
travaux. Celles-ci redémarrent automatiquement dès que le logiciel
métier est arrêté. »

Cette recette valide le chemin V3 : la détection du logiciel métier
appelle `IJobController.PauseAll()` sur l'orchestrateur parallèle (vrai
PauseGate, pas une annulation), et `ResumeAll()` au moment où la dernière
instance disparaît. Chaque transition est tracée dans le log journalier
avec `LogEvent.BusinessSoftwareAutoPaused` / `BusinessSoftwareAutoResumed`.

## Pré-requis

- EasySave v3 buildé en Release (`dotnet build EasySave.sln -c Release`).
- GUI Avalonia lancée (`dotnet run --project src/EasySave.UI`).
- Un dossier source contenant **≥ 50 fichiers** pour avoir le temps
  d'observer la pause.
- La calculatrice Windows (`calc.exe`) disponible — sert de logiciel
  métier de démonstration.

## Configuration

Éditer `src/EasySave/appsettings.json` (ou la copie live
`%AppData%\ProSoft\EasySave\settings.json`) :

```json
{
  "language": "fr",
  "business_software": ["calc"],
  "max_parallel_jobs": 2,
  "log_format": "json"
}
```

> Note : `business_software` accepte indifféremment `"calc"` ou
> `"calc.exe"` — le détecteur normalise le suffixe `.exe`.

Relancer la GUI pour que `BusinessWatcherService` recharge la liste.

## Procédure — un seul job parallèle

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Créer un job Full pointant vers le dossier source (≥ 50 fichiers). | Le job apparaît dans **Jobs** avec l'état `Idle`. |
| 2 | Cliquer **Run** (▶). | `state.json` : `"State": 1` (Active). La progression avance, les fichiers apparaissent dans la cible. |
| 3 | Pendant que le job tourne, ouvrir `calc.exe`. | Sous **2 secondes** (intervalle de polling), le job passe à `Paused` dans la GUI. `state.json` : `"State": 2` (Paused). Aucune nouvelle ligne de copie n'apparaît dans `%AppData%\ProSoft\EasySave\Logs\YYYY-MM-DD.json` tant que `calc.exe` est ouvert. Une ligne avec `"EventType": 10` (`BusinessSoftwareAutoPaused`) est écrite, avec `"SourceFile": "calc"`. |
| 4 | Fermer `calc.exe`. | Sous **2 secondes**, le job repasse à `Active`. Une ligne avec `"EventType": 11` (`BusinessSoftwareAutoResumed`) est écrite. La copie reprend **au fichier suivant** celui copié juste avant la pause. |
| 5 | Attendre la fin. | `state.json` : `"State": 0` (Inactive), `FilesRemaining: 0`. Tous les fichiers de la source sont en cible, chacun **copié une seule fois**. |

## Procédure — Run-All (plusieurs jobs en parallèle)

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Créer 3 jobs Full vers 3 dossiers source distincts, chacun ≥ 30 fichiers. | 3 jobs `Idle`. |
| 2 | Cliquer **Run All** (ou Run sur chacun). | 2 jobs `Active` (cap `MaxParallelJobs = 2`), 1 queued. |
| 3 | Ouvrir `calc.exe` pendant que 2 jobs tournent. | Les **2 jobs Active** passent en `Paused` sous 2 s. Une **seule** ligne `BusinessSoftwareAutoPaused` est écrite (pas une par job — le bridge logue le verrou global). Le 3ᵉ job reste queued. |
| 4 | Fermer `calc.exe`. | Les 2 jobs reprennent simultanément ; le 3ᵉ démarre dès qu'un slot se libère. Une **seule** ligne `BusinessSoftwareAutoResumed` est écrite. |
| 5 | Attendre la fin. | Les 3 jobs terminent `Inactive`. Aucun fichier dupliqué dans les cibles. |

## Critères d'acceptation

- [ ] Le job se met en pause **dans les 2 secondes** suivant l'ouverture
      de `calc.exe`.
- [ ] Aucun fichier n'est copié pendant la pause.
- [ ] Le job reprend **automatiquement** à la fermeture de `calc.exe`,
      sans intervention utilisateur.
- [ ] `state.json` reflète fidèlement `Active → Paused → Active → Inactive`.
- [ ] Le log journalier contient les entrées `EventType: 10`
      (`BusinessSoftwareAutoPaused`) et `EventType: 11`
      (`BusinessSoftwareAutoResumed`) aux moments attendus.
- [ ] Sur Run-All, **toutes** les exécutions actives se mettent en pause
      en une seule "vague" (PauseAll), pas job par job.

## Cas limites

- **`calc.exe` ouvert *au lancement* du job** — détection au premier poll
  → pause immédiate sans avoir copié de fichier. Reprise normale à la
  fermeture.
- **Plusieurs ouvertures/fermetures successives de `calc.exe`** — chaque
  transition est tracée ; le job repart proprement à chaque fois.
- **Sortie brutale d'EasySave pendant la pause** (kill du process GUI) —
  `state.json` reflète `Paused`. Au prochain démarrage l'opérateur peut
  inspecter l'état ; aucune reprise automatique sur redémarrage (par
  design en v3.0).

## Couverture automatique

- `tests/EasySave.Tests.V2/BusinessSoftwareControllerBridgeTests.cs` — 6
  cas couvrant : un Detected appelle `PauseAll` + logue
  `BusinessSoftwareAutoPaused`, un Gone appelle `ResumeAll` + logue
  `BusinessSoftwareAutoResumed`, le cycle complet, l'idempotence du
  `Start()` (pas de double-souscription), `Dispose()` désinscrit, et le
  rejet des arguments null.
- `tests/EasySave.Tests/BusinessSoftwareDetectorTests.cs` — polling et
  normalisation `.exe` côté détecteur (V2, toujours valides).

La chaîne complète GUI → `BusinessSoftwareDetector` →
`BusinessSoftwareControllerBridge` → `IParallelBackupOrchestrator` →
`BackupManager` ne peut pas être automatisée sans un harness Avalonia
headless ; cette recette manuelle reste donc nécessaire à chaque release.

## Si la recette échoue

| Symptôme | Cause probable | Vérification |
|---|---|---|
| Le job ne pause jamais | `business_software` mal orthographié ou GUI lancée avant l'édition de la config | `appsettings.json` lu au démarrage ; relancer la GUI après changement. |
| Pause sans entrée `BusinessSoftwareAutoPaused` dans le log | Le bridge n'a pas été démarré au boot | Vérifier `App.OnFrameworkInitializationCompleted` : `BusinessSoftwareControllerBridge.Start()` est appelé avant la construction de `JobsViewModel`. |
| Reprise ne fait rien (job reste Paused) | `IJobController.ResumeAll()` ne trouve plus le contexte (job déjà terminé) | Normal si la copie a fini pendant la pause de la calc ; sinon vérifier que `ParallelBackupOrchestrator._running` contient bien le job en `state.json` Paused. |
| `BusinessSoftwareAutoResumed` écrit alors que `calc.exe` est toujours ouvert | `IsAnyBusinessSoftwareRunning` ne match plus le process name | Vérifier la normalisation `.exe` côté `BusinessSoftwareDetector.NormalizeProcessName`. |
