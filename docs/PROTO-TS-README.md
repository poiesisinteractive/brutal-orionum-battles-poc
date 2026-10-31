# Orionum — Proto de combat de flotte (temps réel)

Proto **exploratoire** d'un *autre produit* qu'Orionum Battles : combat de flottes temps réel en 3D libre, ADN *Nexus: The Jupiter Incident*, univers Orionum. Isolé dans ce dossier, il migrera vers son propre repo.

→ Vision, décisions, phasage : **[CONCEPT.md](CONCEPT.md)**

## Lancer

```bash
cd fleet-proto
npm install
npm run dev        # http://localhost:5173
npm test           # déterminisme, boucliers, ordres, historique
npm run sim -- --n 100 --coalition equilibre --clans cassecou   # IA vs IA headless
npm run sim -- --n 10 --verbose
npm run sim -- --n 80 --scenario well-field                   # carte d'essai du Terrain
npm run sim -- --n 80 --no-terrain --no-horloge               # piliers coupés
npm run sim -- --n 80 --no-orionum-clans                      # profondeur : une IA ignore l'Orionum
npm run sim -- --n 80 --no-terrain-ai-coalition               # profondeur : une IA aveugle au terrain
npm run sim -- --n 40 --scenario convoy                       # objectifs : wolf359-raid, annihilation, convoy, hold, well-field
npm run sim -- --n 40 --fleet-size grand                      # tailles : skirmish (~6), battle (~16, défaut), grand (~30)
npm run sim -- --n 32 --doctrine-coalition artillery --doctrine-clans swarm   # flottes achetées par doctrine
npm run sim -- --n 32 --random-coalition --random-clans --asym 0.2           # aléatoire équilibré, budget asymétrique
npm run sim -- --n 32 --add-coalition trebuchet:rapid+sensors,rempart~       # coques ajoutées (classe[:variante][+option][@niveau|~])
```

## État : v1 « Horloge + Terrain » + lots 1 à 3 (grandes flottes, écran à crédits, renforts)

- **Renforts par saut gravitationnel** (lot 3). Le canon est gardé (pas de FTL) : c'est un pli court ouvert par l'Orionum.
  - **Payés en DT** : la réserve de crédits gardée à l'écran de paramétrage, 45 % de la valeur de chaque coque perdue (rattrapage), et ce que rapporte l'objectif (Tanière entamée ou tenue, cargo passé ou détruit, points de secteur).
  - **Appel** : panneau « Renforts » en bas à droite. On choisit une coque (toute classe, équipement de série), puis la sortie sur la carte : dans sa moitié, à plus de 600 u de l'ennemi, loin des cœurs et du secteur disputé. Un puits proche dévie la sortie.
  - **Lentille** : violette, visible des deux camps, avec un compte à rebours de 8 s et l'alerte de l'officier (« Lentille ennemie — secteur — 8 s »). Une stase posée dessus retient l'arrivée, une bulle la ralentit. Une seule lentille à la fois par camp, puis 20 s de recharge.
  - **Arrivée** : jaillissement, onde de choc qui repousse et sonne ce qui est proche (alliés compris), traînée violette. La coque sort boucliers à zéro et sonnée 5 s (demi-vitesse), puis rejoint le groupe de son rôle.
  - **Saut d'urgence** (U, ou bouton du panneau) : 8 s de charge sur place, immobile, interrompue si les moteurs tombent. La coque sauvée, comme une coque rentrée par la retraite, rend la moitié de sa valeur en DT. Elle rejoint ensuite la liste « coques sauvées » : rappelable pour la moitié de son prix, réparée à moitié.
  - **IA** :
    - elle renforce la classe qui lui manque, sur le flanc où elle plie, sous la couverture de ses gros canons ;
    - elle tend des embuscades aux sorties adverses (frégates et bulle) ;
    - elle fait sauter les coques condamnées.
  - **Officier** : il propose renforts et sauts d'urgence, testés en projection.
  - **Son et caméra** : sons dédiés (montée de la lentille, sortie, charge d'urgence) ; la caméra cinématique coupe sur les arrivées.

- **Écran de paramétrage à crédits** (menu → « Paramétrer… ») : deux colonnes face à face, Coalition et Clans.
  - **Règles et carte, gratuites et communes** : objectif, carte (puits de l'objectif ou espace dégagé), taille, durée, piliers, profil et difficulté de l'IA, graine.
  - **Budget** : fixé par la taille et ajusté par objectif (la valeur des flottes mesurées justes au lot 1 : au Raid, les Clans ont la Tanière et moins de crédits). Un curseur donne jusqu'à 50 % d'avantage à un camp.
  - **Achats** :
    - coques ;
    - **variantes**, 2 ou 3 par classe (armement, défense, propulsion) ;
    - **options** à cocher : capteurs, DCA+, blindage, soute à mines pour les Clans ;
    - **Orionum** : niveau du module (Coalition : stable ou maîtrisé ; Clans : brut seulement), capacités à la carte, module réduit (bulle et stase) sur un croiseur de ligne ou un porteur, instabilité de départ qui rend des crédits ;
    - **avantages** : déploiement avancé, puits supplémentaire, tourelles de la Tanière.
  - **Préréglages** :
    - doctrines (Équilibrée, Artillerie, Essaim, Blindage, Orionum lourd) ;
    - aléatoire équilibré ;
    - rejouer la dernière (même graine en option) ;
    - mes flottes sauvées, dans le navigateur.
  - **Réserve pour renforts** : ce sont vos DT au début de la bataille.
- Le menu garde la **bataille rapide** (flottes préréglées), et l'IA adverse achète selon sa doctrine sans voir votre flotte.

- **Bataille à ~16 coques par camp** par défaut (préréglages Escarmouche ~6, Bataille ~16, Grande ~30, au menu), 2 capitaux par camp, chacun avec son module Orionum.
- **Quatre objectifs** (menu) :
  - **Raid sur Wolf 359** : détruire la Tanière, le dépôt des Clans, avant 12 min ;
  - **Annihilation** : détruire la flotte adverse (aux points au chrono) ;
  - **Escorte de convoi** : 2 cargos sur 4 doivent traverser la carte en 14 min ;
  - **Tenir le secteur** : marquer 300 points dans la zone centrale, où compte le poids de flotte présent.
- Le **Champ de puits** reste la carte d'essai du Terrain.
- **Six nouvelles classes asymétriques** (lot 1) :

  | Coalition (ordre) | Clans (chaos) |
  |---|---|
  | **Rempart**, croiseur de ligne : 1 750 de coque, tient la ligne à 450 u | **Éventreur**, destroyer d'assaut rapide : deux tubes lance-torpilles et un laser |
  | **Trébuchet**, artillerie : portée 2 500, obus lents et lourds, tire seulement sur une cible qu'un allié voit (repérage à 1 100 u), excellent en tir courbe | **Semeur**, poseur de mines : sème des champs qui dérivent, camouflés (la DCA ne les voit qu'à 130 u), qui explosent au contact de n'importe quelle coque, alliés compris, et que les puits attirent |
  | **Hospitalier**, remorqueur : répare coque et sous-systèmes autour de lui (deux fois plus vite à l'arrêt), remorque un allié aux moteurs coupés jusqu'au saut de retraite | **Murmure**, brouilleur : dans sa bulle, les munitions guidées et les drones ennemis perdent en précision, et les coques ennemies voient et tirent moins loin |
- Jouable des deux côtés, ou en spectateur IA contre IA (caméra cinématique).
- Boucliers directionnels sur 6 faces, sous-systèmes (moteurs, armes, boucliers) ciblables, postures et triangle d'énergie.
- Ordres relatifs et positionnels, file d'ordres (Maj), déclencheurs conditionnels, pause tactique.
- Drones de porteurs : bombardiers de sous-systèmes qui passent sous les boucliers. Ce sont des cores IA bridés, qui s'éteignent si leur porteur meurt.
- DCA, missiles, cinétique balistique avec **suivi des tourelles** : un brawler collé à un capital est dur à toucher, à distance c'est une proie.
- **Lasers** (traits de blaster et turbolasers : forts sur bouclier, faibles sur coque) et **bombes** lentes à souffle de zone (alliés compris), abattables par la DCA et les drones.
- **Destruction en plusieurs temps** : agonie (vrille, feux, explosions secondaires, pièces arrachées), puis rupture du réacteur qui blesse tout ce qui est proche ; les pièces restent en champ d'épaves.
- **Essaims** : porte-drones doublés, intercepteurs (anti-drones, anti-missiles, anti-bombes), escadrilles sur les capitaux, lancements en vagues.
- **Bombe à énergie** (une par capital) : boucliers à zéro dans un grand rayon, alliés compris, puis redémarrage rapide.
- **Les gros tiennent la mêlée à distance** : bulle de sécurité (capitaux 900 u, porteurs 1 100 u), zone de mort que les petits évitent, portées des gros ×1,5 ; seul l'ordre « Au contact » les envoie dans la mêlée.
- **Vol newtonien assisté** : glisse sans frottement, moteur principal dans l'axe du nez, RCS faibles, inertie angulaire (les capitaux mettent environ 16 s à se retourner), flip-and-burn automatique, pointage par vaisseau indépendant du mouvement.
- **Commandement de flotte** : 3 groupes tactiques (formations, doctrines, manœuvres coordonnées), utilisés aussi par l'IA ennemie.
- **Analyse en temps réel** : projection simulée à +60 s, officier tactique qui teste ses conseils en projection, matrice d'engagement, carte des menaces.
- Retraite par saut gravitationnel (flash violet) : la coque est sauvée mais quitte la bataille.
- Rendu : ciel peint, Wolf 359, astéroïdes, coques en ligne claire (aplats toon + traits encrés), épaves persistantes.
- Caméras : tactique, embarquée (V), cinématique auto (C, ou après 30 s d'inactivité), mode photo (P).
- **Lisibilité à grande échelle** : au dézoom, symboles stratégiques par rôle (◆ capital, ▮ ligne, ⬢ porteur, ✛ artillerie, ✚ soutien, ◇ escorte, ▲ brawler, ■ cargo), petits détails (tourelles, feux) coupés au loin ; anneaux verts de réparation et violets de brouillage en vue tactique.
- **Groupes automatiques par rôle** : 1 Ligne, 2 Lanciers, 3 Hangar, 4 Artillerie, 5 Soutien, 6 Mines (les groupes 4 à 6 n'existent que s'ils ont des membres). L'IA ennemie les commande aussi.

### Orionum v1 : instabilité + Horloge

- Un module par capital. **Ordre contre chaos** : le Bastion (Coalition) a un module **stable** (coûts ×0,6, risque ×1) ; le Mastodonte (Clans) un module **brut** (coûts ×1,6, puissance ×1,15, risque ×2,5).
- **Bulle de ralenti** : zone à demi-vitesse (vaisseaux, tirs, drones) · **Surcadençage** : le capital à ×1,75 pendant 8 s · **Fenêtre rétroactive** : retour à son état d'il y a 5 s · **Stase** : zone figée, rien n'entre, les tirs restent suspendus · **Élan** (module stable seulement) : zone où le temps file à ×1,25 pour tous, ennemis compris.
- **Instabilité** de 0 à 100 : au-delà de 70, risque de **résonance** (certaine à 100) : micro-stase, puits ou désynchronisation au hasard autour du capital, alliés compris ; le capital perd 30 % de coque et son module est coupé 30 s.
- L'IA s'en sert (rembobine si elle encaisse, fige les bombes, ralentit les grappes ennemies, surcadence au contact) et l'officier tactique peut la proposer.
- **Playtest** : un journal de partie (usages Orionum, résonances, pertes, bombes à énergie) et un questionnaire de fin de partie, enregistrés sur la page de playtest publiée (ou téléchargés en JSON).

### Orionum v1 : Terrain (gravité)

- **Puits fixes** posés par la carte (vestiges effondrés) : un cœur noir qui avale tout ce qui l'atteint (tirs, bombes, drones ; les coques y sont broyées) et un champ qui attire tout, **lasers compris**. Les tirs lents et les bombes se courbent beaucoup, les obus rapides peu ; les petites coques sont déportées, les gros résistent.
- **Visée automatique** : la conduite de tir compense la courbure. Une cible **cachée derrière un cœur** ne se tire plus en ligne droite (rayons arrêtés net).
- **Tir courbe** (J) : obus et bolts lancés en cloche, à charge réduite (27 à 45 % de la vitesse), hors de l'axe, pour que le puits les ramène sur la cible cachée ; trajectoire tracée en vue tactique. Coûte 15 % de cadence tant qu'il est actif.
- **Fronde** (Orionum, Maj+5) : ancre un puits tournant **sans cœur** (14 s). Il attire faiblement, et tout ce qui frôle son œil repart ×1,2 plus vite (tirs et bombes au-delà de leur vitesse, drones et coques au-delà de leur croisière pendant 3 s), ennemis compris. Coûte 40 d'instabilité de base. C'est un outil de manœuvre et d'attaque, plus un bouclier.
- Deux cartes : le **Raid sur Wolf 359** (3 puits qui découpent l'approche) et le **Champ de puits**, carte d'essai où les puits dominent (l'un masque la Tanière depuis le sud).
- Les piliers se cochent dans le menu (Horloge, Terrain) : on les mesure seuls et ensemble.

### Fondations pour la suite de v1

- `timeScale` par entité, piloté par les zones Orionum (`w.zones`) ; champ de gravité par entité (`w.wells`, `src/sim/terrain.ts`).
- Historique d'états par vaisseau sur 10 s (fenêtre rétroactive).
- Piliers et cartes en options de `World` (`new World(seed, { scenario, pillars })`).

## Commandes

| Action | Contrôle |
|---|---|
| Sélectionner | Clic gauche (Maj : ajouter), glisser : cadre · double-clic : toutes les coques actives de la même classe |
| Ordre contextuel | Clic droit : ennemi = attaquer, allié = escorter, vide = déplacer · un Hospitalier sur un allié aux moteurs coupés : le remorquer |
| File d'ordres | Maj + clic droit |
| Ordres | A attaquer · O orbiter · E escorter · D garder la distance · B passe balistique · K repli en tirant · M déplacer · H tenir · R retraite |
| Caméra | Glisser droit : orbite · glisser milieu : panoramique · molette : zoom · F : centrer |
| Modes d'interface | Tab : Commandement → Gestion vaisseau → Ciblage précis |
| Temps | Espace : pause tactique · +/− : vitesse |
| Renforts | Panneau « Renforts » : clic sur une coque (ou une coque sauvée), puis sur la sortie. Clic droit : annuler |
| Saut d'urgence | U (ou bouton du panneau) sur la sélection |
| Bombe à énergie | N (ou bouton de la fiche du capital) puis clic sur le groupe ennemi : une par capital et par bataille |
| Orionum | Barre violette en bas (capital sélectionné) : jauge d'instabilité (repère à 70) et ses boutons, ou Maj+1 bulle · Maj+2 surcadençage · Maj+3 rembobiner · Maj+4 stase · Maj+5 fronde · Maj+6 élan (Coalition). Bulle, stase, fronde et élan : puis clic sur le point visé (portée 1 600 u). Chaque bouton a son info-bulle d'une phrase. Utilisable en pause tactique : s'exécute à la reprise. |
| Tir courbe | J (ou bouton de la fiche) : la sélection lance ses obus autour des cœurs ; trajectoires en pointillés ambrés en vue tactique |
| Carte et piliers | Menu principal : objectif (Raid, Annihilation, Convoi, Secteur, Champ de puits), taille de flotte, cases Horloge et Terrain |
| Au contact | Bouton rouge de la fiche d'un gros vaisseau (deux clics pour confirmer) : seul ordre qui l'emmène dans la mêlée |
| Menu pause | Échap (si aucun ordre en cours) : reprendre, son, crédits, quitter vers le menu. Le menu principal propose aussi « Reprendre la partie ». Le bouton 🔊 de la barre ouvre les curseurs de son sans interrompre la bataille. |
| Caméras | V embarquée · C cinématique · P photo · L surcouche tactique |
| Groupes | Ctrl+1–9 assigner · 1–9 rappeler |
| Commandement de flotte | Mode Commandement (Tab) : groupes, formations, doctrines, manœuvres · Alt+1 : appliquer le conseil de l'officier · G : replier le panneau · X : matrice d'engagement · T : carte des menaces |
| Pointage manuel | Pointer vers…, Exposer [face], Verrouiller · flèches : pivoter |

## Modèles 3D

Les coques sont procédurales (`src/render/designs.ts`, boîte à outils `src/render/shipkit.ts`) : coques en pièces (qui se détachent à la destruction), tourelles qui visent, hublots, feux clignotants, baies de hangar alignées sur les points de lancement des drones (`bays` dans `data.ts`), greebles. Échelle : capitaux ×3 (Bastion 135, Mastodonte 150), porte-drones ×2, dépôt 200 ; frégates inchangées.

Pour remplacer une coque par un modèle d'artiste :

1. Déposer `public/models/<classId>.glb`. Les ids sont `bastion`, `egide`, `hoplite`, `vigile`, `rempart`, `trebuchet`, `hospitalier`, `mastodonte`, `ruche`, `ecorcheur`, `grele`, `eventreur`, `semeur`, `murmure`, `cargo` et `depot`. Le modèle doit avoir le nez vers +Z ; il est mis à l'échelle automatiquement.
2. Ajouter l'id dans `public/models/manifest.json`, par exemple `["bastion"]`.

Un modèle d'artiste remplace l'image mais pas encore les tourelles animées ni les feux (à brancher par noms de nœuds quand les modèles arriveront).

**Icônes de statut** au-dessus des barres de vie (vue tactique) : rouge = danger (bombe en approche avec délai, coque critique, réacteur instable avec compte à rebours, cible de mon tir concentré), ambre = dégâts (moteurs, armes ou boucliers hors service, face de bouclier tombée, stock de drones bas), cyan = ordres et état (retraite, posture assaut/défense/fuite, drones en attaque ou en écran), violet = capacités (bombe à énergie prête, boucliers bloqués par une bombe à énergie avec compte à rebours).

## Son

- 37 cues (71 variantes) dans `public/audio/`, générées par `python3 tools/audio/build_audio.py` à partir de `tools/audio/sources.json` : enregistrements Freesound et synthèse procédurale (`tools/audio/synth.py`). Il faut `ffmpeg`, `numpy` et `FREESOUND_API_KEY` dans l'environnement.
- Pour passer en qualité originale, voir la liste de téléchargement dans `audio-src/originals/README.md`.
- Musique : thème de Thomas Lheureux (sous licence). Autres pistes : les déposer dans `public/audio/music/` et les lister dans `playlist.json` (voir le README du dossier).
- Volumes (général, effets, ambiance, musique) réglables en jeu sans relancer : bouton 🔊 de la barre du haut, ou menu pause (Échap).
- Mixage : sons légers (tirs, impacts) qui s'éteignent vite avec la distance ; tirs lourds et explosions limités en nombre simultané (budget : 2 tirs lourds, 1 grosse explosion, le plus fort gagne) ; une grosse explosion proche baisse un instant les tirs et les alertes ; en vue tactique, on écoute depuis le point visé et le dézoom éloigne tout, au lieu de rendre toute la carte audible.

## Architecture

```text
src/sim/      simulation pure TS, déterministe (RNG seedé, pas fixe 20 Hz), aucune dépendance au rendu
src/ai/       IA utility à 2 étages (commandant + capitaines), passe par la même API que le joueur
src/render/   three.js : scène, coques, fond, effets, caméra / réalisateur
src/ui/       HUD HTML, entrées, i18n FR/EN
src/audio/    moteur Web Audio : bus, son coque/cinéma selon la caméra, spatialisation, musique
src/headless.ts  runner IA vs IA
```

## Équilibrage (IA équilibrée vs IA équilibrée, Horloge et Terrain actifs)

**Lot 3, renforts** (Bataille, 40 batailles par case, bruit ±8 points ; victoires Coalition / Clans) :

| Objectif | Avec renforts | Dotation de DT au départ |
|---|---|---|
| Raid sur Wolf 359 | 43 / 58, 45 / 55, 45 / 55 (3 graines) | Clans 75 |
| Annihilation | 50 / 50 (toutes au chrono) | Clans 100 |
| Escorte de convoi | 53 / 48 | Coalition 500 |
| Tenir le secteur | 60 / 40 | Coalition 450 |
| Escarmouche (Raid) | 45 / 55 | — |
| Grande (Raid) | 53 / 48 | — |

- **Profondeur** (Raid) : une IA sans renforts s'effondre. Coalition sans : 8 % ; Clans sans : la Coalition gagne 78 %. C'est le pilier le plus lourd mesuré jusqu'ici.
- **Par bataille** (Raid) : ~13 arrivées pour la Coalition et ~11 pour les Clans, 2 à 4 sauts d'urgence par camp, 700 à 900 DT dépensés.
- **Diagnostic** (`RF_OFF=call,ambush,ejump`) :
  - Au Secteur, c'est l'appel de renforts qui déséquilibrait : les lentilles des Clans s'ouvraient au bord de la zone. Elles sont maintenant interdites à moins de 700 u du secteur.
  - Au Raid, c'est l'embuscade de la Coalition. L'IA choisit maintenant des sorties couvertes par ses gros canons.

**Lot 2, prix et doctrines** (Annihilation, Bataille, 32 batailles par case, bruit ±9 points ; victoires de la Coalition) :

| Coalition \ Clans | Équilibrée | Artillerie | Blindage | Essaim | Orionum |
|---|---|---|---|---|---|
| **Équilibrée** | 53 % | 72 % | 63 % | 75 % | 56 % |
| **Artillerie** | 38 % | | | | |
| **Blindage** | 50 % | | | | |
| **Essaim** | 63 % | | | | |
| **Orionum lourd** | 53 % | | | | |

- Aléatoire équilibré contre aléatoire équilibré : 53 % (Raid), 69 % (Annihilation).
- La doctrine « Équilibrée » reprend exactement la flotte mesurée juste pour l'objectif et la taille : aux budgets d'objectif, elle reproduit l'équilibre du lot 1 ci-dessous.
- Les deux dernières colonnes des Clans ont été retouchées après cette mesure (elles ne sacrifient plus d'Éventreurs) : à remesurer.
- **Prix** (crédits) :

  | Classe | Prix | Classe | Prix |
  |---|---|---|---|
  | Bastion | 285 | Mastodonte | 185 |
  | Égide | 250 | Ruche | 180 |
  | Rempart | 165 | Éventreur | 110 |
  | Trébuchet | 130 | Écorcheur | 65 |
  | Hospitalier | 55 | Murmure | 35 |
  | Hoplite | 55 | Semeur | 25 |
  | Vigile | 35 | Grêle | 25 |

  Ils sont mesurés en simulation (CONCEPT § 11 ter), puis calés pour que chaque flotte préréglée juste coûte son budget.

**Lot 1, grandes flottes** (victoires Coalition / Clans ; 40 batailles par case et deux graines quand il y en a deux, bruit ±8 points ; grande taille : 16 batailles, ±12) :

| Objectif | Escarmouche (~6) | Bataille (~16, défaut) | Grande (~30) |
|---|---|---|---|
| Raid sur Wolf 359 | 55 / 45 | 50 / 50 | 56 / 44 |
| Annihilation | — | 53 / 48 et 48 / 53 | 56 / 44 |
| Escorte de convoi | — | 48 / 53 et 45 / 55 | 38 / 63 |
| Tenir le secteur | — | 45 / 55 | 50 / 50 |

- Les objectifs se calent par des **coques en plus** pour un camp, propres à chaque objectif et à chaque taille (`extra`, `extraBySize` dans `data.ts`). Elles servent de leviers fins faute de mieux : une seule coque déplace le résultat de 20 à 40 points (un Éventreur de plus en Annihilation : 80 % → 38 %).
- La Tanière grandit avec la flotte qui l'attaque (racine carrée jusqu'à la Bataille, puis exposant 1,3 au-delà) ; les cargos du convoi grandissent avec la meute au-delà de la Bataille (exposant 1,7).
- Par bataille (Raid, ~16) : ~38 mines posées, ~6 explosions sur coque ; ~2 500 de coque réparée par les Hospitaliers.
- Performance : une Grande bataille (~60 coques) prend ~0,8 ms par pas de sim en headless (12 min de bataille en 12 s).

**v1 Horloge + Terrain, escarmouche** (80 batailles par case) :

| Mesure | Valeur | Cible |
|---|---|---|
| Victoires Coalition / Clans (Raid) | 53 % / 48 % et 51 % / 49 % | ~50/50 |
| Durée moyenne | 10,9 min | 10–15 min |
| Orionum / bataille | Coalition ~24 usages (bulle 7, surcadençage 7, élan 4, stase 3,5, fronde 2,5) · Clans ~8 (stase 5,5, bulle 2) | jamais gratuit, toujours visible |
| Sans Terrain (Horloge seule) | 31 % / 69 % | à rééquilibrer (voir Points ouverts) |
| Champ de puits | 26 % / 74 % | carte d'essai |

**Profondeur de l'Orionum après la réparation** (taux de victoire de la Coalition, Raid ; bruit ±5,5 points par case) :

| Horloge + Terrain | Clans sans | Clans avec |
|---|---|---|
| **Coalition sans** | 49 % | 25 % |
| **Coalition avec** | 83 % | 53 % |

| Terrain seul (fronde) | Clans sans | Clans avec |
|---|---|---|
| **Coalition sans** | 49 % | 49 % |
| **Coalition avec** | 63 % | 63 % |

Lecture :
- Chaque camp tire désormais un vrai bénéfice de son Orionum : +34 points pour la Coalition seule (−14 avant la réparation), +24 pour les Clans. Les deux s'annulent en duel, et le Raid revient à 50/50.
- Sans l'Horloge, la fronde de la Coalition vaut +14 : utile, pas décisive (l'ancrage d'avant valait +56).
- Le module brut paie la fronde 64 et en sortait perdant (−12 mesuré) : l'IA des Clans ne l'utilise plus (d'où 49 % dans la case « Clans avec »), mais un joueur peut la tenter.
- Mesures précédentes (Terrain v1 : IA de terrain ≈ +4 sur le Raid, +30 pour le défenseur sur le Champ de puits) : voir CONCEPT § 5.3 bis.

Leviers : coque de la Tanière (9 200 : règle la base sans Orionum), `TUNING.damageScale` (durée), `droneSubsystemMult` (poids des drones), `tracking` des armes (brawlers vs capitaux), dégâts des lasers de l'Hoplite (très sensible : 4,8 → 43 %, 5 → 56 % pour la Coalition), cadence et souffle des bombes.

## Points ouverts

- **Annihilation et renforts** : avec le rattrapage par les pertes, plus aucune bataille ne finit par anéantissement (40 sur 40 aux points). À trancher : moins de DT par perte dans cet objectif, ou une fin quand un camp n'a plus de capital.
- **Équilibre très sensible** : la dotation de DT au départ des Clans fait passer le Raid de ~65 % à ~37 % pour 100 DT.

- **Prix (lot 2)** : une coque déplace une bataille de 20 à 40 points, et les doctrines dévient encore jusqu'à ±25 points de l'équilibre. Les prix sont une première mesure, à affiner avec les parties jouées.
- Les Clans n'ont pas de module stable ou maîtrisé à acheter : mesuré, il les faisait perdre (l'IA ne sait pas en tirer parti). C'est aussi le canon (Orionum brut).

- **Grandes flottes** : l'équilibre par objectif tient à des coques en plus, très sensibles ; l'écran à crédits (lot 2) remplacera ces leviers par des budgets.
- L'IA ne remorque presque jamais (0 coque ramenée en IA contre IA) : un allié aux moteurs coupés est souvent détruit avant que l'Hospitalier n'arrive. Le joueur, lui, peut le faire.
- Les Trébuchets tirent peu en courbe (l'IA réserve le tir courbe aux capitaux et porteurs).
- Rendu de ~60 coques non mesuré sur un vrai GPU (le test en rendu logiciel n'est pas représentatif).

- Le Champ de puits favorise les Clans (30/70) : le tir courbe depuis l'abri de Le Voile. Carte d'essai, gardée telle quelle pour sentir le pilier ; à rééquilibrer si elle devient une vraie carte.
- L'équilibre du Raid est calé avec les deux piliers. Sans Terrain, la Coalition perd sa fronde et les Clans dominent (31/69) ; un réglage de la Tanière par jeu de piliers, ou des budgets par l'écran de paramétrage, le règlera.
- La force de l'Orionum de la Coalition tient beaucoup à la fronde combinée à l'Horloge : ses bombes, relancées, arrivent avant que la stase des Clans ne les fige. L'IA des Clans déclenche donc sa stase au temps d'arrivée de la bombe, plus à la distance.
- Le module brut des Clans ne peut presque jamais surcadencer ni rembobiner (coût ≈ seuil de résonance) : l'IA ne le fait pas, un joueur peut le tenter à ses risques.

- Beaucoup de fins au chrono (45/80) : la Coalition approche lentement. À voir en playtest si c'est de la tension ou de l'ennui.
- Les profils IA (prudente / casse-cou) ne sont pas encore équilibrés entre eux.
