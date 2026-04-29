# EasySave v2.0 — Répartition des tâches par phase

*Projet Génie Logiciel — Groupe 4 — CESI A3*

## Équipe

- **Dev1 — EasyLog :** Julian Kerignard
- **Dev2 — Backup :** Chloé Lagocki
- **Dev3 — State/Persistence :** Ilian Cahouch
- **Dev4 — UI Avalonia :** Samuel Ceccarelli

---

## Phase 1 — Setup

### Équipe complète (Julian, Chloé, Ilian, Samuel)

- Créer branche `v2-dev` depuis tag `v1.0.0` + branche `hotfix/v1.1` 
- MAJ README avec roadmap V2 (GUI, CryptoSoft, XML log)

### Julian Kerignard 

- Créer projet xUnit `EasySave.Tests.V2` (references Core + Log + Gui) 

### Chloé 

- Déployer CryptoSoft + définir contrat d'intégration 
### Ilian 

- `appsettings.json` : `encrypted_extensions` + `business_software_list` 

### Samuel 

- Ajouter le projet Avalonia `EasySave.UI` à la solution (net8.0 cross-platform Win/macOS)
---

## Phase 2 — Squelettes

### Julian Kerignard 

- `LogEntry` : ajouter `EncryptionTimeMs` (nullable `long?`) 
- Interface `ILogFormatter`

### Chloé 

- Interface `IEncryptionService` 
- Supprimer la limite de 5 jobs dans `BackupManager` 
- `BusinessSoftwareDetector` : classe (`Process.GetProcesses` + polling) 

### Ilian 

- `StateTracker` expose `INotifyPropertyChanged` (pour GUI bindings) 
- `SettingsRepository` : `Load`/`Save` `settings.json` (`encrypted_extensions` + `business_software`) 

### Samuel 

- `MainWindow` MVVM Avalonia (shell + navigation) 
- `JobEditView` + `JobEditViewModel` Avalonia (create/edit job) 
- `SettingsView` + `SettingsViewModel` Avalonia 
- `MarkupExtension` `{T:Key}` pour `LanguageService` Avalonia (runtime switch)
- `App.axaml` Avalonia : styles globaux + icône appli + About window 

---

## Phase 3 — Implémentation

### Julian Kerignard 

- `JsonFormatter` complet (`ILogFormatter` + `EncryptionTimeMs` + compat V1)
- `XmlFormatter` complet (`ILogFormatter` + log journalier XML + XSD) 
- Tests `JsonFormatter` V2 : tests compat V1 (sans `EncryptionTimeMs`) + V2 
- Tests `XmlFormatter` + XSD : tests roundtrip + validation schema 

### Chloé 

- `CryptoSoftAdapter` complet + intégration `BackupManager` 
- `DiffStrategy` : gérer correctement les fichiers cryptés 
- Mesurer `EncryptionTimeMs` par fichier crypté 
- Tests `CryptoSoftAdapter` : tests xUnit (mock process, error codes, success) 
- Tests `DiffStrategy` V2 : tests fichiers cryptés (diff d'un encrypted file) 
- Tests `BusinessSoftwareDetector` : tests (mock `IProcessProvider`) 

### Ilian 

- Rétrocompat V1 → V2 : lire `state.json` + `jobs.json` + logs JSON V1 
- Persister état incluant les jobs en pause (business lock) 

### Samuel 

- `JobListViewModel` Avalonia complet (`ObservableCollection` + commandes Add/Edit/Delete/Run/RunAll) 
- Écran lancement Avalonia avec `ProgressBar` par job (`StateTracker` bindings) 
- Détection logiciel métier Avalonia → pause auto des jobs en cours 
- Switch langue FR/EN Avalonia en runtime (sans restart) 

---

## Phase 4 — Finition et livraison

### Chloé 

- [Grille] UML V2 : cas d'utilisation (GUI + settings + CryptoSoft) 
- [Grille] UML V2 : classes & composants (MVVM + `CryptoSoftAdapter`)
- [Grille] UML V2 : séquence (sauvegarde avec cryptage) 
- [Grille] UML V2 : activité (pause/reprise sur détection logiciel métier) 

### Samuel 

- Rédiger manuel utilisateur V2 (avec screenshots GUI Avalonia) 
- [Recette V2] Pause/reprise auto : job + ouvrir calc → pause → fermer → reprise 
- [Recette V2] Log : `EncryptionTimeMs` présent + format XML vs JSON switchable 

### Julian Kerignard

- [Recette V2] 6+ jobs acceptés (plus de limite 5 en V2) 
- [Recette V2] Settings UI Avalonia : éditer `encrypted_extensions` → fichier crypté 
- [Recette V2] Switch langue FR↔EN runtime sans restart 


### Équipe complète (Julian, Chloé, Ilian, Samuel)

- Rédiger `CHANGELOG.md` V2.0 (release notes) 
- Crosstest V2 : chaque dev teste la zone d'un autre 
- Fixer les bugs trouvés au crosstest V2 
- Merge `v2-dev` → `main` + tag `v2.0.0` 
- Release GitHub v2.0.0 avec binaire zippé 
- Envoi Livrable 2 au tuteur 
- [Grille V2] Validation checklist grille tuteur V2 

---

## Phase 5 — Bonus

### Équipe complète (Julian, Chloé, Ilian, Samuel)

**Scheduler (sauvegarde programmée) :**

- `IScheduler` interface + `JobSchedule` model 
- `SchedulerService` impl (Timer / BackgroundService) 
- `ScheduleView` + VM Avalonia (UI de configuration du loop) 
- Tests `SchedulerService` (xUnit)
- Recette Scheduler (fiche de test end-to-end) 
- UML Activité Scheduler 

**Restauration :**

- `IRestoreService` interface (contrat restauration Full+Diff) 
- `RestoreService` impl (chaîne Full+Diff + déchiffrement CryptoSoft) 
- `RestoreView` + `RestoreViewModel` Avalonia (sélection job + point de restauration) 
- Tests `RestoreService` : tests xUnit (chaîne Diff, fichiers cryptés, destination alternative)
- [Recette V2] Restauration : backup → modifier fichiers → restaurer → fichiers identiques 
- [UML] Activité Restauration : sélection point → résolution chaîne → décryptage → écriture fichiers 

**Infra :**

- `PathResolver` service : chemins par défaut cross-platform (AppData Win / Library macOS / .config Linux) 

---

## Synthèse par développeur

### Julian Kerignard — Dev1 (EasyLog)

Responsable de l'évolution de la bibliothèque `EasyLog.dll`. Ajout du champ `EncryptionTimeMs` au `LogEntry`, architecture `ILogFormatter` avec pattern Strategy (`JsonFormatter` compat V1 + `XmlFormatter` avec XSD). Setup du projet de tests V2. Recette des fonctionnalités multi-formats et settings.

### Chloé — Dev2 (Backup)

Responsable du moteur de sauvegarde V2. Intégration du chiffrement via `IEncryptionService` / `CryptoSoftAdapter` (appel process externe). Adaptation de `DiffStrategy` pour les fichiers cryptés. `BusinessSoftwareDetector` pour la pause automatique. Suppression de la limite 5 jobs. Tests xUnit complets. Responsable de l'ensemble des diagrammes UML V2.

### Ilian — Dev3 (State/Persistence)

Responsable de la persistance V2. `SettingsRepository` pour la configuration (`encrypted_extensions`, `business_software_list`). `StateTracker` enrichi avec `INotifyPropertyChanged` pour les bindings GUI Avalonia. Rétrocompatibilité V1 → V2 (lecture des anciens fichiers JSON). Persistance de l'état pause/reprise.

### Samuel — Dev4 (UI Avalonia)

Responsable de l'interface graphique MVVM Avalonia cross-platform. `MainWindow` avec navigation, `JobEditViewModel`, `SettingsViewModel`, `JobListViewModel` avec `ObservableCollection` et commandes. `MarkupExtension` pour le switch de langue runtime FR/EN. Intégration `ProgressBar` via bindings `StateTracker`. Détection logiciel métier avec pause auto. Manuel utilisateur V2 avec screenshots.

