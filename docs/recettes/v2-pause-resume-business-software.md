# Recette V2 — Pause / reprise automatique sur logiciel métier

**Critère grille tuteur V2** : un job de sauvegarde en cours doit se mettre en pause
quand un logiciel métier surveillé démarre, et reprendre automatiquement quand il
se ferme. La démonstration se fait avec la calculatrice Windows (`calc.exe`).

## Pré-requis

- EasySave v2.0 buildé en Release (`dotnet build EasySave.sln -c Release`).
- GUI Avalonia lancée (`dotnet run --project src/EasySave.UI`).
- Un dossier source contenant **au moins 100 fichiers** (≈10 Mo total) pour avoir
  le temps d'observer la pause. Suggestion : `Documents\` ou un dump de logs.
- Un dossier cible vide.

## Configuration

Éditer `src/EasySave/appsettings.json` (ou `%AppData%\ProSoft\EasySave\settings.json`
si déjà créé par la GUI) :

```json
{
  "language": "fr",
  "encrypted_extensions": [],
  "business_software": ["calc"],
  "log_format": "json",
  "crypto_soft": { "path": "", "timeout_ms": 30000 }
}
```

> Note : `business_software` accepte `"calc"` ou `"calc.exe"` indifféremment —
> le détecteur normalise le suffixe `.exe` (cf. PR #80).

Relancer la GUI pour que `BusinessWatcherService` recharge la liste.

## Procédure de test

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Créer un job Full ou Differential pointant vers le dossier source ≥100 fichiers. | Le job apparaît dans l'onglet **Jobs** avec l'état `Inactive`. |
| 2 | Cliquer sur **Run** (▶). | Le job passe à `Running`, la barre de progression avance, `state.json` montre `"State": 1` (Active). Le panneau **Run progress** affiche le fichier en cours. |
| 3 | Pendant que le job tourne, ouvrir la calculatrice Windows (`calc.exe`). | Sous **2 secondes** (intervalle de polling par défaut), le job passe à `Paused` dans la GUI. `state.json` montre `"State": 2` (Paused) avec `PauseReason` non vide. La progression est **figée** (`FilesRemaining` reste à sa valeur). Aucun nouveau fichier n'apparaît dans le dossier cible. |
| 4 | Inspecter `%AppData%\ProSoft\EasySave\Logs\YYYY-MM-DD.json`. | Aucune nouvelle ligne pour ce job tant que la calculatrice est ouverte. |
| 5 | Fermer la calculatrice. | Sous **2 secondes**, le job repasse à `Running`. La copie reprend **au fichier où elle s'était arrêtée** (pas depuis le début). `state.json` repasse à `"State": 1`. |
| 6 | Attendre la fin du job. | Le job affiche `Inactive`, `FilesRemaining` = 0, le dossier cible contient **exactement** la même arborescence que le dossier source, **chaque fichier copié une seule fois** (vérifier les tailles + un `dir /s` ou équivalent). |

## Critères d'acceptation

- [ ] Le job se met en pause **dans les 2 secondes** suivant l'ouverture de `calc.exe`.
- [ ] Aucun fichier n'est copié pendant la pause.
- [ ] Le job reprend **automatiquement** à la fermeture de `calc.exe`, sans intervention utilisateur.
- [ ] La reprise repart **du fichier suivant** celui copié juste avant la pause (vérifiable via les timestamps des fichiers cibles).
- [ ] Le job termine avec exactement le même nombre de fichiers que la source.
- [ ] `state.json` reflète fidèlement les transitions : Active → Paused → Active → Inactive.

## Cas limites à valider

- **Plusieurs ouvertures/fermetures successives de calc** pendant un même job → chaque transition doit être détectée, le job doit reprendre proprement à chaque fois.
- **Sortie brutale d'EasySave pendant la pause** (Ctrl+C dans la console, ou kill du process GUI) → au prochain démarrage, `state.json` doit pouvoir être inspecté pour savoir où le job s'était arrêté ; un Run manuel reprend du début (pas de reprise automatique au redémarrage en v2.0).
- **Logiciel métier déjà ouvert au lancement du job** → le job doit refuser de démarrer (visible via le bouton Run grisé / message d'erreur), conformément au verrou `IsBusinessSoftwareDetected` dans `JobsViewModel.RunJob`.

## Couverture automatique

Les tests unitaires `tests/EasySave.Tests/BackupManagerPauseResumeTests.cs` couvrent
la mécanique côté `BackupManager` (annulation au file boundary, `state.json`
passe à `Paused`, reprise via `startFromIndex`). La chaîne GUI complète
(détecteur → watcher → adapter) ne peut pas être automatisée sans un harness
Avalonia headless ; cette recette manuelle reste donc nécessaire à chaque release.

## Si la recette échoue

| Symptôme | Cause probable | Vérification |
|---|---|---|
| Le job ne pause jamais | `business_software` mal orthographié, ou GUI lancée avant l'édition de la config | Vérifier la valeur lue par le watcher dans les logs Avalonia, et que le nom matche `Process.GetProcessesByName(...)` |
| Le job reprend mais re-copie depuis le début | `startFromIndex` non transmis ou job de type Differential mal configuré | Pour Full : vérifier que `BackupManagerAdapter.ResumeJob` calcule `TotalFilesEligible - FilesRemaining` avant l'appel à `RunJobAsync`. Pour Differential : c'est le comportement attendu (re-scan), les fichiers déjà copiés sont skippés via le mtime aligné (cf. PR #89). |
| `state.json` reste bloqué à `Active` après pause | `BackupManager.RunJob` n'a pas reçu le token, ou `cts.Cancel()` non appelé | Tracer dans `BackupManagerAdapter.PauseJob` que le job est bien dans `_running` au moment de l'appel |
