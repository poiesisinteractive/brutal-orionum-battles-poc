// Référence S3/S4 : batailles scriptées sans IA, pour comparer le combat en statistiques. Trois scénarios :
//  - raid        : Raid sur Wolf 359, hangars vides, les deux flottes engagées par le commandement de flotte
//                  (tous les groupes, ciblage automatique), 180 s ;
//  - wells       : Champ de puits, hangars vides, la Coalition en tir courbe attaque la Tanière (masquée par
//                  un puits depuis le sud), les Clans défendent en ciblage automatique, 240 s. Il éprouve la
//                  balistique du Terrain ;
//  - raid-drones : le raid, hangars pleins (S4) : lancements, passes d'attaque, interception.
// À lancer depuis une copie de fleet-proto au commit 3eae78c :
//   npx tsx dump-combat.ts <graines> <raid|wells|raid-drones> > combat-<scénario>.json
import { commandGroup } from './src/sim/fleet';
import { isActive } from './src/sim/combat';
import { World } from './src/sim/world';
import { SIDES } from './src/sim/types';

const seeds = Number(process.argv[2] ?? 40);
const scenario = process.argv[3] ?? 'raid';
const KINDS = ['kinetic', 'missile', 'laser', 'bomb'];
const battles = [];

for (let seed = 1; seed <= seeds; seed++) {
  const w = new World(seed, { fleetSize: 'skirmish', scenario: scenario === 'wells' ? 'well-field' : 'wolf359-raid' });
  if (scenario !== 'raid-drones') for (const s of w.ships) if (s.hangar) s.hangar.stock = 0;
  if (scenario === 'wells') {
    for (const s of w.ships) if (s.side === 'coalition') w.setArcFire(s.id, true);
    for (const g of w.fleets.coalition.groups) commandGroup(w.fleets.coalition, g, { targetId: w.depotId });
    for (const g of w.fleets.clans.groups) commandGroup(w.fleets.clans, g, { autoTarget: true });
    w.run(240);
  } else {
    for (const side of SIDES) for (const g of w.fleets[side].groups) commandGroup(w.fleets[side], g, { autoTarget: true });
    w.run(180);
  }

  const per = (side: 'coalition' | 'clans', f: (s: (typeof w.ships)[number]) => number) =>
    w.ships.filter((s) => s.side === side && s.classId !== 'depot').reduce((a, s) => a + f(s), 0);
  const byKind = (table: Record<string, number>) =>
    Object.fromEntries(KINDS.map((k) => [k, Object.entries(table).filter(([key]) => key.endsWith(`.${k}`)).reduce((a, [, v]) => a + v, 0)]));
  const depot = w.shipById(w.depotId)!;

  battles.push({
    seed,
    time: w.time,
    winner: w.result?.winner ?? null,
    damage: { coalition: per('coalition', (s) => s.stats.damageDealt), clans: per('clans', (s) => s.stats.damageDealt) },
    lost: { coalition: per('coalition', (s) => (isActive(s) ? 0 : 1)), clans: per('clans', (s) => (isActive(s) ? 0 : 1)) },
    hullLeft: { coalition: per('coalition', (s) => (s.alive ? s.hull : 0)), clans: per('clans', (s) => (s.alive ? s.hull : 0)) },
    depotHull: depot.alive ? depot.hull : 0,
    shots: byKind(w.stats.shots),
    hits: byKind(w.stats.hits),
    bombsFired: w.stats.bombsFired,
    bombsDetonated: w.stats.bombsDetonated,
    bombsIntercepted: w.stats.bombsIntercepted,
    blockedShots: w.stats.blockedShots,
    arcShots: w.stats.arcShots.coalition + w.stats.arcShots.clans,
    swallowed: w.stats.swallowed,
    dronesLaunched: w.stats.dronesLaunched,
    droneShipHits: w.stats.droneShipHits,
    droneDroneShots: w.stats.droneDroneShots,
    droneSubsystemKills: w.stats.droneSubsystemKills,
    dronesLostToPd: w.stats.dronesLostToPd,
    dronesAlive: w.drones.filter((d) => d.alive).length,
  });
}

console.log(JSON.stringify(battles));
