# Orionum : combat de flotte sur BRUTAL (POC)

Portage du proto de combat de flotte temps réel d'Orionum (TypeScript + three.js) sur [BRUTAL](https://github.com/ahwootech/BRUTAL), le framework C# d'Ahwoo. Pour l'instant, seule la **simulation** est portée (headless, testée) ; le rendu, l'UI et l'audio viendront ensuite.

- Vision du jeu : [docs/CONCEPT.md](docs/CONCEPT.md) et [docs/PROTO-TS-README.md](docs/PROTO-TS-README.md), tels qu'au commit de référence du proto
- Décisions du portage : [docs/DECISIONS.md](docs/DECISIONS.md)
- Architecture et jalons : [docs/PORTAGE.md](docs/PORTAGE.md)

## État

| Jalon | État |
| --- | --- |
| S0 Socle (solution, RNG bit à bit avec le TS) | ✅ |
| S1 Monde et données (stockage, déploiement identique au TS, clone, historique, grille spatiale) | ✅ |
| S2 Vol et ordres (vol newtonien, ordres, commandement de flotte, trajectoires comparées au TS) | ✅ |
| S3 Combat (armes, boucliers, sous-systèmes, bombes, balistique à travers les puits, batailles comparées au TS) | ✅ |
| V0 Visualiseur de débug (OpenGL HDR + bloom, ImGui) | ✅ |
| S4 Drones et hangars (vagues, passes d'attaque, interception, essaims comparés au TS) | ✅ |
| S5 à S7 (Orionum, IA, parité et performance) | à faire |

## Prérequis

- [SDK .NET 10](https://dotnet.microsoft.com/download)
- Windows x64. Les paquets `Ahwoo.Brutal.*` viennent de NuGet.

## Commandes

```sh
dotnet build
dotnet test
dotnet run --project src/OrionumFleet.Headless
dotnet run --project src/OrionumFleet.Viewer -c Release
```

En attendant le runner IA contre IA (S7), le runner headless joue une manœuvre scriptée : la Coalition prend le Mastodonte en tenailles, et la console affiche les positions toutes les 20 s.

### Visualiseur

Le visualiseur fait tourner la sim en direct et la dessine avec le rendu de Brutal Arena : HDR, bloom et lumières ponctuelles. Les formes restent simples : un pavé orienté par coque, une sphère noire cerclée de violet par cœur de puits, des traits lumineux pour les tirs. Ce n'est pas la direction artistique du jeu.

- **Batailles scriptées**, sans IA (S6), hangars pleins :
  - le Raid en engagement général ;
  - le Champ de puits, avec la Coalition en tir courbe sur la Tanière ;
  - les tenailles sur le Mastodonte.
- **Contrôles** :
  - Espace : pause.
  - `.` : un pas de sim.
  - `+` et `-` : vitesse de ×1/8 à ×32.
  - R : relancer.
  - F : suivre la mêlée.
  - F1 : afficher ou masquer le panneau.
  - Clic gauche : tourner.
  - Clic droit : déplacer.
  - Molette : zoomer.
- **Capture sans écran**, utile pour vérifier un rendu : la commande ci-dessous avance la sim à pleine vitesse jusqu'à 90 s, enregistre une image puis quitte.

  ```sh
  dotnet run --project src/OrionumFleet.Viewer -c Release -- --scenario 1 --seed 3 --at 90 --capture shot.png
  ```

## Structure

```text
src/OrionumFleet.Sim/        simulation pure : aucune dépendance au rendu
src/OrionumFleet.Headless/   runner IA contre IA (équivalent de `npm run sim`)
src/OrionumFleet.Viewer/     visualiseur de débug (GLFW, OpenGL, ImGui), rendu repris de Brutal Arena
tests/OrionumFleet.Sim.Tests tests portés du proto TS
docs/                        concept, décisions, plan de portage
```

## Licence

BRUTAL est distribué sous la [BRUTAL License v1.0](https://github.com/ahwootech/BRUTAL/blob/main/LICENSE.txt). Tout produit distribué qui l'utilise doit créditer Ahwoo, afficher le logo BRUTAL avant le jeu et livrer son `NOTICE.txt`.
