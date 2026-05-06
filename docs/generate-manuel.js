"use strict";

const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, Table, TableRow, TableCell,
  TableOfContents, WidthType, BorderStyle, AlignmentType, PageNumber, Footer,
  NumberFormat, convertInchesToTwip, ShadingType, UnderlineType,
  ExternalHyperlink, PageBreak, LevelFormat, VerticalAlign,
} = require("docx");

// ─── Colors & Fonts ────────────────────────────────────────────────────────────
const FONT = "Century Gothic";
const COLOR_DARK  = "1F2D3D";   // header background
const COLOR_ALT   = "F2F4F7";   // alternating row background
const COLOR_WHITE = "FFFFFF";
const COLOR_ACCENT = "2E6DB4";  // blue for headings/accent

const PT = (n) => n * 2; // half-points used by docx

// ─── Helpers ──────────────────────────────────────────────────────────────────

function h1(text) {
  return new Paragraph({
    text,
    heading: HeadingLevel.HEADING_1,
    spacing: { before: PT(18), after: PT(8) },
    run: { font: FONT, size: PT(16), bold: true, color: COLOR_ACCENT },
  });
}

function h2(text) {
  return new Paragraph({
    text,
    heading: HeadingLevel.HEADING_2,
    spacing: { before: PT(14), after: PT(6) },
    run: { font: FONT, size: PT(13), bold: true, color: COLOR_ACCENT },
  });
}

function h3(text) {
  return new Paragraph({
    text,
    heading: HeadingLevel.HEADING_3,
    spacing: { before: PT(10), after: PT(4) },
    run: { font: FONT, size: PT(11), bold: true },
  });
}

function body(text, options = {}) {
  return new Paragraph({
    children: [
      new TextRun({
        text,
        font: FONT,
        size: PT(11),
        ...options,
      }),
    ],
    spacing: { after: PT(6) },
  });
}

function bold(text) {
  return body(text, { bold: true });
}

function bullet(text, level = 0) {
  return new Paragraph({
    children: [new TextRun({ text, font: FONT, size: PT(11) })],
    bullet: { level },
    spacing: { after: PT(4) },
  });
}

function code(text) {
  return new Paragraph({
    children: [
      new TextRun({
        text,
        font: "Courier New",
        size: PT(10),
        shading: { type: ShadingType.CLEAR, fill: "F0F0F0" },
      }),
    ],
    spacing: { before: PT(4), after: PT(4) },
    indent: { left: convertInchesToTwip(0.3) },
  });
}

function pageBreak() {
  return new Paragraph({ children: [new PageBreak()] });
}

function empty(n = 1) {
  return Array.from({ length: n }, () =>
    new Paragraph({ children: [new TextRun({ text: "" })], spacing: { after: PT(4) } })
  );
}

// ─── Table helpers ────────────────────────────────────────────────────────────

function tableCell(text, { bg, color = "000000", bold: isBold = false, width } = {}) {
  const opts = { font: FONT, size: PT(10), bold: isBold, color };
  const cellProps = {
    verticalAlign: VerticalAlign.CENTER,
    margins: { top: PT(3), bottom: PT(3), left: PT(5), right: PT(5) },
  };
  if (bg) cellProps.shading = { type: ShadingType.CLEAR, fill: bg };
  if (width) cellProps.width = { size: width, type: WidthType.PERCENTAGE };

  return new TableCell({
    ...cellProps,
    children: [new Paragraph({
      children: [new TextRun({ text, ...opts })],
      alignment: AlignmentType.LEFT,
    })],
    borders: {
      top:    { style: BorderStyle.SINGLE, size: 1, color: "CCCCCC" },
      bottom: { style: BorderStyle.SINGLE, size: 1, color: "CCCCCC" },
      left:   { style: BorderStyle.SINGLE, size: 1, color: "CCCCCC" },
      right:  { style: BorderStyle.SINGLE, size: 1, color: "CCCCCC" },
    },
  });
}

function configTable(rows) {
  const headerRow = new TableRow({
    tableHeader: true,
    children: [
      tableCell("Champ (JSON)",    { bg: COLOR_DARK, color: COLOR_WHITE, bold: true, width: 25 }),
      tableCell("Type",            { bg: COLOR_DARK, color: COLOR_WHITE, bold: true, width: 12 }),
      tableCell("Défaut",          { bg: COLOR_DARK, color: COLOR_WHITE, bold: true, width: 15 }),
      tableCell("Description",     { bg: COLOR_DARK, color: COLOR_WHITE, bold: true, width: 48 }),
    ],
  });

  const dataRows = rows.map(([field, type, def, desc], i) =>
    new TableRow({
      children: [
        tableCell(field, { bg: i % 2 ? COLOR_ALT : COLOR_WHITE, width: 25 }),
        tableCell(type,  { bg: i % 2 ? COLOR_ALT : COLOR_WHITE, width: 12 }),
        tableCell(def,   { bg: i % 2 ? COLOR_ALT : COLOR_WHITE, width: 15 }),
        tableCell(desc,  { bg: i % 2 ? COLOR_ALT : COLOR_WHITE, width: 48 }),
      ],
    })
  );

  return new Table({
    width: { size: 100, type: WidthType.PERCENTAGE },
    rows: [headerRow, ...dataRows],
  });
}

// ─── Document sections ─────────────────────────────────────────────────────────

function coverPage() {
  return [
    ...empty(6),
    new Paragraph({
      children: [new TextRun({
        text: "EasySave V3.0 — Manuel Utilisateur",
        font: FONT, size: PT(26), bold: true, color: COLOR_ACCENT,
      })],
      alignment: AlignmentType.CENTER,
      spacing: { after: PT(10) },
    }),
    new Paragraph({
      children: [new TextRun({
        text: "Application de sauvegarde de fichiers",
        font: FONT, size: PT(14), color: "555555",
      })],
      alignment: AlignmentType.CENTER,
      spacing: { after: PT(6) },
    }),
    new Paragraph({
      children: [new TextRun({ text: "─".repeat(55), font: FONT, size: PT(11), color: "AAAAAA" })],
      alignment: AlignmentType.CENTER,
      spacing: { after: PT(20) },
    }),
    new Paragraph({
      children: [new TextRun({ text: "Version : 3.0", font: FONT, size: PT(11) })],
      alignment: AlignmentType.CENTER,
      spacing: { after: PT(4) },
    }),
    new Paragraph({
      children: [new TextRun({ text: "Date : Mai 2026", font: FONT, size: PT(11) })],
      alignment: AlignmentType.CENTER,
      spacing: { after: PT(4) },
    }),
    new Paragraph({
      children: [new TextRun({ text: "Auteur : Samuel (Dev4)", font: FONT, size: PT(11) })],
      alignment: AlignmentType.CENTER,
      spacing: { after: PT(4) },
    }),
    pageBreak(),
  ];
}

function tocSection() {
  return [
    new Paragraph({
      children: [new TextRun({ text: "Table des matières", font: FONT, size: PT(16), bold: true, color: COLOR_ACCENT })],
      spacing: { before: PT(10), after: PT(10) },
    }),
    new TableOfContents("Table des matières", {
      hyperlink: true,
      headingStyleRange: "1-3",
      stylesWithLevels: [],
    }),
    pageBreak(),
  ];
}

// ─── Section 1 ────────────────────────────────────────────────────────────────
function section1() {
  return [
    h1("1. Introduction"),

    h2("1.1 Présentation d'EasySave"),
    body("EasySave est un outil de sauvegarde de fichiers développé pour ProSoft. Il permet à un opérateur de définir jusqu'à 5 tâches de sauvegarde (jobs), chacune associant un répertoire source à un répertoire de destination. L'application copie les fichiers selon la stratégie choisie (sauvegarde complète ou différentielle) et enregistre le résultat dans des journaux quotidiens au format JSON."),
    body("En version 3, EasySave adopte un modèle d'exécution parallèle des jobs, un mécanisme de contrôle des fichiers volumineux (BigFileGate) et une console déportée TCP permettant de superviser et de piloter les sauvegardes à distance depuis une application graphique."),

    h2("1.2 Nouveautés V3 par rapport à V2"),
    bullet("Parallélisme des jobs : plusieurs tâches de sauvegarde s'exécutent simultanément, dans la limite du paramètre max_parallel_jobs (défaut : 4 threads)."),
    bullet("BigFileGate : les fichiers dont la taille dépasse le seuil configuré (large_file_threshold_kb) passent par un sémaphore à capacité 1. Un seul gros fichier est transféré à la fois sur l'ensemble des jobs parallèles ; les petits fichiers ne sont pas bloqués."),
    bullet("Console déportée TCP : une application Avalonia autonome (EasySave.RemoteConsole) se connecte à EasySave via TCP (port 9000 par défaut) et affiche en temps réel la progression de chaque job. Elle permet d'envoyer les commandes Pause, Play et Stop sans accès direct à la machine hébergeant EasySave."),
    bullet("Chiffrement de fichiers : intégration optionnelle avec CryptoSoft pour chiffrer certaines extensions lors de la copie."),

    h2("1.3 Prérequis système"),
    bullet("Runtime .NET 8.0 installé sur la machine."),
    bullet("Systèmes d'exploitation supportés : Windows, macOS, Linux."),
    bullet("Pour la console déportée : accès réseau TCP entre le poste client et la machine hébergeant EasySave (port configuré, 9000 par défaut)."),
    bullet("Pour le chiffrement (optionnel) : CryptoSoft installé et son chemin renseigné dans la configuration."),

    pageBreak(),
  ];
}

// ─── Section 2 ────────────────────────────────────────────────────────────────
function section2() {
  return [
    h1("2. Installation et lancement"),

    h2("2.1 Lancement de l'application principale"),
    body("Depuis le répertoire racine du projet, exécutez les commandes suivantes :"),
    code("cd src/EasySave"),
    code("dotnet run"),
    body("Sans argument, l'application affiche le menu interactif. Pour exécuter directement un ou plusieurs jobs, passez leur numéro en argument :"),
    code("dotnet run -- 1            # Exécute le job 1"),
    code("dotnet run -- 1-3          # Exécute les jobs 1, 2 et 3"),
    code("dotnet run -- 1;3;5        # Exécute les jobs 1, 3 et 5"),
    code("dotnet run -- 1-3;5        # Exécute les jobs 1, 2, 3 et 5"),
    body("La sélection est en base 1. Les plages sont définies par un tiret, les groupes par un point-virgule."),

    h2("2.2 Menu interactif"),
    body("Lorsque l'application est lancée sans argument, le menu suivant s'affiche :"),
    code("=== EasySave ==="),
    code("1. Add a backup job"),
    code("2. Remove a backup job"),
    code("3. List backup jobs"),
    code("4. Execute backup job(s)"),
    code("5. Change language"),
    code("0. Exit"),

    h2("2.3 Fichier de configuration"),
    body("Le fichier de configuration principal est appsettings.json, situé dans src/EasySave/. Il est lu au démarrage. Les données persistantes (jobs, état) sont stockées dans le répertoire utilisateur :"),
    bullet("Windows : %AppData%\\ProSoft\\EasySave\\"),
    bullet("Linux / macOS : ~/.config/ProSoft/EasySave/"),
    body("Ce répertoire contient :"),
    bullet("jobs.json — liste des jobs configurés."),
    bullet("state.json — état courant de chaque job (un seul fichier, singleton)."),
    bullet("Logs/ — journaux quotidiens au format JSON (un fichier par jour, nommé yyyy-MM-dd.json)."),

    pageBreak(),
  ];
}

// ─── Section 3 ────────────────────────────────────────────────────────────────
function section3() {
  return [
    h1("3. Gestion des jobs de sauvegarde"),

    h2("3.1 Créer un job"),
    body("Depuis le menu, choisissez l'option 1. L'application demande successivement :"),
    bullet("Nom du job (doit être unique, insensible à la casse)."),
    bullet("Chemin source : répertoire local, lecteur externe ou partage réseau (format UNC accepté)."),
    bullet("Chemin de destination : même types de chemins acceptés."),
    bullet("Type de sauvegarde : Full ou Differential."),
    body("Un maximum de 5 jobs peut être configuré simultanément. La source doit exister au moment de la création."),

    h2("3.2 Lister les jobs"),
    body("Option 3 du menu. Affiche la liste numérotée des jobs avec leur type, source et destination :"),
    code("{index}. {nom} [{type}] {source} -> {destination}"),

    h2("3.3 Supprimer un job"),
    body("Option 2 du menu. Saisir le numéro du job à supprimer. L'opération est immédiate et irréversible."),

    h2("3.4 Exécuter un ou plusieurs jobs"),
    body("Option 4 du menu, ou via argument en ligne de commande. Saisir la sélection de jobs selon la syntaxe décrite en section 2.1."),

    h2("3.5 Types de sauvegarde"),
    bullet("Full (complète) : copie tous les fichiers du répertoire source vers la destination, de manière récursive, quelle que soit leur date de modification."),
    bullet("Differential (différentielle) : copie uniquement les fichiers absents de la destination ou dont la date de modification est plus récente que la copie existante."),

    h2("3.6 Parallélisme"),
    body("En V3, les jobs sélectionnés s'exécutent en parallèle, dans la limite du paramètre max_parallel_jobs (défaut : 4). Si plus de jobs sont lancés simultanément, ils se répartissent sur les slots disponibles. Les erreurs dans un job n'interrompent pas les autres jobs en cours."),

    h2("3.7 BigFileGate"),
    body("Le BigFileGate est un mécanisme de protection contre la saturation réseau ou disque par les fichiers volumineux lors des sauvegardes parallèles."),
    body("Fonctionnement :"),
    bullet("Si la taille d'un fichier est inférieure au seuil (large_file_threshold_kb, défaut 4096 Ko = 4 Mo) : le fichier est copié normalement, sans attente."),
    bullet("Si la taille est supérieure ou égale au seuil : le job attend un jeton exclusif (SemaphoreSlim N=1). Un seul fichier volumineux est transféré à la fois sur l'ensemble des jobs parallèles. Dès la fin du transfert, le jeton est libéré et le suivant peut commencer."),
    body("Les petits fichiers ne sont jamais bloqués par les gros fichiers : les deux catégories utilisent des canaux séparés."),

    pageBreak(),
  ];
}

// ─── Section 4 ────────────────────────────────────────────────────────────────
function section4() {
  return [
    h1("4. Console déportée (EasySave.RemoteConsole)"),

    h2("4.1 Présentation"),
    body("EasySave.RemoteConsole est une application graphique autonome développée avec Avalonia UI. Elle se connecte à l'application EasySave principale via une connexion TCP et offre une vue en temps réel de l'état des jobs de sauvegarde en cours, ainsi que les commandes Pause, Play et Stop."),
    body("La console déportée est utile lorsque l'opérateur ne dispose pas d'un accès direct à la machine hébergeant EasySave (connexion à distance, salle serveur, etc.)."),

    h2("4.2 Lancement de la console déportée"),
    body("La console déportée est un projet séparé. Pour la lancer :"),
    code("cd EasySave.RemoteConsole"),
    code("dotnet run"),
    body("Sur Windows, l'exécutable compilé est EasySave.RemoteConsole.exe. Sur Linux/macOS : EasySave.RemoteConsole."),
    body("Important : EasySave doit être démarré avec la console déportée activée (remote_console_enabled: true dans la configuration) avant que la console déportée ne tente de se connecter."),

    h2("4.3 Interface"),
    body("La fenêtre principale de la console déportée comprend :"),
    h3("Barre de connexion (en haut)"),
    bullet("Champ Host : adresse IP ou nom d'hôte de la machine hébergeant EasySave (défaut : 127.0.0.1)."),
    bullet("Champ Port : port TCP à utiliser (défaut : 9000, plage 1-65535)."),
    bullet("Bouton Connexion / Connexion : établit la connexion TCP."),
    bullet("Bouton Déconnexion / Disconnect : ferme la connexion."),
    bullet("Indicateur d'état : affiche Disconnected, Connecting, Connected ou Error."),

    h3("Liste des jobs (zone centrale)"),
    body("Chaque job affiché présente :"),
    bullet("Nom du job (160 px)."),
    bullet("Barre de progression (0 à 100 %)."),
    bullet("Statut textuel (Done, Running, Paused)."),
    bullet("Trois boutons d'action : Pause, Play, Stop."),

    h2("4.4 Connexion à EasySave"),
    body("Procédure :"),
    bullet("Vérifier que EasySave est lancé avec remote_console_enabled: true."),
    bullet("Saisir l'adresse IP de la machine hébergeant EasySave dans le champ Host."),
    bullet("Vérifier que le port correspond à remote_console_port dans la configuration d'EasySave (défaut : 9000)."),
    bullet("Cliquer sur le bouton Connexion."),
    bullet("L'indicateur d'état passe à Connected. La liste des jobs apparaît avec leur progression en temps réel."),

    h2("4.5 Commandes disponibles"),
    body("Depuis la console déportée, pour chaque job listé :"),
    bullet("Pause : suspend le job à la prochaine limite de fichier. Le job passe à l'état Paused."),
    bullet("Play : reprend un job en pause ou démarre un job idle. Le job passe à l'état Running."),
    bullet("Stop : annule le job en cours. Le job s'arrête immédiatement."),
    body("Ces commandes sont envoyées au serveur TCP d'EasySave sous forme de messages JSON (protocole interne newline-delimited)."),

    h2("4.6 Reconnexion automatique"),
    body("En cas de coupure réseau ou de redémarrage d'EasySave, le client TCP tente de se reconnecter automatiquement selon la séquence de délais suivante :"),
    bullet("1 s → 2 s → 5 s → 10 s (puis 10 s en boucle)."),
    body("L'indicateur d'état passe à Connecting pendant les tentatives. La reconnexion est transparente pour l'utilisateur."),

    h2("4.7 Port TCP par défaut"),
    body("Le port TCP par défaut est 9000. Il est configurable via le champ remote_console_port dans appsettings.json (côté EasySave) et doit être saisi manuellement dans le champ Port de la console déportée."),

    pageBreak(),
  ];
}

// ─── Section 5 ────────────────────────────────────────────────────────────────
function section5() {
  const configRows = [
    ["language",                    "string",  "\"en\"",   "Langue de l'interface. Valeurs : \"en\" (anglais) ou \"fr\" (français)."],
    ["encrypted_extensions",        "string[]", "[]",      "Extensions de fichiers à chiffrer via CryptoSoft lors de la copie (ex. [\".pdf\", \".docx\"])."],
    ["business_software",           "string[]", "[]",      "Noms d'exécutables métier. Si l'un d'eux est détecté en cours d'exécution, les jobs sont mis en pause automatiquement."],
    ["log_format",                  "string",  "\"json\"", "Format des journaux de transfert. Valeur actuelle : \"json\"."],
    ["crypto_soft.path",            "string",  "\"\"",     "Chemin absolu vers l'exécutable CryptoSoft. Laisser vide pour désactiver le chiffrement."],
    ["crypto_soft.timeout_ms",      "int",     "30000",    "Délai d'attente maximal (en millisecondes) pour une opération CryptoSoft."],
    ["large_file_threshold_kb",     "int",     "4096",     "Seuil BigFileGate en kilo-octets. Les fichiers >= ce seuil passent par le sémaphore N=1 (défaut : 4 Mo)."],
    ["remote_console_enabled",      "bool",    "false",    "Active le serveur TCP de la console déportée. Mettre à true pour permettre les connexions distantes."],
    ["remote_console_port",         "int",     "9000",     "Port TCP d'écoute du serveur de console déportée."],
    ["max_parallel_jobs",           "int",     "4",        "Nombre maximum de jobs de sauvegarde s'exécutant simultanément."],
  ];

  return [
    h1("5. Configuration avancée"),

    h2("5.1 Tableau des paramètres"),
    body("Le fichier appsettings.json est situé dans src/EasySave/. Exemple de fichier complet :"),
    code("{"),
    code("  \"language\": \"en\","),
    code("  \"encrypted_extensions\": [\".pdf\", \".docx\", \".xlsx\"],"),
    code("  \"business_software\": [\"calc.exe\"],"),
    code("  \"log_format\": \"json\","),
    code("  \"crypto_soft\": { \"path\": \"\", \"timeout_ms\": 30000 },"),
    code("  \"large_file_threshold_kb\": 4096,"),
    code("  \"remote_console_enabled\": false,"),
    code("  \"remote_console_port\": 9000,"),
    code("  \"max_parallel_jobs\": 4"),
    code("}"),
    ...empty(1),
    configTable(configRows),
    ...empty(1),

    h2("5.2 Internationalisation"),
    body("EasySave supporte deux langues : anglais (en) et français (fr). La langue active est définie par le champ language dans appsettings.json. Elle peut aussi être changée à chaud via l'option 5 du menu interactif."),
    body("Les chaînes de l'interface utilisateur sont stockées dans des fichiers JSON de ressources :"),
    bullet("src/EasySave/Resources/en.json et fr.json — application principale."),
    bullet("EasySave.RemoteConsole/Resources/en.json et fr.json — console déportée."),

    pageBreak(),
  ];
}

// ─── Section 6 ────────────────────────────────────────────────────────────────
function section6() {
  return [
    h1("6. Dépannage"),

    h2("6.1 La console déportée ne se connecte pas"),
    bullet("Vérifier qu'EasySave est bien lancé sur la machine cible."),
    bullet("Vérifier que remote_console_enabled est à true dans appsettings.json."),
    bullet("Vérifier que le port saisi dans la console (défaut 9000) correspond à remote_console_port dans la configuration d'EasySave."),
    bullet("Vérifier que le pare-feu de la machine hébergeant EasySave autorise les connexions entrantes TCP sur ce port."),
    bullet("Vérifier que l'adresse IP saisie dans le champ Host est correcte et joignable depuis le poste client (ping)."),
    bullet("La console tente de se reconnecter automatiquement en cas d'échec — attendre quelques secondes avant d'intervenir."),

    h2("6.2 Un job reste en état Paused indéfiniment"),
    bullet("Depuis la console déportée : cliquer sur le bouton Play du job concerné."),
    bullet("Si la console déportée n'est pas disponible : relancer EasySave. L'état est persisté dans state.json ; les jobs Paused au redémarrage peuvent nécessiter un relancement manuel."),

    h2("6.3 Erreur au démarrage d'EasySave"),
    bullet("Vérifier que .NET 8 Runtime est installé : dotnet --version doit afficher 8.x.x."),
    bullet("Vérifier que appsettings.json est un JSON valide et ne contient pas de champs inconnus."),
    bullet("Si le message « Backup configuration file is temporarily locked » apparaît, une instance d'EasySave est déjà en cours ou un verrou de fichier n'a pas été libéré. Patienter ou redémarrer la machine."),

    h2("6.4 Fichier de log illisible ou absent"),
    bullet("Les logs sont créés dans {DataRoot}/Logs/ au format yyyy-MM-dd.json (heure locale)."),
    bullet("Si le répertoire est absent, vérifier les droits d'écriture de l'utilisateur sur %AppData%\\ProSoft\\EasySave\\ (Windows)."),

    h2("6.5 Le chiffrement ne fonctionne pas"),
    bullet("Vérifier que crypto_soft.path pointe vers un exécutable CryptoSoft valide."),
    bullet("Vérifier que les extensions visées figurent dans encrypted_extensions."),
    bullet("Vérifier que crypto_soft.timeout_ms est suffisant pour les fichiers concernés."),
  ];
}

// ─── Assemble & write ─────────────────────────────────────────────────────────

async function main() {
  const doc = new Document({
    creator: "Samuel (Dev4)",
    title: "EasySave V3.0 — Manuel Utilisateur",
    description: "Manuel utilisateur EasySave V3 — ProSoft",
    styles: {
      default: {
        document: {
          run: { font: FONT, size: PT(11) },
        },
        heading1: {
          run: { font: FONT, size: PT(16), bold: true, color: COLOR_ACCENT },
          paragraph: { spacing: { before: PT(18), after: PT(8) } },
        },
        heading2: {
          run: { font: FONT, size: PT(13), bold: true, color: COLOR_ACCENT },
          paragraph: { spacing: { before: PT(14), after: PT(6) } },
        },
        heading3: {
          run: { font: FONT, size: PT(11), bold: true },
          paragraph: { spacing: { before: PT(10), after: PT(4) } },
        },
      },
    },
    sections: [
      {
        properties: {
          page: {
            margin: {
              top:    convertInchesToTwip(0.984), // 2.5 cm
              bottom: convertInchesToTwip(0.984),
              left:   convertInchesToTwip(0.984),
              right:  convertInchesToTwip(0.984),
            },
          },
        },
        footers: {
          default: new Footer({
            children: [
              new Paragraph({
                alignment: AlignmentType.CENTER,
                children: [
                  new TextRun({ text: "EasySave V3.0 — Manuel Utilisateur    |    Page ", font: FONT, size: PT(9), color: "888888" }),
                  new TextRun({ children: [PageNumber.CURRENT], font: FONT, size: PT(9), color: "888888" }),
                  new TextRun({ text: " / ", font: FONT, size: PT(9), color: "888888" }),
                  new TextRun({ children: [PageNumber.TOTAL_PAGES], font: FONT, size: PT(9), color: "888888" }),
                ],
              }),
            ],
          }),
        },
        children: [
          ...coverPage(),
          ...tocSection(),
          ...section1(),
          ...section2(),
          ...section3(),
          ...section4(),
          ...section5(),
          ...section6(),
        ],
      },
    ],
  });

  const buffer = await Packer.toBuffer(doc);
  const outPath = path.join(__dirname, "ManuelUtilisateur_EasySave_V3.docx");
  require("fs").writeFileSync(outPath, buffer);
  console.log(`Document généré : ${outPath}`);
}

main().catch((err) => { console.error(err); process.exit(1); });
