# ORIONUM — Combat de flottes temps réel · Concept du proto

**Statut :** proto exploratoire — produit distinct d'*Orionum Battles* (TCG)
**Version :** 0.1 — 2026-10-13
**Stack :** TypeScript · Vite · three.js (dossier isolé `fleet-proto/`, migration vers un repo dédié plus tard)
**Lore :** défère au canon partagé `poiesis-skills/skills/orionum-canon/CANON.md` (v1.4)

---

## 1. Intention

Un combat de flottes **temps réel en 3D libre**, dans la lignée de *Nexus: The Jupiter Incident* (et, plus loin, *Homeworld* / *Haegemonia*), dans l'univers Orionum.

Deux exigences en tension, assumées :

- **Efficace** — gestion fine d'une poignée de coques nommées : énergie, sous-systèmes, boucliers directionnels, ciblage précis.
- **Sublime** — une bataille qu'on a envie de *regarder* : rythme lourd, caméra cinématique, décor spectaculaire, mort d'une coque qu'on connaît par son nom.

La pause tactique (avec file d'ordres) est la soupape qui permet les deux.

### La démarcation : à trouver par le proto

Lore Orionum seul ≠ démarcation. Ce qui doit rendre le jeu unique, c'est **l'Orionum comme mécanique** (gravité + temps, une seule courbure — CANON § 2 / § 2 bis). Trois pitchs candidats, **départagés par les playtests** (§ 9) :

| Pilier | Pitch candidat |
|---|---|
| **Horloge** | *« Chaque vaisseau vit à sa propre vitesse. Ralentis l'ennemi, accélère les tiens, rembobine une erreur — et paie chaque seconde volée. »* |
| **Terrain** | *« Des puits de gravité-temps sculptent le vide : on tire par-dessus, on se cache derrière, on s'y fait broyer. »* |
| **Arme / Instabilité** | *« Chaque vaisseau Orionum est une bombe que tu pilotes. Plus tu tires sur la corde, plus la bataille devient folle. »* |

---

## 2. Décisions actées (grill du 2026-10-13)

| Sujet | Décision |
|---|---|
| ADN | **Nexus** : peu de coques, sous-systèmes, ordres relatifs, zéro économie de base |
| Victoire | **Objectif de raid** (v0 : détruire le dépôt) |
| Échelle | **< 10 vaisseaux contrôlables** par camp + **100–300 drones** autonomes |
| Durée | **10–15 min** par bataille |
| Roster | **4 classes**, 5–7 coques par camp : Brawler (S), Porteur (M), Capital (L), Escorte DCA |
| Sous-systèmes | **Moteurs, Armes, Boucliers** (+ module Orionum en v1) |
| Boucliers | **Directionnels**, 6 faces (avant/arrière/bâbord/tribord/dessus/dessous) — héritage des 5 segments de Battles |
| Énergie | **Postures 1 clic** (Assaut / Défense / Fuite / Équilibré) + **triangle à curseur** en gestion vaisseau |
| Ordres | **Relatifs par défaut** (attaquer, orbiter, escorter, garder la distance), positionnel en option |
| Pause | **Pause tactique + file d'ordres conditionnels** |
| Drones | **Décisifs avec contre-jeu** (DCA), lancés par les porteurs (héritage des hangars), pilotés par des *cores IA bridés* (canon § 9.2) |
| Renforts | **Oui, par saut gravitationnel** (v2), payés en DT |
| DT | Gagnées par **ses propres pertes** (drones compris) → paient renforts + capacités tactiques (v2) |
| Coût Orionum | **Instabilité par vaisseau**, résonance = **effet de zone** (alliés compris) |
| Caméra | **3D libre** + switch rapide : commandement global / gestion vaisseau / ciblage précis |
| Contemplatif | Caméra cinématique auto · rythme lent et lourd · caméra embarquée · mode photo |
| DA | **Hybride** : fonds peints réalistes (façon Nexus) + vaisseaux en **ligne claire** (contours, aplats) + Orionum **violet électrique** émissif |
| Factions | **Coalition Solarienne vs Clans de Tartare** |
| Décor | **Wolf 359** — naine rouge en éruption, refuge des Clans ; éruptions = **danger périodique** (v2) |
| Commandants | 1 par camp (v2) : **Amiral Voss** vs **Capitaine Reva** |
| Joueur | Choisit son camp au lancement, l'IA prend l'autre (+ mode spectateur IA vs IA) |
| IA | **Utility AI à 2 étages** (commandant + capitaines), mêmes règles et même information que le joueur, joue l'instabilité selon son profil |
| Équilibrage | **IA vs IA headless** déterministe dès le départ |
| Modèles 3D | Coques **procédurales** en placeholder + chargement **glTF** interchangeable (les sites de packs CC0 sont bloqués depuis l'environnement de dev) |

---

## 3. Phasage

### v0 — Combat nu (pas d'Orionum)

> **Question à trancher :** *une minute de combat est-elle déjà belle et lisible, sans Orionum ?* Si non, rien d'autre ne la sauvera.

- Scénario **Raid sur Wolf 359** : la Coalition doit détruire le dépôt d'Orionum des Clans avant la fin du chrono (12 min).
- 6 vaisseaux par camp (4 classes) + dépôt + drones.
- Mouvement 3D, armes (cinétiques balistiques, missiles, DCA, canons de drones), boucliers directionnels, sous-systèmes, postures d'énergie + triangle.
- Ordres relatifs + positionnel, file d'ordres (Maj), déclencheurs conditionnels.
- Pause tactique, vitesse ×0.5 / ×1 / ×2.
- IA utility à 2 étages, jouable des deux côtés.
- Caméra : tactique libre, focus, embarquée, cinématique auto, mode photo.
- Runner headless IA vs IA + tests de déterminisme.
- **Fondations posées pour v1** : temps local par entité (`timeScale`), historique d'états par vaisseau (fenêtre rétroactive), RNG seedé.

**Critère de sortie v0 :** 5 playtesteurs regardent 2 min de bataille IA vs IA et la décrivent comme « belle » ; un joueur comprend qui gagne et pourquoi sans explication ; une bataille IA vs IA se termine (pas d'impasse) dans 95 % des seeds.

### v1 — Orionum, un pilier à la fois

> **État (2026-10-15)** : instabilité, Horloge (§ 5.2 bis et ter) et Terrain (§ 5.3 bis) livrés, chacun derrière un interrupteur ; Arme à venir.

Chaque mécanique derrière un **flag**, chacune avec un **scénario de test dédié**, et l'IA apprend chaque mécanique **au moment où elle est activée** (sinon le playtest ne vaut rien).

- **Instabilité** (socle commun, toujours active en v1).
- **Horloge** : bulle de ralenti · surcadençage · fenêtre rétroactive · stase.
- **Terrain** : puits fixes · projectiles balistiques courbés.
- **Arme** : lance gravitationnelle · effondrement local · onde de désynchronisation.

### v2 — Méta-bataille

DT, renforts par saut gravitationnel, commandants (Voss / Reva), éruptions de Wolf 359.

### Plus tard (hors proto de mécaniques)

Persistance de flotte, équipages, exil temporel, campagne, loadout, abordage, PvP.

---

## 4. Le combat (v0)

### 4.1 Classes

| Classe | Rôle | Coalition | Clans |
|---|---|---|---|
| **Brawler (S)** | Rapide, courte portée, flanque | Frégate *Hoplite* (canons laser) | Raider *Écorcheur* (lasers courts + missiles) |
| **Porteur (M)** | Lance des drones, batterie légère | Porte-drones *Égide* (stock réduit, drones solides, bombe lourde) | *Ruche* (stock massif, drones fragiles) |
| **Capital (L)** | Artillerie lourde, porte l'Orionum (v1) | Croiseur *Bastion* (railgun axial + turbolasers) | *Mastodonte* (minéralier armé, batteries de flanc + bombe à fusion + missiles) |
| **Escorte DCA** | Défense rapprochée anti-drones et anti-missiles | *Vigile* | *Grêle* |
| **Dépôt** (Clans) | Objectif, immobile, DCA + batterie | — | Dépôt d'Orionum |

Asymétrie : la Coalition, c'est **l'ordre** — moins de coques mais plus lourdes, peu de drones, de l'artillerie. Les Clans, c'est **le chaos** : essaims, missiles, coques bricolées.

### 4.2 Armes et arcs

Les armes ont un **montage** (direction locale) et un **arc** : le railgun est axial (il faut pointer la coque), les batteries tirent par le flanc. Orienter sa coque sert donc deux fois : présenter ses armes, et présenter sa meilleure face de bouclier.

- **Cinétique** : projectile balistique simulé (prépare la courbure gravitationnelle de v1).
- **Missiles** : guidés, lents, interceptables par la DCA.
- **DCA** : instantané (*hitscan*), courte portée, probabiliste, contre les drones, les missiles et les bombes uniquement.
- **Lasers et bombes** : voir § 4.3 sexies.
- **Canons de drone** : instantanés, très courte portée.

### 4.3 Dégâts

1. On détermine la **face de bouclier** touchée selon la direction d'arrivée, exprimée dans le repère du vaisseau.
2. La face absorbe ; le **débordement** passe à la coque.
3. Le débordement touche aussi un **sous-système** : celui visé par l'attaquant (ciblage précis), sinon un sous-système aléatoire.
4. Un sous-système à 0 est **hors service** : moteurs (vitesse ×0.2), armes (plus de tir), boucliers (faces effondrées, pas de régénération). Il se répare lentement jusqu'à 50 %.
5. Coque à 0 → **destruction**.

### 4.3 bis Drones et suivi (ajoutés après playtest IA vs IA)

- **Drones = bombardiers de sous-systèmes** : ils passent **sous** les faces de bouclier, frappent le sous-système visé par leur porteur (armes par défaut) avec un multiplicateur de précision, et ne font que peu de dégâts à la coque. Ils sont décisifs par l'**effet** (désarmer, immobiliser, dépouiller les boucliers), pas par le DPS. Le contre-jeu, c'est la DCA et la destruction du porteur.
- **Drones orphelins** : un core bridé sans lien avec son porteur s'éteint après 12 s (canon § 9.2). Tuer le porteur, c'est désarmer l'essaim.
- **Suivi des tourelles** : chaque arme cinétique a une vitesse de suivi angulaire ; au-delà, la dispersion augmente. Les gros canons ne suivent pas un brawler qui orbite de près. C'est l'écho temps réel de la riposte asymétrique de Battles (S/M/L).

### 4.3 ter Vol newtonien assisté (lot 1, 2026-10-14)

On est dans l'espace, et les vaisseaux ne volent pas comme des avions.

- **Aucun frottement** : la vitesse persiste tant qu'on ne contre-pousse pas. Les vaisseaux **glissent** moteurs coupés.
- **Moteur principal** puissant, uniquement dans l'axe du nez. Les **propulseurs de manœuvre (RCS)** sont faibles et omnidirectionnels : environ 8 % du moteur principal pour un capital, 30 % pour un brawler. Pour freiner fort, il faut **se retourner et pousser** (flip-and-burn).
- **Inertie angulaire** : la rotation accélère et décélère. Un capital met environ 16 s à se retourner, un brawler environ 4 s.
- **Ordinateur de vol** : il transforme les ordres en poussées. Il pivote d'abord, puis allume le moteur principal une fois le nez aligné. Il plafonne la **vitesse de sécurité**, c'est-à-dire la vitesse dont on peut encore s'arrêter en comptant le temps de retournement.
- **Pointage par vaisseau**, indépendant du mouvement : Auto, Cible, Bordée, Mouvement, Meilleur bouclier. Un vaisseau peut reculer en tirant.
- **Nouveaux ordres** :
  - **Passe balistique** : accélérer, couper les moteurs, traverser en glissant nez sur la cible, puis revenir.
  - **Repli en tirant** : reculer jusqu'à la portée max en gardant le nez et les armes sur la cible.
- **Le suivi des tourelles s'applique à la vitesse relative** : une passe rapide contre une cible agile rate. La passe est faite pour les grosses cibles lentes, et l'IA des Clans l'utilise sur les capitaux et le dépôt.
- **Visuels** : flamme moteur proportionnelle à la poussée, bouffées RCS, vecteur vitesse projeté à 5 s en vue tactique.

### 4.3 quater Commandement de flotte et analyse (lot 2, 2026-10-14)

**Groupes tactiques.** Trois groupes sont pré-remplis par rôle : **Ligne** (capital et escorte), **Lanciers** (brawlers) et **Hangar** (porteurs). On les réassigne avec « + sélection ». Chaque groupe a une cible (désignée, ou automatique selon sa priorité) ou une destination, une **formation** et une **doctrine**. Un ordre direct donné à un vaisseau le **détache** de son groupe, jusqu'à ce que le groupe reçoive un nouvel ordre.

**Formations** (postes calculés dans un repère orienté vers la menace) :

| Formation | Placement |
|---|---|
| Ligne de bataille | Côte à côte, perpendiculaire à l'ennemi |
| Mur | Ligne serrée, porteurs et capitaux abrités derrière |
| Écran | Escortes devant, protégés derrière |
| Coin | Pointe vers la cible |
| Sphère | Couverture à 360° |

**Doctrines** :

| Réglage | Options |
|---|---|
| Distance d'engagement | Portée max / optimale / mêlée |
| Priorité de tir | Capitaux, porteurs, brawlers, le plus faible, le plus proche, dépôt ; sous-système visé par défaut selon la cible |
| Repli automatique | Jamais / 50 % / 30 % : repli en tirant, puis saut |
| Politique drones | Attaque / écran / **réserve** (lancer seulement si le porteur est menacé) / **tout lancer** |

**Manœuvres coordonnées** :
- **Tenaille** : deux groupes prennent la cible par des flancs opposés.
- **Arrivée synchronisée** : les rapides ralentissent pour arriver à portée en même temps que les lents.
- **Face faible** : toute la flotte se place du côté de la face de bouclier la plus basse de la cible.
- **Repli en échelon** : un groupe se replie en tirant pendant que les autres couvrent, à tour de rôle, puis saut.

**Pointage manuel**, en plus des modes du lot 1 :
- **Pointer vers…** : un vaisseau ou un point de l'espace.
- **Exposer** : une face choisie, présentée à la menace.
- **Verrouiller** : l'orientation est figée ; les flèches la font pivoter (lacet et tangage).

**Contrôle de tir.** Une tourelle abandonne une cible qu'elle ne peut pas suivre et en choisit une qu'elle peut toucher. Les gros canons ne gaspillent plus leurs tirs sur un brawler collé à eux.

**Analyse en temps réel** :
- **Projection simulée** : le core tactique clone la bataille et fait tourner la vraie simulation, qui est déterministe, **60 s en avance** avec les ordres actuels. Il en tire l'état du dépôt, les coques perdues et leur heure, et les sous-systèmes neutralisés. Le calcul est découpé en tranches de 6 ms par image, environ 1 s par cycle, relancé toutes les 5 s et à chaque pause.
- **Officier tactique** : un core IA bridé (canon § 9.2) propose des actions (repli en tirant ou saut d'une coque condamnée, feu concentré, tenaille, politique drones, face faible), **teste chacune en projection** à partir du même instantané, et n'affiche que **la meilleure**, avec son gain et son effet (« Vesta survit (41 %) »). On l'applique d'un clic ou avec Alt+1. Il propose seulement les gains supérieurs au bruit de la projection, ne lance pas de nouvelle manœuvre de flotte tant qu'une autre est en cours, puis **se tait 20 s** après chaque conseil appliqué. Il n'agit jamais seul.
- **Matrice d'engagement** (X) : pour chaque vaisseau, qui le vise (drones compris), le feu entrant, le temps qu'il peut tenir, sa coque à T+60, sa face faible et ce qu'il peut toucher.
- **Carte des menaces** (T, automatique en pause), sur le plan tactique : la portée des gros canons ennemis, leur **zone morte de suivi** pour tes brawlers, et les essaims de drones.

**L'IA ennemie** commande avec les mêmes groupes, formations et doctrines. En difficulté **Difficile**, elle lit aussi sa propre projection et suit son officier, avec une recharge de 10 s. Sur 16 batailles face à l'IA normale, elle gagne **75 %** côté Clans et **88 %** côté Coalition. L'évaluation de l'officier tient compte de l'objectif : pour l'attaquant, le dépôt pèse triple et un vaisseau qui saute hors du combat ne vaut presque plus rien.

**Panneau Commandement repliable** (G ou bouton) : replié, il garde le rapport de force, la projection et le conseil du moment.

### 4.3 quinquies Boucliers visibles et son (2026-10-14)

**Boucliers visibles à l'impact.** Chaque coque porte une enveloppe ellipsoïde ajustée à sa silhouette, dessinée en **trame hexagonale encrée** à la couleur de la faction. Elle est invisible au repos.
- À l'impact, **seul le secteur de la face touchée s'allume**, avec une onde qui part du point d'impact. Taille et intensité suivent les dégâts : un railgun ne ressemble pas à un canon de drone.
- Une face presque vide **grésille** quand on la touche.
- Une face qui **tombe à zéro** produit un éclat de rupture, avec des cellules qui s'effacent ; les tirs suivants frappent la coque.
- Un vaisseau **sélectionné ou survolé** garde une enveloppe faible, chaque face teintée selon sa charge (couleur de faction quand elle est pleine, rouge quand elle est vide).
- Les **drones** passent sous les boucliers : leurs tirs font des étincelles directement sur la coque, sans allumer l'enveloppe.

**Sound design** (en place, 37 cues et 71 variantes, couleur space opera « Star Wars ») :
- **Sources** : Freesound (CC0 en majorité, sons CC-BY crédités) et **synthèse procédurale** (`tools/audio/synth.py`) pour ce que les banques ne couvrent pas proprement : blasters à la Ben Burtt (chirp dispersif de ressort + ton qui chute), turbolasers, couche grave des bombes, passages rapprochés, allumages moteur, alertes. Tout est tracé dans `public/audio/credits.json`, avec un écran Crédits dans le menu.
- **Chaîne** : `tools/audio/build_audio.py` mélange les couches (Freesound ou synthèse) de chaque variante, coupe les silences, rend les boucles sans couture, nivelle par fenêtre de 300 ms, encode en MP3 et écrit `manifest.json` (nombre de variantes par cue).
- **Variété** : plusieurs variantes par son, jamais deux fois la même d'affilée ; hauteur ±4 % et niveau ±1,5 dB aléatoires ; la hauteur suit la taille de la coque (un capital sonne plus grave qu'une frégate) ; au loin, le claquement s'efface et un écho grave et long prend le relais (envoi vers une réverbération sombre), et les grosses explosions lointaines passent sur des enregistrements d'explosions distantes.
- **Lasers** : timbres distincts par camp (Coalition propre et aiguë, Clans sale et saturée), turbolasers lourds pour les capitaux, petits blasters pour les drones.
- **Bombes** : lancement sourd, grondement pulsé qui suit les bombes proches de la caméra, explosion dévastatrice à trois couches (deux enregistrements + sub synthétique).
- **Moteurs** : plus de boucle continue. Restent l'allumage du moteur principal des grosses coques, les bouffées des moteurs de manœuvre (discrètes, seulement près de la caméra) et le « whoosh » des passages rapprochés en vue cinéma.
- **Alertes** (tous les vaisseaux) : coque sous 40 % (klaxon lent), sous 20 % (plus rapide et plus aigu), sous 10 % (sirène montante) ; face de bouclier tombée sous le feu (double bip) ; sous-système hors service (motif distinct pour moteurs, armes, boucliers) ; bombe en approche (bip de proximité qui accélère et monte à l'approche). Le volume suit la distance à la caméra, plus localisé que les combats ; un vaisseau sélectionné ou survolé reste audible de loin ; les alertes ennemies sont étouffées ; au plus trois vaisseaux sonnent à la fois, les plus graves et les plus proches d'abord.
- **Mixage contre la bouillie** (retour de playtest : les gros sons s'empilaient, et le dézoom augmentait le brouhaha) :
  - trois étages : sons légers (atténuation rapide, sans écho lointain), tirs lourds, grosses explosions (portent loin) ;
  - budget : au plus 2 tirs lourds et 1 grosse explosion à la fois ; un son plus fort vole la voix du plus faible, sinon il est abandonné (une explosion devient un grondement lointain court) ;
  - ducking : une grosse explosion proche baisse les tirs et les alertes d'environ 9 dB pendant une seconde ;
  - en vue tactique, l'auditeur est au point visé, et la distance de zoom s'ajoute comme pénalité : dézoomer éloigne tout. Mesure sur une bataille : dézoomé, le niveau moyen passe de −15 dB à −32 dB (avant, dézoomer rendait plus fort que zoomer) ; tirs lourds et explosions simultanés divisés par deux.
- **Direction hybride selon la caméra** :
  - en vue tactique, le son tel que perçu par la coque et les capteurs : passe-bas 1,1 kHz, compression, nappe de passerelle (les alertes restent nettes) ;
  - en caméra cinématique ou embarquée, un son cinéma plein, filtre ouvert, nappe spatiale.
- **Musique** : thème de Thomas Lheureux (sous licence), dans `public/audio/music/`, listé dans `playlist.json`.

### 4.3 sexies Lasers et bombes (2026-10-14)

- **Lasers** : forts sur bouclier (×1,6), faibles sur coque (×0,6). Le cinétique (railgun, batteries, autocanons) frappe pareil partout, donc mieux sur coque : on déshabille au laser, on perce au cinétique.
  - **Traits de blaster** (Hoplite, Écorcheur) : projectiles rapides et colorés par camp (cyan Coalition, rouge Clans).
  - **Turbolasers** (tourelles du Bastion) : rayon instantané, épais et bref. Le rayon part avec un retard de visée de tourelle (0,6 s) : il touche les grosses coques lentes et manque les raiders rapides qui croisent.
- **Bombes** (Égide : bombe lourde ; Mastodonte : bombe à fusion) : lentes (100 à 110 u/s), guidage faible (un vaisseau qui manœuvre tôt peut l'éviter), fusée de proximité, souffle de zone qui touche **aussi les alliés**, moitié des dégâts à travers les boucliers. Elles visent les capitaux, porte-drones et stations ; le tireur retient son tir si un allié est près de la cible. La défense rapprochée et les drones en écran peuvent les abattre (points de vie).
- Équilibrage après ajout : 52/48 sur 160 batailles, 10,9 min en moyenne ; IA difficile 63 % (Coalition) et 69 % (Clans) face à l'IA normale.

### 4.3 septies Destruction, essaims, bombe à énergie (2026-10-14)

**Destruction en plusieurs temps.** À 0 de coque, le vaisseau ne disparaît pas.
- **Agonie** (2,6 à 8 s selon la taille) : moteurs et boucliers morts, il part en vrille, brûle, enchaîne des explosions secondaires de plus en plus rapprochées et perd des morceaux de lui-même (les vraies pièces de sa coque).
- **Rupture du réacteur** : une détonation finale à zone (rayon 60 + 3,2 × rayon de coque, environ 200 u pour un capital) qui touche tout le monde, alliés compris, en partie à travers les boucliers. Puis la coque se brise : toutes ses pièces s'éparpillent, rougeoyantes, refroidissent et dérivent. Elles restent jusqu'à la fin de la bataille (champ d'épaves, plafonné à 220 morceaux).
- Le pilote de vol s'écarte des épaves en agonie (tous camps) ; on peut quand même y laisser un ennemi collé. La caméra cinématique coupe sur l'agonie et la tient jusqu'à la rupture.

**Plus de drones.** Porte-drones doublés (Égide 52 en stock et 24 actifs, Ruche 56 et 28), petites escadrilles d'interception sur le Bastion et le Mastodonte (6 et 7 actifs), lancement en vagues. Un tiers (Égide) ou un quart (Ruche) des vagues sont des **intercepteurs** : rapides, ils chassent drones, missiles et bombes et ne touchent jamais une coque.

**Bombe à énergie.** Une par capital et par bataille.
- Déclenchement : bouton de la fiche du capital ou touche N, puis clic sur le cœur du groupe ennemi. L'officier tactique peut la proposer (testée en projection) ; l'IA la garde pour le contact.
- Projectile lent mais coriace (abattable, difficilement). À l'impact : flash blanc-violet immense, toutes les faces de bouclier à zéro dans un rayon de 350 u (alliés compris), drones grillés, régénération bloquée 5 s puis redémarrage rapide (×6) jusqu'au plein. Aucun dégât de coque : c'est l'ouverture pour la salve suivante.
- Le dépôt est durci : ses boucliers sont divisés par deux, sans blocage.
- Option « Atténuer les flashs » (photosensibilité) dans le menu pause et le menu principal.
- Équilibrage : sans le redémarrage rapide, une seule bombe décidait la bataille (jusqu'à 83/17 ou 13/88 selon celle qui passait). Avec : 49/51 sur 160 batailles, 10,1 min. IA difficile face à l'IA normale : 56 % côté Coalition, 75 % côté Clans (16 batailles chacun).

### 4.3 octies Les gros vaisseaux tiennent la mêlée à distance (2026-10-14)

- **Bulle de sécurité** : capitaux 900 u, porte-drones 1 100 u (coque à coque). Un ennemi qui y entre fait reculer le gros en tirant, avec une glissade latérale sur les moteurs de manœuvre. Un ordre « Attaquer » le mène à portée de tir, jamais en deçà de la bulle.
- **Zone de mort** : les petits vaisseaux évitent la moitié de la bulle d'un gros ennemi. La mêlée se joue entre petits ; ils n'entrent chez les gros que par une **passe balistique** (doctrine Mêlée) ou un ordre explicite.
- **« Au contact »** : nouvel ordre (avec confirmation) qui lève la bulle d'un gros vaisseau. L'IA ne l'utilise pas.
- **Écran** : les Lanciers de l'IA interceptent d'abord tout ennemi entré dans la bulle d'un de leurs gros.
- **Portées des gros ×1,5** (railgun, turbolasers, batteries, bombes, missiles des porteurs), obus plus rapides ; dépôt 1 220 u ; Coalition déployée un peu plus loin.
- Mesure (12 batailles) : avant, le Bastion avait un ennemi à moins de 450 u de sa coque 31 % du temps, avec des contacts ; après, 14 %, sans contact hors passes.
- Équilibrage : 52/48 sur 160 batailles, 10,5 min.

## 4.4 Énergie

Un triangle (Armes / Boucliers / Moteurs), dont la somme vaut 1, module :

- la cadence de tir,
- la régénération et le maximum des faces,
- la vitesse et le virage.

En posture **Défense**, la face la plus menacée est en plus renforcée.

### 4.5 Ordres

| Ordre | Type | Effet |
|---|---|---|
| Attaquer | relatif | Se place à la portée préférée de sa classe, oriente ses armes, peut viser un sous-système |
| Orbiter | relatif | Tourne autour d'une entité à distance donnée |
| Escorter | relatif | Reste en formation près d'un allié |
| Garder la distance | relatif | Kite : maintient une distance d'une cible |
| Déplacer | positionnel | Point dans l'espace |
| Tenir | — | Maintient la position |
| Retraite | — | Revient vers la zone de départ |

**File d'ordres** (Maj) et **déclencheurs** :

- immédiat ;
- quand la cible est à portée ;
- dans 10 s ;
- si la coque passe sous 50 %.

---

## 5. Orionum (v1) — spécifications de départ

> Canon : jamais gratuit (§ 9.5), toujours violet électrique et toujours gravité + temps (§ 9.7). Instabilité spontanée (§ 2).

### 5.1 Instabilité (socle)

- Une jauge par vaisseau équipé d'un module Orionum, de 0 à 100.
- Chaque usage ajoute de l'instabilité ; elle décroît lentement, et plus lentement encore si le module est endommagé.
- **Au-delà de 70** : risque de **résonance** par seconde, croissant.
- **À 100** : résonance certaine. C'est un **effet de zone aléatoire** (micro-stase, puits instantané ou vague de désynchronisation), qui frappe **alliés et ennemis** et laisse le vaisseau source fortement endommagé.
- Conséquence : on **écarte** ses vaisseaux instables, et on peut les **jeter** au milieu de l'ennemi.

### 5.2 Horloge

| Mécanique | Effet | Coût |
|---|---|---|
| Bulle de ralenti | Zone où `timeScale` = 0.5 (projectiles et vaisseaux compris) | instabilité moyenne |
| Surcadençage | Le vaisseau passe à `timeScale` = 1.75 pendant X s | instabilité continue + désynchronisation |
| Fenêtre rétroactive | Le vaisseau revient à son état d'il y a 5 s (position, coque, sous-systèmes) | instabilité très élevée |
| Stase | Zone figée : rien n'entre, rien ne sort, les projectiles restent suspendus ; les munitions guidées s'y éteignent | instabilité élevée |

### 5.2 bis Horloge v1 livrée : ce qui a été fixé (grill et mesures du 2026-10-14)

- **Un module par capital**. **Ordre contre chaos** : Bastion (Coalition) **stable**, coûts ×0,6 ; Mastodonte (Clans) **brut**, coûts ×1,6, puissance ×1,15, risque ×2,5.
- **Valeurs** (`ORIONUM` dans `src/sim/data.ts`) :

| | Effet | Coût de base |
|---|---|---|
| Bulle | rayon 280 u, 6 s, `timeScale` 0,5 | 38 |
| Surcadençage | ×1,75 pendant 8 s | 5 / s |
| Rembobinage | état d'il y a 5 s (position, vitesse, coque gardée au maximum des deux, faces, sous-systèmes) | 45 |
| Stase | rayon 220 u, 6 s, figé et invulnérable ; à la fin, missiles, bombes et bombes à énergie pris dedans s'éteignent | 35 |

- **Instabilité** : décroît de 1,2 / s (0,5 si un sous-système est à terre).
- **Résonance « catastrophe locale »** : risque croissant au-delà de 70, certaine à 100. Un effet au hasard autour de la source, alliés compris :
  - micro-stase (180 u, 3,5 s) ;
  - puits (260 u, 3 s, aspire puis broie : 320 dégâts) ;
  - désynchronisation (420 u, 8 s, le temps bégaie).

  La source perd 30 % de coque, son module est coupé 30 s, et l'instabilité retombe à 40.
- **Rembobinage en pause tactique** : autorisé, il s'exécute à la reprise (comme tout ordre Orionum).
- **Commandes** : barre Orionum (jauge, repère à 70, 4 boutons) et Maj+1–4 ; clic pour placer bulle et stase (portée 1 600 u). L'officier tactique peut proposer un coup Orionum.
- **IA** : budget sous le seuil (stable 66, brut 74), au plus un usage toutes les 20 s, dans cet ordre :
  1. rembobine si la coque a perdu plus de 8 % en 5 s ;
  2. fige une bombe qui arrive sur un allié ;
  3. ralentit une grappe ennemie (sans allié à proximité, pas de bulles empilées) ;
  4. surcadence au contact.

**Ce que les mesures ont appris :**

- La bulle **coupe l'attaquant en deux** : la tête arrive seule et se fait battre en détail. C'est l'effet le plus fort du pilier, au point qu'une IA des Clans « bulle seule » gagnait à 96–98 %.
- Ce chiffre est apparu parce que la stase ne faisait que **retarder** les bombes : les Clans gaspillaient leur budget en stases inutiles, et l'équilibre apparent de 50/50 tenait par accident. Correctif : les munitions guidées ne survivent pas à la stase. La stase devient le contre des bombes, et l'IA des Clans s'en sert d'abord pour ça (6,6 stases par bataille).
- Profondeur mesurée (taux Coalition, 160 batailles par case) :
  - aucun camp avec l'Orionum : 52 % ;
  - Coalition seule : 47,5 % ;
  - Clans seuls : 38,5 % ;
  - les deux : 50,5 %.

  L'Horloge du défenseur vaut environ +13 points ; celle de l'attaquant sert à l'annuler. Le pilier joue en duel, il n'est pas décoratif.
- Le module brut ne peut presque jamais surcadencer ni rembobiner : ces coups coûtent environ le seuil de résonance. C'est l'asymétrie voulue (le chaos fige et ralentit, l'ordre manœuvre), à confirmer en playtest.

### 5.2 ter Playtest du pilier (§ 9)

- **Journal de partie** : usages Orionum (joueur et IA), résonances, pertes, bombes à énergie, sauts.
- **Questionnaire de fin de partie**, en cinq questions :
  - le moment marquant ;
  - « as-tu compris ce qui s'est passé ? » (oui, en partie, non) ;
  - sublime, de 1 à 5 ;
  - usage spontané (oui, sur conseil de l'officier, non) ;
  - un commentaire.
- Les deux sont enregistrés dans le stockage de la page de playtest publiée, un sous-arbre par testeur, lisible seulement par le propriétaire. À défaut, ils sont téléchargés en JSON.

### 5.2 quater Réparation Horloge + Terrain (grill du 2026-10-15)

**Décisions**
- **Élan** : nouvelle capacité de l'Horloge, réservée au module stable (Coalition). Zone de 300 u où le temps file à ×1,25 pendant 7 s, pour tous, ennemis compris. Coût 40.
- **Fronde** : l'ancrage devient un puits tournant **sans cœur**. Il attire faiblement (80 u/s² à l'œil, portée 500), et ce qui frôle son œil (150 u) repart ×1,2 plus vite : tirs et bombes au-delà de leur vitesse, drones et coques au-delà de leur croisière pendant 3 s. Ennemis compris. Coût 40. Plus de renfort des puits fixes, plus de cœur qui avale : fini le doublon avec la stase.
- **Critères de réussite** : Horloge de l'attaquant ≥ +5 seule sans casser le 50/50 ; ancrage sans Horloge ni décisif ni nul ; usage IA visible ; une phrase d'info-bulle par capacité, vérifiée par le questionnaire (nouvelle question : « quelles capacités vous ont paru obscures ? »).

**IA**
- Élan sur un groupe allié engagé qui pèse plus lourd que l'ennemi dans la zone.
- Fronde « sur la voie » : 320 u devant un lanceur allié (bombes, missiles, drones) engagé entre 900 et 2 200 u. Jamais à moins de 550 u d'un ennemi, une seule par voie, et jamais au prix de la prochaine stase.
- Le module brut ne s'en sert pas : à 64 d'instabilité, mesurée à −12 points pour les Clans.
- La stase anti-bombe se déclenche désormais au **temps d'arrivée** (moins de 6,5 s), plus à la distance : une bombe relancée par une fronde arrive plus tôt.

**Ce que les mesures ont appris** (80 batailles par case)
1. **Première fronde, ×1,5 et attraction 240 : +50 points pour la Coalition en duel.** Ce n'était pas la relance : une fronde inerte ou sans attraction ne change rien (74 % et 71 %). C'était l'attraction, posée là où arrivent les bombes ennemies :
   - les bombes lentes tournaient autour jusqu'à épuisement (bouclier anti-bombes déguisé) ;
   - les drones ennemis étaient déviés de leur cible (bouclier anti-essaim, qui frappe d'abord les Clans).

   Correctifs : les frondes ne dévient plus les drones ni les munitions guidées, elles les relancent seulement ; attraction ramenée à 80.
2. **Fronde × Horloge** : les bombes relancées passaient sous la stase des Clans, déclenchée trop tard. C'est un vrai contre au contre, mais il faisait basculer la bataille (80/20) : la stase par temps d'arrivée le rend lisible pour l'IA.
3. **La base sans Orionum penchait vers la Coalition** (71-74 %). Le 50/50 d'avant tenait grâce à l'Orionum des Clans, qui contrait les bombes. La coque de la Tanière passe de 6 240 à 9 200, et le laser des Hoplites de 5 à 4,8. Base : 49 %.
4. **Résultat** :
   - Horloge + Terrain : la Coalition +34 seule, les Clans +24 seuls, 53/48 et 51/49 avec les deux ;
   - Terrain seul : fronde de la Coalition +14.

   Critères atteints. Le Raid **sans Terrain** penche désormais vers les Clans (31/69) : l'équilibre est calé sur les deux piliers ensemble.

### 5.3 Terrain

| Mécanique | Effet |
|---|---|
| Puits fixes | Champ d'accélération radial sur la carte : dévie les trajectoires et les projectiles, crée du relief |
| Projectiles balistiques courbés | Les projectiles cinétiques suivent la somme des puits ; on peut tirer « par-dessus » un puits |

### 5.3 bis Terrain v1 livré : ce qui a été fixé (grill et mesures du 2026-10-15)

**Décisions du grill**

- **Puits** : un décor de 2 à 4 puits fixes par carte, plus un **ancrage Orionum** (renforcer un puits fixe ou en ancrer un temporaire).
- **Ce que dévie le champ** : vaisseaux, obus et bombes, drones et missiles, **lasers compris**.
- **Visée** : automatique, plus un ordre **« tir courbe »**.
- **Où tester** : dans le Raid et sur une carte d'essai, le **Champ de puits**.
- **Piliers** : ils se cumulent, chacun derrière un interrupteur (menu et runner headless).
- **L'IA de l'attaquant** reçoit des coups offensifs de l'Horloge (voir plus bas).

**Physique** (`TERRAIN` dans `src/sim/data.ts`, `src/sim/terrain.ts`)

| Élément | Règle |
|---|---|
| Champ | a = attraction · (cœur / d)², effacé entre 75 % et 100 % de la portée. Raid : Charybde (cœur 150, 520 u/s², portée 1 000), Scylla (110, 440, 820), Le Gué (80, 380, 650) |
| Projectiles | Tirs non guidés : champ entier. Munitions guidées : 35 % (leurs moteurs luttent). Ce qui atteint un cœur est avalé : pas d'impact, pas de souffle |
| Coques | Part du champ ressentie selon le rôle : petits 0,018, porteurs 0,005, capitaux 0,0025. Les petits sont déportés, les gros résistent ; dans un cœur, 160 dégâts/s qui ignorent les boucliers |
| Évitement | Marge de 70 u + 0,6 × rayon. Anticipation égale au temps d'arrêt (retournement + freinage), sur la vitesse voulue **et** la dérive réelle. Jamais plus près que la distance où l'attraction dépasse la moitié des propulseurs latéraux. Un point d'ordre tombé dans une marge est repoussé au bord |
| Visée | La conduite de tir simule le tir à travers le champ (même pas et même intégrateur que la simulation) et corrige la visée. Cible derrière un cœur : tir impossible en ligne droite, rayons arrêtés au bord |
| Tir courbe | Obus lancés en cloche à 45, 35 puis 27 % de leur vitesse, hors de l'axe (jusqu'à 50°), pour que le puits les ramène. Bombes : lancées hors de l'axe, guidage retardé. −15 % de cadence tant qu'il est actif |
| Ancrage | Coût de base 30 d'instabilité (stable 18, brut 48). Renforce le puits fixe le plus proche dans un rayon de 600 u (attraction ×1,8, cœur ×1,3, 12 s), sinon ancre un puits temporaire (cœur 100, attraction 400, portée 700, 14 s) |

**Ce que les mesures ont appris**

1. **L'évitement compte plus que la gravité.** Première version : anticipation longue, marge large. La formation de l'attaquant se disloquait autour des puits (13/88 avec les cœurs seuls, sans aucun champ). Un évitement minimal, borné au temps d'arrêt, rend les cœurs neutres (53/48).
2. **Les gros ne fuient pas un puits.** Le Bastion se retourne en ~16 s, avec 0,4 u/s² de propulseurs latéraux. D'où la règle de distance par la poussée : plus d'écrasements.
3. **La gravité sur les coques favorisait le défenseur** (28/73 avec ce seul effet). Divisée par deux, on retrouve 50/50 : l'attaquant traverse le champ, le défenseur l'attend.
4. **Tirer « par-dessus » un puits est impossible à pleine vitesse.** La lentille gravitationnelle focalise à l'intérieur du cœur : l'anneau d'Einstein y est plus petit que le rayon de capture. Le tir courbe passe donc par une **charge réduite** : sous ~500 u/s, le puits peut ramener l'obus. Vérifié en simulation : 9 tirs sur 9 touchent une cible cachée, de 400 à 1 000 u derrière le cœur.
5. **L'IA a essayé, puis abandonné, deux ancrages**, parce qu'ils avalaient ou attiraient autant ses propres tirs, drones et coques que ceux de l'ennemi :
   - un cœur en écran devant son propre capital : −12 points ;
   - le renfort d'un puits plein de petits ennemis : −12 points.

   Elle garde l'ancrage **sur la trajectoire d'une bombe**, et seulement quand la stase n'est pas disponible. Avant cette restriction, il évinçait la stase : −30 points.
6. **Ancrage et stase font double emploi contre les bombes.** Sans Horloge, l'ancrage devient le seul contre aux bombes et décide de la bataille : +56 points pour les Clans. **Question ouverte** : donner à l'ancrage un rôle que la stase n'a pas.
7. **Profondeur** (README) :
   - **Raid** : l'IA de terrain vaut environ +4 points de chaque côté, dans le bruit. Les puits façonnent la carte sans la décider.
   - **Champ de puits** : le défenseur qui tire en cloche derrière Le Voile gagne +30 points. Le terrain devient une arme quand il domine. Cette carte favorise les Clans (30/70), et elle est gardée ainsi comme carte d'essai.

**Coups offensifs de l'attaquant (Horloge, grill du 2026-10-15)**

- **Stase sur l'escorte DCA** qui couvre la cible des bombes en vol. La zone épargne la cible, car une coque figée ne prend pas de dégâts, ainsi que la trajectoire des bombes, qui s'y éteindraient.
- **Bulle sur les défenseurs qui reviennent** vers les vaisseaux engagés.
- **Surcadençage du capital** quand il tient la Tanière sous ses canons.
- **Mesure (Raid, Terrain actif, 80 batailles par case)** : ces coups ne rendent pas l'Horloge de la Coalition plus profonde. Elle la dessert seule (−14 points) et ne pèse rien en duel (−1).
  - Ablation : 60 % avec ou sans les trois coups, et aucune capacité retirée seule ne comble l'écart (56 à 60 %). C'est l'usage d'ensemble qui ne sert pas l'attaquant.
  - L'Horloge des Clans vaut +20 points seule, +7 en duel.
  - **Question pour le prochain grill** : l'attaquant a-t-il besoin d'une capacité propre (par exemple une bulle accélérante pour les alliés) ?

### 5.4 Arme

| Arme | Effet | Coût |
|---|---|---|
| Lance gravitationnelle | Rayon qui ignore les boucliers, dégâts massifs à la coque | instabilité très élevée |
| Effondrement local | Singularité à retardement : aspire puis broie tout dans un rayon | instabilité élevée, frappe les alliés |
| Onde de désynchronisation | Les cibles touchées reçoivent leurs ordres en retard et leur `timeScale` oscille | instabilité moyenne |

---

## 6. IA

**Utility AI à deux étages**, mêmes règles et même information que le joueur.

**Commandant** (toutes les 2 s) : évalue le rapport de forces, l'état de l'objectif et le temps restant, puis choisit une **stance** :

- côté attaquant : *assaut du dépôt*, *engager la flotte*, *regrouper* ;
- côté défenseur : *défendre le dépôt*, *intercepter*, *harceler*.

Il distribue ensuite des intentions par rôle :

- **Capital** : choisit la cible la plus menaçante ou la plus rentable.
- **Porteurs** : drones en attaque ou en écran.
- **Escortes** : protègent porteurs et capital.
- **Brawlers** : flanquent par la face de bouclier la plus faible de la cible.

**Capitaine** (toutes les 0,5 s par vaisseau) : choisit la posture (Assaut en portée, Défense sous le feu, Fuite en transit ou en repli), le sous-système à viser, et décide la retraite des coques mourantes (préserver les coques : esprit Nexus).

**Profils** : prudent / équilibré / casse-cou. Ils pondèrent les considérations ; en v1, ils fixent aussi le seuil d'instabilité que l'IA accepte.

---

## 7. Architecture

```text
fleet-proto/
  src/sim/      simulation pure TS — aucune dépendance au rendu (headless, testable)
  src/ai/       IA utility (commandant + capitaines), consomme l'état de la sim
  src/render/   three.js : scène, coques, effets, caméra
  src/ui/       HUD HTML/CSS, entrées, i18n FR/EN
  src/headless.ts  runner IA vs IA (npm run sim)
  test/         déterminisme, boucliers, file d'ordres, historique
```

- **Pas fixe** de 20 Hz, rendu interpolé.
- **Déterminisme** : RNG seedé, itération par id, aucun `Math.random` dans la sim.
- **Temps local par entité** : chaque entité intègre `dt × timeScale`. Toujours égal à 1 en v0, c'est la fondation de l'Horloge.
- **Historique d'états** : instantané par vaisseau toutes les 0,5 s sur 10 s, pour la fenêtre rétroactive.
- **Ordres** : structures de données sérialisables. Le joueur et l'IA passent par la même API (`world.issue(...)`), ce qui préparera le replay et le réseau.
- **Modèles** : registre classe → `public/models/<classe>.glb`, avec repli automatique sur la coque procédurale.

---

## 8. Direction artistique (proto)

- **Fond** : ciel peint procédural (nébuleuse bleu/violet, champ d'étoiles) et **Wolf 359** en naine rouge à surface animée, avec halo et bloom.
- **Coques** : shading **toon** à 2–3 aplats, **contour noir net** par coque inversée, couleur de faction (Coalition bleu profond / argent, Clans rouge sang / noir).
- **Silhouettes** (2026-10-14) : space opera lisible. Coalition en coins nets et symétriques, pont surélevé, flancs à tourelles, rangées de hublots (Bastion en coin à tour de commandement, Égide porte-drones à sponsons et baies latérales). Clans en roche et carcasses soudées, asymétriques : Mastodonte = bloc de minerai cerclé d'échafaudages avec éperon, plaques soudées, balancines à batteries et moteurs dépareillés ; Ruche = ruche organique à alvéoles et anneau de baies ; dépôt = astéroïde-station (anneau d'amarrage, réservoirs, portique, cristaux d'Orionum).
- **Échelle** : capitaux ×3, porte-drones ×2, frégates inchangées ; une frégate est un moucheron à côté d'un capital. Les hublots et feux de position donnent l'échelle. Les tourelles visent leur cible et les tirs partent des bouches ; les drones sortent des baies.
- **Lisibilité 3D** : grille tactique discrète et **tiges verticales** reliant chaque vaisseau au plan.
- **Effets** : tirs émissifs stylisés, explosions en aplats, éclats sur les faces de bouclier.
- **Orionum (v1)** : violet électrique émissif, exclusivement.

---

## 9. Désigner le pilier héros (fin de v1)

Pour chaque pilier, sur des sessions de playtest et en IA vs IA :

1. **Taux d'usage spontané** : le joueur s'en sert-il sans y être invité ?
2. **Moments racontés** : nombre d'anecdotes citées après la partie qui impliquent le pilier.
3. **Lisibilité** : le joueur comprend-il ce qui s'est passé ? (question fermée posée après la partie)
4. **Sublime** : note de 1 à 5, « j'ai eu envie de regarder ».
5. **Profondeur** : l'écart de win-rate entre une IA qui sait l'utiliser et une IA qui l'ignore. Un écart nul signifie un pilier décoratif.

Le pilier en tête devient le **pitch**. Les autres restent s'ils le servent, sinon on les coupe.

---

## 10. Canon : points de vigilance

- Les **noms de classes et de coques** sont du lore libre (zone non figée du canon). Les commandants Voss et Reva sont des figures enregistrées (CANON § 7) et ne doivent pas être contredits.
- **Drones** = cores IA bridés : aucune autonomie stratégique, leash sur leur porteur (§ 9.2).
- **Pas de FTL** : les renforts arrivent par **saut gravitationnel** Orionum (§ 9.3).
- Ce produit aura besoin de **son propre `canon-divergences.md`** dès qu'il quittera ce dossier.
- Strings joueur **bilingues FR + EN** (règle studio).

---

## 11. Backlog (grills du 2026-10-15), dans l'ordre

1. **Flottes plus grandes, nouvelles classes asymétriques** — *livré (lot 1, voir § 11 bis), sauf les variantes d'équipement, qui passent à l'écran à crédits*
   - **Taille** : variable selon les crédits (escarmouche ~8 coques, bataille ~16, grande ~30).
   - **Coalition** (ordre) : artillerie longue portée, croiseur de ligne, remorqueur de réparation.
   - **Clans** (chaos) : poseur de mines, destroyer rapide, brouilleur.
   - **Orionum** : sur les capitaux seulement.
   - **Équipement** : 2-3 variantes par classe, plus des options à cocher.
   - **Commandement** : groupes automatiques par rôle, sélection par classe (double-clic), icônes stratégiques au dézoom, optimisations (grille spatiale, analyse allégée, LOD).
   - **Objectifs** : raid, annihilation, escorte de convoi, tenir une zone.
2. **Écran de paramétrage à crédits** — *livré (lot 2, voir § 11 ter)*
   - **Budget** : un par camp, avec un curseur d'asymétrie ; l'IA dépense le sien selon son profil.
   - **Achats** : vaisseaux, équipement, Orionum (niveau du module, piliers, instabilité de départ), carte et règles.
   - **Préréglages** : tailles de bataille, doctrines (équilibré, artillerie, essaim, blindage, Orionum lourd), aléatoire équilibré, rejouer la dernière partie.
   - **Réserve** : une part du budget peut être gardée pour les renforts.
3. **Renforts par saut gravitationnel et saut d'urgence** — *livré (lot 3, voir § 11 quater).* Le canon est gardé (pas de FTL), avec une mise en scène « sortie d'hyperespace » : espace plié en violet, lentille, jaillissement, onde de choc, freinage en traînée.
   - **Paiement** : réserve de crédits plus DT gagnées par ses propres pertes.
   - **Arrivée** : point choisi dans sa moitié de carte, lentille visible 8 à 10 s avant ; les puits dévient l'arrivée, ou l'interdisent près d'un cœur.
   - **Retraite** : en plus du retour au point de départ, un saut d'urgence sur place après 6 à 10 s de charge, vulnérable et interrompu si les moteurs tombent. Un vaisseau sauvé rend une part de sa valeur en DT.
4. **Tutoriel incrémental**
   - **Forme** : 5 à 7 mini-batailles scriptées (1 à 3 min), une notion chacune, objectif clair, déblocage progressif.
   - **Tuteur** : l'officier tactique, avec bulles courtes, flèches sur l'interface et pause automatique.
   - **Contenu** : commandement, flotte et analyse, armes spéciales, Orionum.
   - **En bataille** : un codex consultable depuis la pause.
5. **Pilier Arme** : une arme par capital.
   - lance gravitationnelle pour le Bastion ;
   - effondrement local pour le Mastodonte ;
   - onde de désynchronisation, en version moindre, pour les deux.

   Ensuite, désigner le pilier héros (§ 9).

## 11 bis. Lot 1 livré : grandes flottes (grill et mesures du 2026-10-16)

**Décisions du grill**
- Les classes d'abord, l'écran à crédits ensuite (lot 2). Flotte de base ~16 coques par camp (préréglage « Bataille », par défaut), escarmouche ~6, grande ~30.
- Un module Orionum par capital, deux capitaux par camp.
- **Coalition** :
  - Rempart, croiseur de ligne solide ;
  - Trébuchet, artillerie à très longue portée, aux obus lents, qui a besoin d'un observateur et excelle en tir courbe ;
  - Hospitalier, remorqueur qui répare en zone et remorque.
- **Clans** :
  - Éventreur, destroyer d'assaut rapide à torpilles ;
  - Semeur, poseur de mines : champs semés en vol qui dérivent, camouflés, qui explosent au contact de toute coque (alliés compris), sont repérés et abattus par la DCA proche et sont attirés par les puits ;
  - Murmure, brouilleur qui dégrade le guidage et raccourcit la portée de détection.
- Objectifs ajoutés : annihilation, escorte de convoi, tenir une zone. Commandement : groupes automatiques par rôle, sélection par classe (double-clic), symboles stratégiques au dézoom, optimisations.

**Ce qui a été fixé en mesurant**
- **Mines** :
  - Le Semeur ne posait rien : la règle « tir masqué par un puits » éteignait toutes ses cibles, à 18 u/s de vitesse de mine. Les mines en sont exemptées.
  - Elles tuaient surtout des alliés : pas de pose si un allié est à moins de 120 u du point de largage, les coques évitent leurs propres mines, et le Semeur a son propre groupe (le groupe Soutien réécrivait ses ordres).
  - Mines retenues : portée 3 000, vie 300 s, armement 3 s, fusée 90 u, champ de 18 mines au plus. Résultat : ~38 mines posées et ~6 touches par bataille.
- **IA** : les Clans gagnaient tout tant que le commandant ignorait les groupes 4 à 6 ; il les commande maintenant :
  - l'artillerie tire sur la cible prioritaire à portée maximale ;
  - le soutien se tient en retrait ;
  - le Semeur sème 1 000 u devant le point gardé ;
  - l'Hospitalier remorque de lui-même les alliés aux moteurs coupés.
- **Trébuchet** : 9 000 de dégâts par bataille au premier jet, ramenés à ~5 700 (deux Trébuchets, Raid).
- **Raid** : la Tanière, calibrée pour l'escarmouche, fondait (88/13).
  - Sa coque suit désormais la coque totale de la flotte qui attaque : exposant 0,5 jusqu'à la Bataille, 1,3 au-delà, car la puissance de feu grandit plus vite que la racine carrée (Grande à 0,5 : 100/0).
  - Le déploiement en rangs garde les emplacements classiques quand un rôle tient dedans, pour que l'escarmouche ne change pas.
- **Objectifs** : chaque objectif se cale par des coques en plus pour un camp (`extra`), avec une variante par taille (`extraBySize`). Ces leviers sont **très sensibles** : une coque déplace 20 à 40 points, par exemple un Éventreur de plus en Annihilation fait passer la Coalition de 80 % à 38 %. Les cargos du convoi grandissent avec la meute au-delà de la Bataille (exposant 1,7).
- **Performance** : une Grande bataille prend ~0,8 ms par pas en headless. Le profil est plat, donc pas de grille spatiale ; l'analyse est limitée à 6 ms par image et à 2 candidats au-delà de 40 coques ; LOD des tourelles et feux au-delà de 90 rayons.

**Mesures** (victoires de la Coalition, 40 batailles par case ; Grande : 16, ±12) :

| Objectif | Escarmouche | Bataille | Grande |
|---|---|---|---|
| Raid | 55 % | 50 % | 56 % |
| Annihilation | — | 53 % et 48 % | 56 % |
| Convoi | — | 48 % et 45 % | 38 % |
| Secteur | — | 45 % | 50 % |

**Leçons**
- À ~16 coques, l'équilibre n'est plus une affaire de stats de classe mais de **composition et de placement** : il faut des budgets (lot 2) plutôt que des coques en plus codées en dur.
- Une mécanique d'IA non commandée (groupes 4 à 6) pèse plus que n'importe quel réglage de dégâts.
- Le remorquage n'arrive presque jamais en IA contre IA (0 coque ramenée) : les coques désemparées meurent avant. À reconsidérer avec les renforts et le saut d'urgence (lot 3).

## 11 ter. Lot 2 livré : l'écran à crédits (grill et mesures du 2026-10-16)

**Décisions du grill**
- **Prix** mesurés en simulation, pour qu'à budget égal on soit près de 50/50.
- **Écran** à deux colonnes face à face (Coalition, Clans), avec une barre « préréglages · carte · règles » au-dessus.
- **Variantes**, 2 ou 3 par classe, choisies pour avoir un sens (armement, défense, propulsion), et des **options** à cocher (capteurs, DCA+, blindage, soute à mines pour les Clans).
- **Orionum** : niveau du module, capacités à la carte, modules réduits sur d'autres coques, instabilité de départ.
- **Budget** : fixé par la taille, plus un curseur d'asymétrie (±50 %).
- **Règles et carte** gratuites et communes ; quelques **avantages** payants par camp (déploiement avancé, puits supplémentaire, tourelles de la Tanière).
- **Préréglages** : doctrines, aléatoire équilibré, rejouer la dernière, mes flottes sauvées.
- **IA** : elle achète selon la doctrine de son profil, sans voir votre flotte.
- **Réserve** pour renforts : le curseur est présent, mais inactif jusqu'au lot 3.
- **Menu** : « Bataille rapide » et « Paramétrer… ».

**Architecture**
- `src/sim/loadout.ts` porte le catalogue (variantes, options, Orionum, avantages, prix), les budgets et les achats de l'IA.
- Une coque achetée devient une **classe dérivée** (`deriveClass`), sous une clé (`specKey`). Le World garde ces classes, partagées avec les projections, et chaque vaisseau garde sa clé. Le modèle, le nom et le rôle restent ceux de la classe de base.

**Ce qu'ont appris les mesures**
- **Valeur d'une classe** :
  - Ajouter ~300 crédits d'une classe à une flotte juste sature (+1 Bastion : 100 %).
  - On oppose donc 300 crédits d'une classe à 300 crédits du mélange adverse : à prix juste, le résultat reste près de 50 %.
  - Les corrections sont amorties, puis chaque faction est recalée pour que sa flotte juste (préréglage + coques de l'objectif) coûte son budget.
- **Budgets d'objectif** : l'équilibre du lot 1 reposait sur des coques en plus. Ces coques deviennent un ajustement en crédits, qui peut être négatif : au Raid, les Clans ont la Tanière, donc ~480 crédits de moins.
- **Variantes** : deux sont retirées, désastreuses même gratuites :
  - le Grand nid de la Ruche (97 % pour la Coalition) ;
  - le Lance-missiles de l'Écorcheur (88 %).

  D'autres ont été renchéries (Casemate du Trébuchet, Obus rapides, Canonnière) ou bradées (DCA lourde, Turbolasers du Rempart, Mines lourdes, Sprint de l'Éventreur).
- **Orionum des Clans** : un module stable ou maîtrisé les faisait perdre (69 % et 78 % pour la Coalition), même après avoir interdit la fronde à leur IA. Les Clans gardent donc l'Orionum brut : canon et mesure s'accordent.
- **Modules réduits** : bon marché à mesurer (~30 à 40 crédits de valeur) ; fixés à 60.
- **Doctrines** :
  - Construites à partir de rien, elles s'écartaient de 30 à 50 points de l'équilibre.
  - Elles partent maintenant de la flotte juste et n'en échangent que 15 % (l'aléatoire, 25 %).
  - Écart restant : jusqu'à ±25 points, contre ±9 de bruit.

## 11 quater. Lot 3 livré : renforts et saut d'urgence (grill et mesures du 2026-10-16)

**Décisions du grill**
- **DT** : réserve de crédits, plus les pertes (45 % de la valeur), plus l'objectif.
- **Renforts** : toute classe, une lentille à la fois par camp, puis 20 s de recharge.
- **Sortie** :
  - arrivée vulnérable (boucliers à zéro, demi-vitesse 5 s) ;
  - onde de choc (poussée, armes sonnées 1 s, alliés compris) ;
  - lentille visible des deux camps, contrable par la stase et la bulle Orionum.
- **Saut d'urgence** : la coque sauvée rend la moitié de sa valeur en DT et peut revenir (moitié prix, réparée à moitié). Les coques rentrées par la retraite en profitent aussi.
- **Zone permise** : sa moitié de carte, à plus de 600 u de l'ennemi, loin des cœurs. Mesure ajoutée : à plus de 700 u du secteur disputé.
- **IA** : renfort là où ça plie, embuscade de sortie, saut d'urgence des coques condamnées, conseils de l'officier testés en projection.
- **Mise en scène** : coupe cinématique sur les arrivées, sons dédiés (synthèse), alerte de l'officier.

**Architecture**
- `src/sim/reinforce.ts` : DT, lentilles, arrivée, saut d'urgence, revenus d'objectif ; état cloné avec le monde, donc projeté par l'analyse.
- `src/ai/reinforceAi.ts` : l'IA (choix de la coque et de la sortie, embuscade, saut d'urgence).
- Rendu : `src/render/jump.ts` ; interface : `src/ui/reinforce.ts`.
- Une coque connaît sa spécification d'achat et sa valeur (`Ship.spec`, `Ship.value`) : c'est la base des DT et des rappels.
- Les DT entrent dans l'évaluation des projections (0,012 par DT) : l'officier ne propose un renfort que s'il vaut plus que les DT qu'il coûte.

**Ce qu'ont appris les mesures**
- **Poids du pilier** : c'est le plus lourd mesuré jusqu'ici. Une IA qui ne s'en sert pas perd presque tout (Raid : 8 % et 22 %).
- **Diagnostic par interrupteur** (`RF_OFF`) :
  - Au Secteur, l'appel seul faisait 6/94 : les Clans renforçaient au bord de la zone. D'où la distance au secteur.
  - Au Raid, l'embuscade seule faisait gagner la Coalition. D'où les sorties couvertes par les gros canons.
- **Équilibrage par objectif** : une dotation de DT au départ (`dtStart` dans `data.ts`), comme les coques en plus du lot 1. Elle reste très sensible (Raid : ±25 points pour 100 DT).
- **Annihilation** : les renforts et le rattrapage empêchent tout anéantissement, et les batailles finissent aux points. À trancher en playtest.

## 12. Questions ouvertes

- Les drones gagnent-ils trop ? (à surveiller en IA vs IA : part des dégâts faits par les drones)
- La 3D libre est-elle lisible sans fog of war ni capteurs ? (v0 : vision totale des deux côtés)
- La fenêtre rétroactive et la pause tactique : rembobiner pendant une pause est-il trop puissant ? (v1 : autorisé, exécuté à la reprise ; à juger en playtest)
- Faut-il une couche capteurs / brouillage plus tard ? (Nexus en avait une)
