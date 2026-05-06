"use strict";

const { h1, bullet } = require("../helpers");

module.exports = function section6() {
  return [
    h1("6. Dépannage"),
    bullet(
      "Console non connectée : vérifier qu'EasySave est lancé avec " +
      "remote_console_enabled: true, que le port est identique des deux côtés " +
      "et que le pare-feu autorise le trafic TCP sur ce port."
    ),
    bullet(
      "Job bloqué en Pause : cliquer Play depuis la console déportée, " +
      "ou redémarrer EasySave."
    ),
    bullet(
      "Erreur au démarrage : vérifier .NET 8 (dotnet --version) " +
      "et la validité du JSON dans appsettings.json."
    ),
    bullet(
      "Fichiers de log absents : vérifier les droits d'écriture " +
      "sur le répertoire de données utilisateur (§2)."
    ),
  ];
};
