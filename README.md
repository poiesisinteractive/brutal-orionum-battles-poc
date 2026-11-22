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
| Grande bataille : 1 200 coques et 8 000 drones à 9 ms par pas (grilles spatiales, JobScheduler, rendu instancié) | ✅ |
| Bataille à l'échelle du système de Wolf 359 : jusqu'à 2 000 coques par camp, croisière, astres, icônes tactiques, fiche de vaisseau | ✅ |
| Bataille des capitaux : vaisseaux de 150 m à 3 km, batteries de tourelles, canon axial, lances, dégâts localisés, escadrilles, modèles procéduraux en deux styles, tableau de bord | ✅ |
| S5 Orionum | reporté |
| S6 et S7 (IA, analyse, runner, parité globale) | à faire |

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
  - les tenailles sur le Mastodonte ;
  - la **bataille des capitaux** dans le système de Wolf 359 (par défaut) : la flotte type de chaque camp (un amiral de 3 km, quatre cuirassés de 1,5 km, six croiseurs, deux porteurs, douze frégates) multipliée de ×1 à ×8 (curseur, ou `--fleet 2`). Les flottes partent de deux points tirés de la graine, traversent le système en croisière (3 km/s), puis se rangent en lignes de bataille à 20-25 km. Bordées de tourelles, lances, canon axial, salves de missiles, escadrilles de chasseurs ; chaque tourelle se détruit. La dernière flotte debout l'emporte.
  - Les capitaux sont des modèles procéduraux : Coalition effilée, blanche et bleue ; Clans massifs, rouillés et orange. Le panneau « Capitaux » les liste avec leur coque, leurs boucliers et leurs tourelles (un clic sélectionne, un second suit).
- **Contrôles** :
  - Espace : pause.
  - `.` : un pas de sim.
  - `+` et `-` : vitesse de ×1/8 à ×64 (la vitesse effective s'affiche quand la machine ne suit plus).
  - R : relancer.
  - Clic sur un vaisseau (modèle ou icône) : sa fiche (coque, 6 faces, sous-systèmes, armes, ordre, groupe, bilan), ses lignes de ciblage, sa portée, ses drones et son escadre.
  - Double-clic ou F : zoomer sur le vaisseau et le suivre. Sans sélection, F suit la mêlée.
  - Échap : lâcher la sélection.
  - Origine : cadrer toute la bataille.
  - G : icônes tactiques (de loin, chaque vaisseau devient une icône de classe ; plus loin encore, une escadre devient un marqueur avec son effectif).
  - F1 : afficher ou masquer le panneau.
  - Glisser : tourner. Clic droit : déplacer. Molette : zoomer (de 30 m à 30 000 km).
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
