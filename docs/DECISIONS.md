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

## Révision du 2026-11-21 : la bataille des capitaux

La bataille du système est un fouillis : trop de petits vaisseaux, des essaims de drones, aucune hiérarchie visuelle, aucune lecture de qui tire sur qui. On la recentre sur des vaisseaux capitaux gigantesques.

| Sujet | Décision |
| --- | --- |
| Classes | **Cinq classes par camp, de quatre tailles nettes** : frégate (~150 m), croiseur (~600 m), cuirassé (~1,5 km), vaisseau amiral (~3 km), et un porteur (~1 km) avec ses escadrilles. Chaque taille a un rôle et une silhouette. |
| Effectifs | **Peu mais énormes** : de 10 à 30 capitaux par camp, escortés de quelques dizaines de vaisseaux plus petits. |
| Armement des capitaux | **Bordées de tourelles** (des dizaines, qui pivotent et tirent de flanc), **canon axial géant** (salve dévastatrice, longue recharge, il faut pointer tout le vaisseau), **lances à rayon** (rayons continus de plusieurs secondes), **missiles en salves** (interceptables par la défense ponctuelle). |
| Portée | **10 à 30 km** entre capitaux : on voit les deux lignes et les tirs qui traversent l'espace entre elles. Les petits vaisseaux vont au contact. |
| Dégâts | **Localisés** : tourelles, moteurs et générateurs de bouclier détruits un par un, sections en feu, coque qui se fissure. Un capital se dégrade sous nos yeux avant de mourir. |
| Drones | **Escadrilles lisibles** : quelques escadrilles de 6 à 12 appareils par porteur, qui volent et frappent en groupe. |
| Apparence | **Modèles procéduraux de grande qualité**, générés en code par classe : proues, superstructures, tourelles visibles qui pivotent, tuyères, feux de position. |
| Esthétique | **Deux styles** : Coalition aux coques effilées, claires, aux lignes nettes et aux feux bleus ; Clans aux masses brutes, asymétriques, blindées, aux feux orange. Reconnaissables même de loin. |
| Lecture | **Lignes de bataille** (les capitaux face à face, les petits sur les flancs), **moments forts** (tir axial, destruction d'un capital, bouclier qui tombe : effets marqués, ralenti optionnel, journal), **tableau de bord des capitaux** (une colonne par camp, coque, boucliers, tourelles restantes, cliquable). |
| Base | **Une nouvelle couche capitale sur le socle** : vol newtonien, boucliers par face, croisière, astres et performance sont gardés. La parité avec le TS reste valable pour l'Escarmouche. |
