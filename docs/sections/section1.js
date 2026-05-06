"use strict";

const { h1, body, bullet } = require("../helpers");

module.exports = function section1() {
  return [
    h1("1. Introduction"),
    body(
      "EasySave est un outil de sauvegarde de fichiers ProSoft (.NET 8). " +
      "Il permet de définir jusqu'à 5 tâches de sauvegarde (jobs) et de les exécuter " +
      "vers des destinations locales, externes ou réseau."
    ),
    body("Prérequis : .NET 8 Runtime · Systèmes : Windows, macOS, Linux."),
    body("Nouveautés V3 :"),
    bullet(
      "Parallélisme : plusieurs jobs s'exécutent simultanément " +
      "(max_parallel_jobs, défaut 4) ; erreurs isolées par job."
    ),
    bullet(
      "BigFileGate : les fichiers dont la taille est >= large_file_threshold_kb " +
      "(défaut 4 096 Ko) passent par un sémaphore N=1 — un seul gros transfert actif " +
      "à la fois. Les petits fichiers ne sont pas concernés."
    ),
    bullet(
      "Console déportée TCP (port 9000 par défaut) : supervision et contrôle " +
      "des jobs à distance via une application Avalonia autonome."
    ),
  ];
};
