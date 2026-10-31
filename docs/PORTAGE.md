# Plan de portage de la sim

La sim du proto TS fait environ 5 500 lignes (`src/sim`, `src/ai`, `src/analysis`) et ne dépend pas du rendu. On la porte en C# sur BRUTAL, avec une contrainte qui change tout : viser **1 000+ coques et 10 000+ drones**. On transcrit donc la **logique** fidèlement, mais on réécrit le **stockage** pour la performance.

## Architecture

### Ce qu'on garde du TS
- **Les phases de `World.step`, dans le même ordre** : flotte, Orionum, terrain, ordres et vol des coques, défenses, soutien, tirs, hangars, drones, projectiles, agonies, élagage, historique, victoire.
- **Le pas fixe de 20 Hz** et le temps local par entité (`dt × timeScale`).
- **Les noms de fonctions.** Chaque fonction C# indique en commentaire son équivalent TS (`combat.ts:fireWeapons`) pour que les deux codes restent comparables.
- **Le RNG global mulberry32, bit à bit** (fait, voir `Rng.cs`).

### Ce qu'on change
1. **Des tables contiguës, pas un objet par entité.**
   - **Coques, drones et projectiles** : un struct blittable par entité, dans une table contiguë (`EntityTable<T>`), manipulé par `ref`. Les parties de taille variable du TS (faces, sous-systèmes, armes, file d'ordres) sont des tableaux à capacité fixe (`InlineArray`) dans le struct. Le code porté se lit donc presque comme le TS (`s.Faces[f]`, `s.Orders`).
   - **Écart avec le plan initial** : on avait prévu de séparer les champs chauds et froids des coques en tableaux parallèles (SoA). On ne le fera que si le profilage de S7 le demande. À 1 000 coques, la table entière ne pèse que 1,5 Mo ; les drones et projectiles, bien plus nombreux, sont déjà des structs compacts.
   - **Tableaux .NET plutôt que `NativeList` de BRUTAL** : avec `NativeList`, il faudrait libérer à la main chaque clone de l'analyse tactique, et un oubli fuit de la mémoire non managée. Des tableaux de structs blittables se copient aussi vite. À réévaluer avec les mesures de S7.
   - **Données statiques** (classes, armes, drones, scénario) : tables immuables référencées par index. Plus aucune chaîne de caractères dans la boucle.
2. **Identité et ordre.** Les ids sont des entiers croissants ; 0 veut dire « aucun » partout où le TS a `null`. L'élagage compacte les tables en gardant l'ordre de création, comme le `prune` du TS, puis reconstruit la table id → slot (`IdMap`, adressage ouvert, que des tableaux d'entiers). L'itération reste donc dans l'ordre des ids, et le résultat est déterministe.
3. **Grille spatiale.** Un hachage uniforme, reconstruit à chaque tick, répond aux requêtes de voisinage (ciblage, DCA, souffles, mines, évitement) à la place des boucles en O(n²) du TS.
4. **Deux sortes de phases.**
   - **Séquentielles** : ordres, commandement de flotte, IA commandant, application des dégâts, apparitions.
   - **Parallèles** : intégration des projectiles et des drones, vol des coques, choix des cibles. Chaque job lit l'état du début de la phase et n'écrit que dans ses propres slots. Les effets croisés (dégâts, tirs, événements) passent par des tampons par thread, appliqués ensuite **triés par (id source, séquence)**. Le résultat ne dépend donc pas du nombre de threads.
5. **L'aléatoire dans les phases parallèles** vient d'un RNG dérivé de (graine, tick, id d'entité). Il reste déterministe quel que soit l'ordonnancement. Le mulberry32 global reste réservé aux phases séquentielles.
6. **Le clone devient une copie de tampons.** Le monde n'étant fait que de tables blittables, `Clone()` copie quelques mégaoctets sans GC. Les projections à +60 s de l'analyse tactique peuvent alors tourner chacune sur son thread.
7. **Historique** (fenêtre rétroactive) : un anneau fixe de 20 instantanés par coque (10 s à 0,5 s).
8. **Événements** : un tampon de structs étiquetés, à la place des unions JS.
9. **Jobs** : le `JobScheduler` de BRUTAL, un lot par phase. On mesure son surcoût par phase. En dessous d'un seuil, la phase reste séquentielle, et ce seuil est en soi un résultat d'évaluation de BRUTAL.
10. **Plus tard, hors jalon** : des niveaux de détail de sim pour l'échelle 1 000+ (agrégats, IA de groupe, tirs simplifiés au loin). Le stockage ne doit pas les empêcher.

### Ce qui reste hors du jalon
Les lots 1 à 3 du proto (classes de soutien, objectifs, écran à crédits, renforts) ne sont pas portés. Là où la sim v0 les appelle (`support.ts`, `reinforce.ts`, `loadout.ts`), on garde des points d'accroche vides, pour les brancher plus tard sans toucher aux phases.

## Jalons

| # | Jalon | Sources TS | Critère de fin |
| --- | --- | --- | --- |
| S0 | **Socle** ✅ | `rng.ts` | Dépôt, solution, RNG bit à bit (10 tests verts). |
| S1 | **Monde et données** ✅ | `types.ts`, `data.ts`, `vec.ts`, `world.ts` (stockage, ids, événements, élagage, clone, historique) | Le monde construit est identique au TS (4 cas, RNG compris) ; clone, élagage, historique et grille spatiale testés (24 tests verts). |
| S2 | **Vol et ordres** | `movement.ts`, `fleet.ts`, ordres de `world.ts` | Tests : vol newtonien, ordres, commandement de flotte. |
| S3 | **Combat** | `combat.ts`, `projectiles.ts` | Tests : boucliers directionnels, règles de combat v0, lasers et bombes, destruction et bombe à énergie, distance de sécurité. |
| S4 | **Drones et hangars** | `drones.ts` | Tests des drones (sous les boucliers, porteur détruit). |
| S5 | **Orionum** | `orionum.ts`, `terrain.ts` | Tests : instabilité et Horloge, puits, tir courbe, fronde, élan, piliers. |
| S6 | **IA et analyse** | `ai/ai.ts`, `orionumAi.ts`, `terrainAi.ts`, `analysis/analysis.ts` | Tests : déterminisme, bataille IA contre IA, projection, limites de l'officier. |
| S7 | **Headless, parité, performance** | `headless.ts` | Mêmes statistiques que `npm run sim`, parité dans les tolérances, banc d'échelle. |

Le périmètre couvre environ 44 des 63 tests TS. Les autres concernent les lots 1 à 3.

## Validation

### Parité statistique
- **Référence TS**, au commit `3eae78c` :

  ```bash
  npm run sim -- --n 400 --fleet-size skirmish --scenario wolf359-raid --no-reinforce-ai-coalition --no-reinforce-ai-clans
  ```

  Horloge et Terrain sont actifs par défaut. Les renforts sont coupés, puisqu'ils viennent du lot 3.
- **Côté C#** : le même scénario et les mêmes métriques, avec les mêmes libellés que le runner TS.
- **Tolérances proposées**, à confirmer une fois le `float` et le RNG parallèle mesurés :

  | Métrique | Tolérance |
  | --- | --- |
  | Taux de victoire | ± 5 points |
  | Durée moyenne | ± 10 % |
  | Coques perdues | ± 10 % |
  | Part des dégâts des drones | ± 5 points |
  | Usages Orionum | ± 15 % |

### Performance
Un banc synthétique : 1 000 coques et 10 000 drones, sans rendu. On mesure le temps par tick et par phase, le surcoût du `JobScheduler`, et le temps d'un clone et d'une projection.
