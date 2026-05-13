"use strict";

const { h1, body, bullet } = require("../helpers");

module.exports = function section3() {
  return [
    h1("3. Sauvegardes"),
    body(
      "Onglet Jobs : créer / éditer / supprimer un job (nom unique, source, destination, " +
      "type Complète ou Différentielle). Boutons Run par job ou Run All en masse. Chaque " +
      "card affiche un badge d'état (Idle / Running / Paused / Done), une barre de " +
      "progression et le fichier en cours."
    ),
    bullet("Parallélisme : max_parallel_jobs (défaut 4) ; une erreur n'interrompt pas les autres."),
    bullet("Prioritaires : tant qu'une extension de priority_extensions reste à copier sur un job, les fichiers non prioritaires des autres jobs attendent."),
    bullet("Gros fichiers : les fichiers >= large_file_threshold_kb traversent un sémaphore N=1 commun à tous les jobs ; les petits fichiers passent en parallèle sans restriction."),
    bullet("Pause (au prochain fichier) · Play (reprise depuis l'offset courant) · Stop (annulation immédiate) sur chaque card et globalement."),
    bullet("Logiciel métier : si business_software est détecté, tous les jobs en cours passent automatiquement en pause ; reprise auto dès qu'il est fermé. L'événement est consigné dans le journal."),
  ];
};
