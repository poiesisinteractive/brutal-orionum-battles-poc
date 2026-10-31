# Orionum : combat de flotte sur BRUTAL (POC)

Portage du proto de combat de flotte temps réel d'Orionum (TypeScript + three.js) sur [BRUTAL](https://github.com/ahwootech/BRUTAL), le framework C# d'Ahwoo. Pour l'instant, seule la **simulation** est portée (headless, testée) ; le rendu, l'UI et l'audio viendront ensuite.

- Vision du jeu : [docs/CONCEPT.md](docs/CONCEPT.md) et [docs/PROTO-TS-README.md](docs/PROTO-TS-README.md), tels qu'au commit de référence du proto
- Décisions du portage : [docs/DECISIONS.md](docs/DECISIONS.md)
- Architecture et jalons : [docs/PORTAGE.md](docs/PORTAGE.md)

## État

| Jalon | État |
| --- | --- |
| S0 Socle (solution, RNG bit à bit avec le TS) | ✅ |
| S1 à S7 (monde, vol, combat, drones, Orionum, IA, parité) | à faire |

## Prérequis

- [SDK .NET 10](https://dotnet.microsoft.com/download)
- Windows x64. Les paquets `Ahwoo.Brutal.*` viennent de NuGet.

## Commandes

```sh
dotnet build
dotnet test
dotnet run --project src/OrionumFleet.Headless
```

## Structure

```text
src/OrionumFleet.Sim/        simulation pure : aucune dépendance au rendu
src/OrionumFleet.Headless/   runner IA contre IA (équivalent de `npm run sim`)
tests/OrionumFleet.Sim.Tests tests portés du proto TS
docs/                        concept, décisions, plan de portage
```

## Licence

BRUTAL est distribué sous la [BRUTAL License v1.0](https://github.com/ahwootech/BRUTAL/blob/main/LICENSE.txt). Tout produit distribué qui l'utilise doit créditer Ahwoo, afficher le logo BRUTAL avant le jeu et livrer son `NOTICE.txt`.
