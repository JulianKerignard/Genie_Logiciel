"use strict";

const { h1, bullet } = require("../helpers");

module.exports = function section6() {
  return [
    h1("6. Dépannage"),
    bullet("Console déportée injoignable : vérifier remote_console_enabled, le port identique des deux côtés et l'autorisation TCP du pare-feu."),
    bullet("Job bloqué en Pause : cliquer Play depuis la card ou la console déportée ; vérifier qu'aucun logiciel métier n'est lancé."),
    bullet("Journaux centralisés vides : vérifier log_centralizer_url et que le conteneur Docker LogCentralizer est démarré."),
    bullet("Erreur au démarrage : vérifier .NET 8 (dotnet --version) et la validité du JSON dans appsettings.json."),
  ];
};
