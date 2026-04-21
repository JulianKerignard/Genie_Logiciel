# EasySave v1.0 — Répartition des tâches par phase

*Projet Génie Logiciel — Groupe 4 — CESI A3*

## Équipe

- **Dev1 — EasyLog :** Julian Kerignard
- **Dev2 — Backup :** Chloé Lagocki
- **Dev3 — State/Persistence :** Ilian Cahouch
- **Dev4 — CLI/UI :** Samuel Ceccarelli

---

## Phase 1 — Setup

### Équipe complète (Julian, Chloé, Ilian, Samuel)

- Créer la solution `EasySave.sln` et les 2 `.csproj` (net8.0)
- Créer l'arborescence `src/EasySave/` et `src/EasyLog/`
- Ajouter la ProjectReference EasySave → EasyLog
- `Program.cs` qui affiche "Hello EasySave"
- Rédiger `README.md` initial

### Tâches transverses (non assignées)

- Ajouter un `.gitignore` .NET
- Inviter le tuteur sur le repo GitHub

### Grille tuteur

- [Grille] Communiquer l'accès Git au tuteur (avec date) — ✓ Terminée
- [Grille] Activité Git régulière sur toutes les périodes + branches propres — ✓ Terminée
- [Grille] Organiser les livrables (docs, code source, release notes) — ✓ Terminée
- [Grille] Respecter les jalons de livraison — ✓ Terminée

---

## Phase 2 — Squelettes

### Julian Kerignard (Dev1 — EasyLog)

- Interface `IDailyLogger`
- POCO `LogEntry`
- Classe `JsonDailyLogger` (vide)

### Chloé (Dev2 — Backup)

- Enum `BackupType` (Full / Differential)
- POCO `BackupJob`
- `IBackupStrategy` + `FullStrategy` + `DiffStrategy` (stubs)
- `BackupManager` avec méthodes `throw NotImplementedException`

### Ilian (Dev3 — State)

- POCO `StateEntry`
- `StateTracker.Update()` (vide)
- `JobRepository.Load()` et `Save()` (vides)

### Samuel (Dev4 — CLI)

- `ConsoleUI` avec menu affiché (actions vides)
- `CommandParser.ParseJobSelection` (retourne liste vide)
- `LanguageService.T(key)` (retourne la clé)
- `Resources/en.json` + `fr.json` (clés vides)

---

## Phase 3 — Implémentation

### Julian Kerignard (Dev1 — EasyLog)

- `JsonDailyLogger.Append` écrit `Logs/YYYY-MM-DD.json`

### Chloé (Dev2 — Backup)

- Implémenter `FullStrategy` (copie tout)
- Implémenter `DiffStrategy` (taille + date)
- `BackupManager.ExecuteJob` (parcours + logger + state)
- `AddJob` bloque au-delà de 5 jobs

### Ilian (Dev3 — State)

- `JobRepository` persiste `jobs.json`
- Emplacement configurable (pas `C:\temp`)
- `StateTracker.Update` écrit `state.json` en temps réel

### Samuel (Dev4 — CLI)

- Menu interactif complet (ajouter/supprimer/lister/exécuter)
- Parser les formats `1`, `1-3`, `1;3`, `1-3;5`
- Mode CLI (`EasySave.exe 1-3`)
- `LanguageService` charge les JSON, switch EN/FR

### Équipe complète

- Rédiger manuel utilisateur (1 page) + doc support
- Écrire `CHANGELOG.md` v1.0

---

## Phase 4 — Finition et livraison

### Équipe complète (Julian, Chloé, Ilian, Samuel)

- Crosstest : chacun teste la zone d'un autre
- Fixer les bugs trouvés au crosstest
- Finaliser UML et exporter PDF (cas d'usage, classes, séquence)
- Merge `staging` → `main` via PR
- Tag `v1.0.0` sur le commit de merge
- Release GitHub avec le `.zip` du binaire
- Publier les release notes
- Envoi du Livrable 1 au tuteur

### Grille tuteur — Recette V1.0

- Interface Mode Console sur .NET Core (8.0)
- Enregistrer jusqu'à 5 travaux de sauvegarde
- IHM soignée
- Fichier d'État temps réel conforme cahier des charges
- Fichier log journalier unique conforme cahier des charges
- DLL EasyLog utilisée pour la gestion du log
- Logiciel multi-langues (EN/FR)
- Lancement d'un travail unique ou d'une séquence via l'interface
- Sauvegardes lançables via ligne de commande
- UML + doc utilisateur présents dans Git
- V1.0 livrable aux clients (recette finale)

### Grille tuteur — Qualité code et architecture

- UML : Diagramme de cas d'utilisation V1.0
- UML : Diagramme d'activité V1.0
- UML : Diagramme de classes & composants V1.0
- UML : Diagramme de séquence V1.0
- Code et commentaires en anglais
- Code optimisé (absence de redondance)
- Documenter la DLL EasyLog (API publique + compatibilité versions)
- Structure code/docs permettant reprise par une autre équipe
- Appliquer les Design Patterns (Singleton, Strategy, Repository...)
- Architecture MVC prête pour passage console → graphique (V2)
- Emplacements judicieux des fichiers (log, état, config)

---

## Synthèse par développeur

### Julian Kerignard — Dev1 (EasyLog)

Responsable de la bibliothèque de logging réutilisable `EasyLog.dll`. Interface `IDailyLogger`, POCO `LogEntry`, implémentation `JsonDailyLogger` avec écriture journalière atomique et support UNC.

### Chloé — Dev2 (Backup)

Responsable du moteur de sauvegarde. Enum `BackupType`, POCO `BackupJob`, pattern Strategy (`FullBackupStrategy`, `DifferentialBackupStrategy`), `BackupManager` (`AddJob` avec limite 5 jobs, `RemoveJob`, `ListJobs`, `ExecuteJob`, `ExecuteAll`).

### Ilian — Dev3 (State/Persistence)

Responsable de la persistance et du suivi d'état. POCO `StateEntry`, singleton `StateTracker` écrivant `state.json` en temps réel, `JobRepository` persistant `jobs.json`, `AppConfig` pour les emplacements configurables.

### Samuel — Dev4 (CLI/UI)

Responsable de l'interface console et de l'internationalisation. `ConsoleUI` avec menu interactif, `CommandParser` pour les sélections multiples, `LanguageService` EN/FR avec resources JSON, mode CLI direct.
