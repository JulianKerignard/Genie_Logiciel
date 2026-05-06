"use strict";

const { h1, body, bullet } = require("../helpers");

module.exports = function section3() {
  return [
    h1("3. Gestion des jobs"),
    body("Maximum 5 jobs. Types : Full (copie totale récursive) · Differential (nouveaux fichiers ou modifiés uniquement)."),
    body("Menu interactif :"),
    bullet("1 — Créer un job : nom (unique, insensible à la casse), source, destination, type"),
    bullet("2 — Supprimer un job"),
    bullet("3 — Lister les jobs"),
    bullet("4 — Exécuter un ou plusieurs jobs"),
    bullet("5 — Changer la langue (en / fr)"),
    body(
      "Parallélisme : les jobs sélectionnés s'exécutent en parallèle dans la limite de " +
      "max_parallel_jobs (défaut 4). Une erreur dans un job n'interrompt pas les autres."
    ),
    body(
      "BigFileGate : les fichiers dont la taille est >= large_file_threshold_kb " +
      "(défaut 4 096 Ko) sont soumis à un sémaphore N=1 — un seul gros fichier est " +
      "transféré à la fois sur l'ensemble des jobs. Les petits fichiers sont traités " +
      "en parallèle sans restriction."
    ),
  ];
};
