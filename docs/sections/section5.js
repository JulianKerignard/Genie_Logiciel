"use strict";

const { h1, body, empty, configTable } = require("../helpers");

const CONFIG_ROWS = [
  ["language",                "string",   "\"en\"",          "Langue de l'interface : \"en\" ou \"fr\"."],
  ["log_format",              "string",   "\"json\"",        "Format des journaux : \"json\" ou \"xml\"."],
  ["log_mode",                "string",   "\"Local\"",       "Routage des journaux : Local · Centralized · Both."],
  ["log_centralizer_url",     "string",   "\"\"",            "URL du service Docker LogCentralizer (modes Centralized / Both)."],
  ["encrypted_extensions",    "string[]", "[]",              "Extensions chiffrées via CryptoSoft (ex. [\".pdf\", \".docx\"])."],
  ["priority_extensions",     "string[]", "[]",              "Extensions prioritaires (copiées avant tout fichier non prioritaire)."],
  ["business_software",       "string[]", "[]",              "Exécutables métier : pause auto des jobs si l'un est lancé."],
  ["crypto_soft.path",        "string",   "\"\"",            "Chemin vers CryptoSoft (mono-instance). Vide = pas de chiffrement."],
  ["large_file_threshold_kb", "int",      "4096",            "Seuil (Ko) au-delà duquel les fichiers passent par le sémaphore N=1."],
  ["max_parallel_jobs",       "int",      "4",               "Nombre max de jobs s'exécutant en parallèle."],
  ["remote_console_enabled",  "bool",     "false",           "Active le serveur TCP de la console déportée."],
  ["remote_console_port",     "int",      "9000",            "Port TCP du serveur de console déportée."],
];

module.exports = function section5() {
  return [
    h1("5. Configuration (appsettings.json)"),
    body("Fichier : src/EasySave/appsettings.json. Redémarrer l'application après modification."),
    empty(),
    configTable(CONFIG_ROWS),
  ];
};
