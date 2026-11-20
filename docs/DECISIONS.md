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

## Révision du 2026-11-08 : la grande bataille

Après S4, le portage change de cap pour profiter de BRUTAL.

| Sujet | Décision |
| --- | --- |
| Orionum (S5) | **Reporté.** Les capacités Orionum (Horloge, fronde, élan) et leur IA attendront. |
| Priorité | La piste **« Grande bataille »** : une bataille géante, d'abord pour la regarder. On commence par l'échelle (flottes de N coques en formations), puis le rendu de masse (instancié) et la sim de masse (grille spatiale, JobScheduler). L'IA (S6) vient ensuite. |
| Fidélité | **Libre** dès qu'il s'agit de la masse. On peut simplifier les règles du proto en O(n²) tant que la bataille reste crédible. Les tests de parité restent un garde-fou à l'échelle de l'Escarmouche : un écart doit être voulu et documenté. |

## Révision du 2026-11-20 : la bataille à l'échelle d'un système

| Sujet | Décision |
| --- | --- |
| Effectifs | **Réglables jusqu'à 2 000 coques par camp**, avec la composition de l'Escarmouche. Il faut rester fluide à 500 contre 500. |
| Champ de bataille | **À l'échelle d'un système (1 000 km et plus).** |
| Système | **Wolf 359 stylisé** : une naine rouge en fond, 2 ou 3 planètes géantes de milliers de km de rayon et quelques lunes, placées à la main. |
| Planètes | **Décor et obstacle** : on ne les traverse pas, les coques les contournent et elles bloquent les tirs. Pas de gravité. |
| Puits de gravité | **Retirés** de cette bataille pour le moment. |
| Objectif | **Annihilation** : la dernière flotte debout l'emporte. Pas de Tanière. |
| Départ | **Tiré de la graine** : positions de départ des flottes, à des centaines de km l'une de l'autre, près des planètes. |
| Transit | **Croisière** : hors combat, les coques filent à 50 à 100 fois leur vitesse. La croisière se coupe dès qu'un ennemi est à quelques km. |
| Temps | **Accélération de ×1 à ×64**, avec la vitesse effective affichée quand la machine ne suit plus. |
| Sélection | **Un clic sur un vaisseau** ouvre sa fiche complète (coque, 6 faces, sous-systèmes, armes, ordre, cible, bilan). On peut aussi suivre le vaisseau à la caméra, voir ses lignes de ciblage, ses portées et ses drones, et mettre en surbrillance son groupe de flotte avec l'état du groupe. |
| Vue lointaine | **Icônes tactiques** : en dézoomant, chaque vaisseau devient une icône de classe colorée par camp. |
| IA | **Pas encore.** En attendant S6, chaque groupe fait route en croisière vers le groupe ennemi le plus proche, puis engage en ciblage automatique. |
