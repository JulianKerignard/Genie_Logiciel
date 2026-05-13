"use strict";

const { h1, body, bullet } = require("../helpers");

module.exports = function section1() {
  return [
    h1("1. Présentation"),
    body(
      "EasySave V3.0 est l'outil de sauvegarde ProSoft (.NET 8). Interface graphique " +
      "Avalonia (FR / EN), nombre de jobs illimité, types Complète ou Différentielle, " +
      "sources et cibles locales / externes / réseau."
    ),
    body("Nouveautés V3 par rapport à la V2 :"),
    bullet("Sauvegardes parallèles (le mode séquentiel est abandonné)."),
    bullet("Fichiers prioritaires : aucun fichier non prioritaire ne démarre tant qu'une extension prioritaire est en attente sur un job."),
    bullet("Gros fichiers : un seul transfert > seuil paramétrable à la fois (anti-saturation bande passante)."),
    bullet("Pause / Play / Stop par job ou en masse, en local ou depuis la console déportée."),
    bullet("Pause auto si logiciel métier détecté ; reprise auto à sa fermeture."),
    bullet("CryptoSoft mono-instance (une seule exécution simultanée sur la machine)."),
    bullet("Centralisation des journaux via service Docker (mode Local, Centralisé ou Both)."),
  ];
};
