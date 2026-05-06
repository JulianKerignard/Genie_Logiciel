"use strict";

const { h1, body, bullet, code } = require("../helpers");

module.exports = function section4() {
  return [
    h1("4. Console déportée (EasySave.RemoteConsole)"),
    body(
      "Application Avalonia autonome. Se connecte à EasySave via TCP pour afficher " +
      "en temps réel la progression de chaque job et envoyer des commandes à distance."
    ),
    body("Activation côté serveur : définir remote_console_enabled: true dans appsettings.json."),
    body("Lancement :"),
    code("cd EasySave.RemoteConsole && dotnet run"),
    body("Interface :"),
    bullet("Barre de connexion : champs Host (défaut 127.0.0.1) et Port (défaut 9000), boutons Connexion / Déconnexion, indicateur d'état."),
    bullet("Liste des jobs : nom · barre de progression · statut (Running / Paused / Done) · boutons Pause, Play, Stop."),
    body("Connexion : saisir l'IP de la machine hébergeant EasySave, vérifier le port, cliquer Connexion."),
    body("Commandes : Pause (suspend au prochain fichier) · Play (reprend ou démarre) · Stop (annule)."),
    body("Reconnexion automatique en cas de coupure : 1 s → 2 s → 5 s → 10 s (boucle)."),
  ];
};
