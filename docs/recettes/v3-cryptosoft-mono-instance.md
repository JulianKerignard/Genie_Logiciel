# Recette V3 — CryptoSoft Mono-Instance

**Critère grille tuteur V3 (obligatoire)** :

> *« Le logiciel CryptoSoft est Mono-instance (il ne peut être exécuté en
> simultanée sur un même ordinateur). Vous devez modifier CryptoSoft pour
> le rendre Mono-Instance et gérer les éventuels problèmes liés à cette
> restriction. »*

L'implémentation v3 enforce la contrainte côté **`CryptoSoftAdapter`** via un
`Mutex` système nommé `Global\ProSoft.CryptoSoft.SingleInstance` (cf.
`docs/cryptosoft-integration.md` pour le contrat complet). Le gate sérialise
tous les callers — jobs parallèles dans le même process EasySave, ou deux
instances EasySave sur la même machine.

## Pré-requis

- EasySave v3 buildé en Release (`dotnet build EasySave.sln -c Release`).
- Un `CryptoSoft.exe` (ou stub fake `sleep`-style sur Linux/macOS pour la démo).
- 2 jobs Full avec des sources distinctes contenant chacun **3 fichiers .pdf**
  (ou autre extension listée dans `encrypted_extensions`). Les fichiers doivent
  être assez gros (~5-10 MB chacun) pour que le cryptage prenne quelques
  secondes — sinon la fenêtre de chevauchement est trop courte pour observer
  la sérialisation.
- `appsettings.json` :

  ```json
  {
    "encrypted_extensions": [".pdf"],
    "crypto_soft": {
      "path": "C:\\Program Files\\ProSoft\\CryptoSoft\\CryptoSoft.exe",
      "timeout_ms": 30000
    },
    "max_parallel_jobs": 2
  }
  ```

## Procédure — deux jobs parallèles avec cryptage

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Créer Job A et Job B, chacun pointant vers une source de 3 `.pdf`. | 2 jobs `Idle` dans la GUI. |
| 2 | Cliquer **Run All**. | Les 2 jobs passent à `Active`. La GUI affiche les 2 progress bars qui montent. |
| 3 | Pendant l'exécution, ouvrir le **gestionnaire de tâches** (Windows) ou `ps aux \| grep CryptoSoft` (Linux/macOS). | À **tout instant**, on observe au maximum **une seule** instance de `CryptoSoft.exe` en cours d'exécution, jamais deux simultanément. |
| 4 | Inspecter les mtimes des fichiers cible. | Les 6 fichiers `.pdf` (3 de A + 3 de B) sont copiés et cryptés, jamais deux en même temps côté CryptoSoft. La durée totale = somme des durées individuelles (sérialisé), pas le max (parallèle). |
| 5 | Inspecter le log journalier. | Chaque ligne `FileTransfer` a `EncryptionTimeMs > 0`. Pas de `EncryptionTimeMs = -1` (ce qui indiquerait un échec / timeout). |
| 6 | Attendre la fin. | Les 2 jobs `Inactive` avec `FilesRemaining = 0`. Tous les fichiers cible sont cryptés. |

## Procédure — deux processus EasySave concurrents (le vrai test mono-instance)

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Lancer une première instance EasySave et démarrer Job A. | Job A `Active`, CryptoSoft.exe visible dans la liste des processus. |
| 2 | Pendant que Job A crypte un fichier, lancer une **deuxième instance** EasySave et démarrer Job B (sur un autre dossier). | Job B passe `Active`. Au gestionnaire de tâches, **toujours une seule** instance de CryptoSoft.exe à la fois. |
| 3 | Observer les logs des deux instances. | Le log de Job B contient des `EncryptionTimeMs` cohérents (cryptage effectivement réalisé), pas d'erreur. Le seul effet visible est que Job B prend plus longtemps à finir parce qu'il attend que Job A relâche le gate à chaque fichier. |
| 4 | Variante stress — lancer **3 instances** EasySave en parallèle. | Toujours zéro CryptoSoft.exe simultanés. Pas de crash, pas d'exception, pas de log d'erreur côté EasySave. |

## Procédure — test du timeout de contention

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Configurer `crypto_soft.timeout_ms = 1000` (1 s). Le `lockWaitMs` dérivé sera de 2 s. | Setting saisi. |
| 2 | Forcer un fichier énorme (~500 MB de `.pdf`) sur Job A pour que le cryptage dure > 5 s. | Job A `Active`, CryptoSoft.exe en cours. |
| 3 | Démarrer Job B avec un petit `.pdf` pendant que Job A est encore en cryptage. | Job B attend le gate pendant 2 s puis **abandonne le cryptage** de ce fichier (log : `EncryptionTimeMs = -1`, message Trace `Mono-Instance lock contention timeout`). Le job continue avec les autres fichiers (le copy plain reste OK, seule l'encryption échoue). |
| 4 | Remettre `timeout_ms = 30000` et relancer. | Plus de timeout, Job B attend patiemment puis crypte normalement. |

## Critères d'acceptation

- [ ] **Aucun instant** où deux CryptoSoft.exe sont visibles dans la liste
      des processus, quel que soit le nombre de jobs parallèles ou de
      processus EasySave concurrents.
- [ ] Les fichiers cible sont tous correctement cryptés (durée > 0 dans
      le log journalier, pas de `-1`).
- [ ] Le timeout de contention (`2 × crypto_soft.timeout_ms`) déclenche
      un `EncryptResult.Failed` propre sans hang ni crash.
- [ ] `Trace.TraceWarning` consigne le timeout dans le diagnostic du
      host (visible via `dotnet-trace` ou la console quand l'app tourne
      en debug).
- [ ] Un kill -9 d'une instance EasySave qui détenait le gate n'empêche
      pas une autre instance d'acquérir le mutex au tour suivant
      (gestion `AbandonedMutexException`).

## Couverture automatique

- `tests/EasySave.Tests/CryptoSoftAdapterTests.cs` (méthodes ajoutées en
  v3) :
  - `Encrypt_TwoConcurrentCalls_AreSerialized_NotParallel` — deux
    `Encrypt` lancés en parallèle prennent ≥ 2× la durée d'un seul,
    prouvant que la deuxième a attendu sur le mutex.
  - `Encrypt_LockTimeout_ReturnsFailed` — un holder qui tient le gate
    1.5 s avec un waiter configuré à 600 ms de budget : le waiter sort
    `Failed` en ~600 ms (pas d'attente infinie).

Les deux tests sont marqués `[SkippableFact]` et utilisent un script
shell généré dans `Path.GetTempPath()` comme fake CryptoSoft. Sur
Windows ils sont skip (faute d'équivalent built-in à `/bin/sh`) — la
validation y passe par la recette manuelle ci-dessus.

## Si la recette échoue

| Symptôme | Cause probable | Vérification |
|---|---|---|
| Deux CryptoSoft.exe visibles simultanément | Le mutex n'est pas acquis avant `Process.Start`. | Inspecter `CryptoSoftAdapter.Encrypt` : la séquence doit être `AcquireGate → RunCryptoSoftProcess → ReleaseMutex` dans un try/finally. |
| Un job hang sans timeout après ~30 s | `_lockWaitMs` n'a pas été câblé ou est resté à `Timeout.Infinite`. | Vérifier le calcul `_lockWaitMs = timeoutMs * 2` dans le constructeur. |
| Sur Linux/macOS, les deux instances de processus EasySave différents NE serialisent pas | Le mutex est per-runtime, pas system-wide, sur ces OS. C'est une limite connue de .NET sur Unix. | Documenter dans le manuel admin ; en pratique le déploiement Mono-Instance opérationnel est Windows. |
| `AbandonedMutexException` non gérée fait crasher EasySave | Le `try/catch` autour de `WaitOne` n'est pas en place. | Vérifier `CryptoSoftAdapter.AcquireGate` — l'exception doit être catch et retourner `true` (semantic OS-driven recovery). |
