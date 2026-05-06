"use strict";

const { h1, body, empty, configTable } = require("../helpers");

const CONFIG_ROWS = [
  ["language",                "string",   "\"en\"",   "Langue de l'interface : \"en\" (anglais) ou \"fr\" (français)."],
  ["encrypted_extensions",    "string[]", "[]",       "Extensions chiffrées via CryptoSoft lors de la copie (ex. [\".pdf\"])."],
  ["business_software",       "string[]", "[]",       "Exécutables métier : les jobs sont mis en pause automatiquement s'ils sont actifs."],
  ["log_format",              "string",   "\"json\"", "Format des journaux de transfert. Valeur : \"json\"."],
  ["crypto_soft.path",        "string",   "\"\"",     "Chemin vers CryptoSoft. Laisser vide pour désactiver le chiffrement."],
  ["crypto_soft.timeout_ms",  "int",      "30000",    "Délai max pour une opération CryptoSoft (ms)."],
  ["large_file_threshold_kb", "int",      "4096",     "Seuil BigFileGate (Ko). Fichiers >= seuil → sémaphore N=1."],
  ["remote_console_enabled",  "bool",     "false",    "Active le serveur TCP de la console déportée."],
  ["remote_console_port",     "int",      "9000",     "Port TCP d'écoute du serveur de console déportée."],
  ["max_parallel_jobs",       "int",      "4",        "Nombre max de jobs s'exécutant en parallèle."],
];

module.exports = function section5() {
  return [
    h1("5. Configuration (appsettings.json)"),
    body("Fichier : src/EasySave/appsettings.json. Relancez l'application après modification."),
    empty(),
    configTable(CONFIG_ROWS),
    empty(),
    body("La langue peut aussi être changée à chaud via l'option 5 du menu interactif."),
  ];
};
