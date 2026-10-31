# Décisions du portage sur BRUTAL (grill du 2026-10-31)

Ce dépôt porte le proto de combat de flotte d'Orionum (TypeScript + three.js, `orionum-battles/fleet-proto`) sur [BRUTAL](https://github.com/ahwootech/BRUTAL), le framework C# d'Ahwoo. La vision du jeu reste celle du proto : voir [CONCEPT.md](CONCEPT.md) et le [README du proto](PROTO-TS-README.md), copiés tels qu'au commit de référence.

| Sujet | Décision |
| --- | --- |
| Cible | Le proto de combat de flotte (`fleet-proto`). Le TCG Unity *Orionum Battles* n'est pas concerné. |
| Référence | Proto TS **gelé au commit `3eae78c`** (lot 3, 2026-10-16). Les lots TS suivants seront reportés plus tard, en diff. |
| Premier jalon | **La sim seule** : sim, IA, analyse tactique, tests et runner headless en C#. On redécidera ensuite du rendu, de l'UI et de l'audio. |
| Périmètre de jeu | **v0 + Horloge + Terrain** : 4 classes par camp et la Tanière, Raid sur Wolf 359, boucliers 6 faces, sous-systèmes, drones, ordres, commandement de flotte, IA. Les lots 1 à 3 (nouvelles classes, objectifs, écran à crédits, renforts) viendront plus tard. |
| Fidélité | **Parité statistique** avec le TS sur des centaines de batailles IA contre IA, plus le portage des tests du périmètre. Pas d'égalité bit à bit, sauf pour le RNG. |
| Numérique | **`float`** (types `float3` / `floatQuat` de `Brutal.Numerics`). |
| Architecture | **Performance d'abord** : stockage data-oriented et phases parallèles (voir [PORTAGE.md](PORTAGE.md)). |
| Échelle visée | **1 000+ coques et 10 000+ drones** à terme. |
| Réseau | **Aucun.** Le déterminisme n'est exigé que sur une même machine (tests, replays). |
| Analyse tactique | **Incluse** dans le jalon sim : projection à +60 s, officier tactique, IA difficile. |
| Rendu (plus tard) | **OpenGL 4.1**, en repartant du renderer HDR et bloom du POC *Brutal Arena*. |
| UI (plus tard) | **ImGui partout** : HUD, panneaux, menus, symboles 3D par la draw list. |
| Audio (plus tard) | **SDL_mixer**. |
| Plateforme | **Windows.** |
| Dépôt | Public : `poiesisinteractive/brutal-orionum-battles-poc`, document de concept compris. |
