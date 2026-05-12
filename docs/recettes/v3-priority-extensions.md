# Recette V3 — Extensions prioritaires (gate cross-jobs)

**Critère grille tuteur V3 (obligatoire)** :

> *« Aucune sauvegarde d'un fichier non prioritaire ne peut se faire tant
> qu'il y a des extensions prioritaires en attente sur au moins un
> travail. »*

L'implémentation v3 utilise un `IPriorityGate` partagé entre tous les
jobs : chaque job s'enregistre en début de course avec son nombre de
fichiers prioritaires, décrémente le compteur après chaque prio copié,
et se désinscrit en fin de boucle. Les fichiers **non prioritaires** de
n'importe quel job se garent sur le gate tant que la somme cross-jobs
des prio restants est non nulle.

## Pré-requis

- EasySave v3 buildé en Release (`dotnet build EasySave.sln -c Release`).
- GUI Avalonia (`dotnet run --project src/EasySave.UI`).
- Deux dossiers source distincts pour deux jobs parallèles. **3 fichiers
  prioritaires + 3 non prioritaires par job** (sample symétrique pour
  rendre l'assertion "tous les `.docx` avant tous les `.txt`" évidente
  sur la timeline) :
  - **Job A** : `Demo\A\` contenant `a1.docx`, `a2.docx`, `a3.docx`,
    `a1.txt`, `a2.txt`, `a3.txt`.
  - **Job B** : `Demo\B\` contenant `b1.docx`, `b2.docx`, `b3.docx`,
    `b1.txt`, `b2.txt`, `b3.txt`.
- Garder une console ouverte sur `tail -F "$APPDATA/ProSoft/EasySave/state.json"`
  (Git Bash / WSL sur Windows) ou `tail -F ~/.config/ProSoft/EasySave/state.json`
  (Linux/macOS) pour suivre l'évolution en temps réel. `-F` (follow by
  name) plutôt que `-f` (follow by descriptor) parce que `state.json` est
  réécrit via rename atomique : `tail -f` raterait les updates sur macOS
  et Git Bash. `$APPDATA` au lieu de `%AppData%` parce que la version
  cmd.exe de `tail` ne suit pas le file, on a besoin de l'environnement
  POSIX où `$APPDATA` est exposé par Git Bash.

## Configuration

Éditer `appsettings.json` :

```json
{
  "priority_extensions": [".docx"],
  "max_parallel_jobs": 2,
  "log_format": "json"
}
```

Le matching est case-insensitive. La forme attendue est celle retournée
par `Path.GetExtension` (leading dot inclus). Relancer la GUI pour que
le BackupManager prenne en compte la nouvelle liste.

## Procédure — un seul job

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Créer Job A pointant vers `Demo\A\`. Cliquer **Run**. | Les fichiers `.docx` sont copiés **avant** les `.txt` dans la cible (vérifier via les mtimes côté cible ou la séquence dans le log journalier). |
| 2 | Inspecter le log JSON. | Les lignes `FileTransfer` apparaissent d'abord pour les `.docx`, ensuite pour les `.txt`. Pas de mélange. |

## Procédure — deux jobs en parallèle (le vrai test V3)

| # | Action | Résultat attendu |
|---|---|---|
| 1 | Créer Job A et Job B comme décrit ci-dessus. | 2 jobs `Idle` dans la GUI. |
| 2 | Cliquer **Run All**. | Les 2 jobs passent à `Active` dans `state.json` (chacun avec `FilesRemaining = 6`). |
| 3 | Observer la séquence de copie dans le log JSON journalier (`yyyy-MM-dd.json`). | **Les 6 premières lignes `FileTransfer`** doivent toutes être des `.docx` (3 de A + 3 de B, ordre cross-job indifférent). **Les 6 lignes suivantes** sont les `.txt`. Aucun mélange. |
| 4 | À mi-parcours (vers la 6e copie), inspecter `state.json`. | Les deux jobs sont `Active`. `CurrentSource` pointe vers des `.docx` ou vient de passer au premier `.txt`. `FilesRemaining` reflète les 6 prio écoulés. |
| 5 | Attendre la fin. | Les 2 jobs `Inactive` dans `state.json`, `FilesRemaining = 0`, `Progress = 100`. Tous les fichiers présents en cible, chacun copié une seule fois. |

### Vérification log + state.json (oracle)

Une fois la course terminée :

```bash
# Linux/macOS — extraire l'ordre des fichiers copiés depuis le log.
# Les lignes de copie de fichier ont EventType = null (omis du JSON via
# JsonIgnore(WhenWritingNull)) ; les lignes V3 d'évènement non-fichier
# (BigFileEnqueued, RemoteConsoleConnected, …) ont la propriété
# explicitement présente.
jq -r '.[] | select(.EventType==null) | .SourceFile' \
    "$HOME/.config/ProSoft/EasySave/Logs/$(date +%Y-%m-%d).json" \
    | awk -F'/' '{print $NF}'
```

La sortie attendue (l'ordre cross-job entre A et B peut varier, mais les
**6 `.docx` viennent TOUS avant les 6 `.txt`**) :

```
a1.docx
b1.docx
a2.docx
b2.docx
a3.docx
b3.docx
a1.txt
b1.txt
a2.txt
b2.txt
a3.txt
b3.txt
```

## Test négatif — `priority_extensions` vide

Remettre le setting à `[]` dans `appsettings.json` puis relancer la GUI
avec les **mêmes** dossiers Job A / Job B.

| # | Action | Résultat attendu |
|---|---|---|
| 1 | `priority_extensions: []`, **Run All**. | Les 2 jobs passent à `Active`. |
| 2 | Inspecter le log journalier. | Pour chaque job, l'ordre suit le **tri lexicographique ordinal** que `BackupManager` applique systématiquement à la liste des fichiers (sur `file.FullName`, comparé en `StringComparer.Ordinal`). Avec les noms `a1.docx, a1.txt, a2.docx, ...` dans un dossier source unique, le résultat est un **mélange** `.docx` / `.txt` — preuve qu'aucun tri prio-d'abord ne s'applique. |
| 3 | Comparer avec la course précédente. | La différence prouve que le tri prio-d'abord venait bien du gate, pas d'un effet de bord du système de fichiers. |

## Critères d'acceptation

- [ ] Dans **un job seul**, les fichiers prioritaires sont copiés avant
      les non prioritaires.
- [ ] Dans **deux jobs parallèles**, **aucun** fichier non prioritaire
      (peu importe le job) n'est copié tant qu'un autre job a encore une
      `.docx` en attente.
- [ ] Quand un job est annulé en plein milieu (Stop), ses fichiers
      prioritaires non encore copiés **ne bloquent pas indéfiniment** les
      non-prio des autres jobs (l'`UnregisterJob` en `finally` les
      abandonne).
- [ ] Le critère reste vérifiable via les **mtimes des fichiers cibles**
      ou la **séquence des lignes `FileTransfer`** dans le log JSON.
- [ ] **Test négatif** : avec `priority_extensions: []` et les mêmes
      sources, l'ordre n'est plus prio-d'abord (mélange `.docx` / `.txt`
      dans la séquence du log). Confirme que le tri vient bien du gate.
- [ ] **state.json** reflète l'état attendu pendant l'exécution : les
      deux jobs `Active` simultanément, `FilesRemaining` décroît au fur
      et à mesure des copies. La phase "parqué sur le gate" est
      observable par un job dont `FilesRemaining > 0` ET `CurrentSource`
      reste **figé** (= chemin du dernier `.docx` copié) sur plusieurs
      cycles de polling, en attendant que l'autre job finisse ses prio.
      `CurrentSource` n'est jamais réécrit à vide pendant l'attente —
      il garde la dernière valeur posée par `BackupManager` après
      copie.

## Cas limites

- **Aucune extension prioritaire configurée** (`priority_extensions: []`) :
  le tri intra-job devient un no-op, le gate est toujours signalé, aucun
  ralentissement par rapport à v2. **Le système doit se comporter
  exactement comme avant la feature.**
- **Tous les fichiers d'un job sont prioritaires** : le gate ne bloque
  jamais ce job pour ses propres fichiers ; il peut bloquer d'autres
  jobs tant qu'il n'a pas fini ses prio.
- **Un job échoue / est stoppé avant d'avoir copié toutes ses prio** :
  l'`UnregisterJob` en `finally` du `BackupManager` jette ses prio
  restantes ; le gate se signale dès que les autres jobs ont terminé
  leurs prio.
- **Resume** : les fichiers déjà copiés avant la pause ne réapparaissent
  pas dans `toCopy` au resume (le cursor les filtre), donc la
  re-registration au PriorityGate utilise le nombre de **prio restants**,
  pas le nombre initial.

## Couverture automatique

- `tests/EasySave.Tests.V2/PriorityGateTests.cs` — 10 cas sur le gate
  pur : signalisation immédiate avec 0 prio, blocage avec N prio
  pending, cross-job avec un job qui n'a que des non-prio, abandon des
  prio restantes via `UnregisterJob`, token cancelled sans corruption,
  no-op sur `MarkPriorityFileDone` d'un nom inconnu, garde contre les
  comptes négatifs, validation des args.
- `tests/EasySave.Tests/BackupManagerPriorityTests.cs` — 2 cas
  d'intégration : ordre prio-d'abord dans un job seul (vérifié via la
  séquence des `LogEntry`), et **JobA.txt attend JobB.docx** sur un
  gate partagé.

## Si la recette échoue

| Symptôme | Cause probable | Vérification |
|---|---|---|
| Les `.txt` partent **avant** les `.docx` dans un même job | `priority_extensions` mal orthographié (`docx` au lieu de `.docx` ?) | Confirmer le matching dans `BackupManager.IsPriority` — `Path.GetExtension` renvoie avec le leading dot, donc `priority_extensions` doit lui aussi avoir le dot. |
| Job A ne attend pas Job B | Les deux jobs n'utilisent pas le même `IPriorityGate` singleton | Vérifier que `App.axaml.cs` enregistre `IPriorityGate` en singleton (un seul `PriorityGate()` partagé). |
| Job A reste bloqué à vie même après que Job B a fini | `UnregisterJob` n'a pas été appelé sur B (exception non rattrapée ?) | `BackupManager.RunJob` doit `UnregisterJob` dans le `finally` — vérifier qu'aucune exception ne saute par-dessus. |
| `state.json` montre `FilesRemaining` qui ne décroît pas pendant l'attente sur le gate | Comportement attendu : le job est parqué avant la copie, son `FilesRemaining` ne bouge que quand un fichier est effectivement copié. | Pas un bug — observable dans la GUI sous forme d'un job `Active` mais sans progression visible le temps que les autres jobs finissent leurs prio. |
