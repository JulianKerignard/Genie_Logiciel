"use strict";

const { h1, body, bullet, code } = require("../helpers");

module.exports = function section2() {
  return [
    h1("2. Installation et lancement"),
    body("Application principale — lancer depuis la racine du projet :"),
    code("cd src/EasySave && dotnet run"),
    body(
      "Sans argument : menu interactif. " +
      "Avec argument : exécution directe des jobs sélectionnés (indices base 1)."
    ),
    code("dotnet run -- 1        # job 1"),
    code("dotnet run -- 1-3      # jobs 1, 2, 3"),
    code("dotnet run -- 1;3;5    # jobs 1, 3, 5"),
    body("Fichiers de données (jobs.json · state.json · Logs/) :"),
    bullet("Windows  : %AppData%\\ProSoft\\EasySave\\"),
    bullet("Linux/macOS : ~/.config/ProSoft/EasySave/"),
  ];
};
