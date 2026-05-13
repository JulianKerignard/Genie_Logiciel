"use strict";

const { h1, body, bullet } = require("../helpers");

module.exports = function section4() {
  return [
    h1("4. Console déportée et journaux"),
    body(
      "EasySave.RemoteConsole se connecte à EasySave en TCP (Host + Port). Elle affiche " +
      "en temps réel l'état de tous les jobs et envoie les commandes Pause / Play / Stop. " +
      "Reconnexion automatique (1 s → 2 s → 5 s → 10 s). TLS optionnel via " +
      "remote_console_tls_enabled."
    ),
    body(
      "Journaux : un fichier par jour (yyyy-MM-dd.json ou .xml) ; format paramétrable. " +
      "Le mode log_mode contrôle la destination :"
    ),
    bullet("Local : journaux uniquement sur le poste de l'utilisateur (défaut)."),
    bullet("Centralized : envoi HTTP au service Docker LogCentralizer ; journal unique pour toute la flotte (différentiation par MachineName + UserName)."),
    bullet("Both : écriture locale ET envoi au centralisateur."),
  ];
};
