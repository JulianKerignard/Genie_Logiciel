"use strict";

const { h1, body, bullet, code } = require("../helpers");

module.exports = function section2() {
  return [
    h1("2. Installation et lancement"),
    body("Interface graphique (mode nominal) :"),
    code("cd src/EasySave.UI && dotnet run"),
    body("Console déportée (poste opérateur distant) :"),
    code("cd EasySave.RemoteConsole && dotnet run"),
    body("Données utilisateur (jobs.json · state.json · Logs/) :"),
    bullet("Windows : %AppData%\\ProSoft\\EasySave\\"),
    bullet("Linux / macOS : ~/.config/ProSoft/EasySave/"),
  ];
};
