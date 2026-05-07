# EasySave v3.0 — Répartition des tâches par phase

*Projet Génie Logiciel — Groupe 4 — CESI A3*

## Équipe

- **Dev1 — EasyLog :** Julian Kerignard
- **Dev2 — Backup :** Chloé Lagocki
- **Dev3 — State/Persistence :** Ilian Cahouch
- **Dev4 — UI/RemoteConsole :** Samuel Ceccarelli

---

## Phase 1 — Setup

### Équipe complète (Julian, Chloé, Ilian, Samuel)

- Branche git `release/v3` + tag `v2.0` final

### Julian Kerignard

- Étendre `LogEvent` enum (`RemoteConnect`, `ParallelStart`, `BigFileEnqueued`)

### Chloé

- Projet `EasySave.Shared` (DTOs partagés serveur/client)

### Ilian

- Étendre `settings.json` schema (V3 fields : `MaxParallelJobs`, `LargeFileThresholdKb`, `RemoteConsole.Port`)

### Samuel

- Nouveau projet `EasySave.RemoteConsole` (Avalonia client)

---

## Phase 2 — Squelettes

### Chloé

- Interfaces `IParallelBackupOrchestrator` + `IBigFileGate`
- `JobExecutionContext` (`CancellationTokenSource` + `ManualResetEventSlim` + état partagé)
- DTOs JSON protocole (`JobProgressDto`, `CommandDto`, `EventDto`, enums)
- UML squelette V3 (classes prévisionnelles)

### Ilian

- Étendre `StateRepository` pour multi-jobs concurrents

### Samuel

- Interface `IRemoteConsoleServer` (côté EasySave)
- Interface `IRemoteConsoleClient` (côté Console app)
- `ConsoleViewModel` skeleton (Avalonia MVVM)

---

## Phase 3 — Implémentation

### Julian Kerignard

- `JsonDailyLogger` thread-safe writes (concurrence multi-jobs)
- Bridge moteur → `RemoteConsoleServer` (EventBus)
- Tests `TcpRemoteConsoleServer` (xUnit)

### Chloé

- `BigFileGate` impl (`SemaphoreSlim` N=1)
- Tests `BigFileGate` (sémaphore N=1)
- Intégration `BigFileGate` dans le pipeline de copie
- `ParallelBackupOrchestrator` impl (`Task.WhenAll` + isolation)
- Tests `ParallelBackupOrchestrator` (concurrence)
- `ProgressAggregator` par job (état isolé)

### Ilian

- `StateRepository` thread-safe + tests concurrence

### Samuel

- `TcpRemoteConsoleServer` impl (`TcpListener` async)
- Réception Pause/Play/Stop côté serveur (cmd → orchestrator)
- `TcpRemoteConsoleClient` impl + reconnexion auto
- `ConsoleView.axaml` (jobs live + boutons Pause/Play/Stop)

---

## Phase 4 — Finition et livraison

### Chloé

- [Grille] UML V3 : Classes finales V3
- [Grille] UML V3 : Séquence Parallélisme + BigFileGate
- [Grille] UML V3 : Séquence Console déportée
- [Grille] UML V3 : Déploiement (architecture distribuée)
- [Grille] UML V3 : Activité (cycle de vie d'un job parallèle)

### Julian Kerignard

- [Recette V3] Parallélisme (3 jobs simultanés)
- [Recette V3] File gros fichiers (`BigFileGate`)
- [Recette V3] Console déportée (connexion + commandes)
- [Recette V3] Rétrocompat V1 + V2 + V3

### Samuel

- Manuel utilisateur V3 (`.docx`)

### Équipe complète (Julian, Chloé, Ilian, Samuel)

- Support soutenance V3 (`.pptx`)
- Tag git `v3.0.0` + livraison la veille de la soutenance

---

## Phase 5 — Bonus

### Équipe complète (Julian, Chloé, Ilian, Samuel)

- `CryptoSoft` sémaphore (limite parallèle CryptoSoft pour éviter contention)
- Sécurisation socket TLS optionnelle
- Multi-console synchronisée (broadcast bidirectionnel : plusieurs consoles connectées au même moteur)

---

## Synthèse par développeur

### Julian Kerignard — Dev1 (EasyLog)

Responsable de l'évolution de la bibliothèque `EasyLog.dll` pour la concurrence V3 et du pont moteur → console déportée. Extension de `LogEvent` (`RemoteConnect`, `ParallelStart`, `BigFileEnqueued`) pour tracer les nouveaux événements V3. `JsonDailyLogger` rendu thread-safe pour les écritures concurrentes multi-jobs. Bridge `EventBus` qui transforme les événements moteur en `EventDto` poussés au serveur déporté. Tests xUnit du `TcpRemoteConsoleServer`. Responsable de l'ensemble des recettes V3 (parallélisme, gros fichiers, console déportée, rétrocompat V1/V2/V3).

### Chloé — Dev2 (Backup)

Responsable du moteur de sauvegarde parallèle V3. Création du projet `EasySave.Shared` (DTOs JSON partagés moteur/client). Architecture parallèle complète : `IParallelBackupOrchestrator` (`Task.WhenAll` + cap `MaxParallelJobs` via `SemaphoreSlim`), `JobExecutionContext` (un par job, `CancellationTokenSource` + `ManualResetEventSlim` pour Pause/Stop sans interférence inter-jobs), `IBigFileGate` (sémaphore N=1 global pour sérialiser les transferts de gros fichiers et éviter la saturation bande passante), `ProgressAggregator` par job. Tests xUnit complets sur la concurrence (cap respecté, Pause/Resume/Stop isolés, race deterministes). Responsable de l'ensemble des diagrammes UML V3.

### Ilian — Dev3 (State/Persistence)

Responsable de la persistance V3 thread-safe. Extension de `settings.json` avec les nouveaux champs V3 (`MaxParallelJobs`, `LargeFileThresholdKb`, `RemoteConsole.Port`). `StateRepository` adapté pour gérer les écritures concurrentes des N jobs parallèles sans corruption du `state.json`. Tests xUnit de concurrence sur le repository.

### Samuel — Dev4 (UI/RemoteConsole)

Responsable de la console déportée V3. Création du projet Avalonia `EasySave.RemoteConsole` (client cross-platform Win/macOS). Interfaces `IRemoteConsoleServer` (côté moteur) et `IRemoteConsoleClient` (côté console). Implémentations `TcpRemoteConsoleServer` (`TcpListener` async, NDJSON-over-TCP) et `TcpRemoteConsoleClient` (avec reconnexion automatique). Réception côté serveur des commandes Pause/Play/Stop et propagation vers `ParallelBackupOrchestrator`. `ConsoleView.axaml` MVVM avec affichage live des jobs et boutons de contrôle. Manuel utilisateur V3 avec captures d'écran.
