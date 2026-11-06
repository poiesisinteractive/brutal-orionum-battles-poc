// Référence S2 : trajectoires de manœuvres scriptées, sans tir (le combat n'est pas encore porté).
// Positions et nez échantillonnés chaque seconde. À lancer depuis une copie de fleet-proto au commit 3eae78c :
//   npx tsx dump-flight.ts > flight.json
import { commandGroup, startManeuver } from './src/sim/fleet';
import { World } from './src/sim/world';
import type { Ship } from './src/sim/types';

const skirmish = () => new World(1, { fleetSize: 'skirmish' });
const isolate = (w: World, keep: Ship[]) => {
  for (const o of w.ships) if (!keep.includes(o) && o.classId !== 'depot') o.alive = false;
};
const record = (w: World, ships: Ship[], seconds: number) => {
  const out: Record<number, number[][]> = {};
  for (const s of ships) out[s.id] = [];
  for (let i = 1; i <= seconds * 20; i++) {
    w.step();
    if (i % 20 === 0) for (const s of ships) out[s.id].push([s.pos.x, s.pos.y, s.pos.z, s.fwd.x, s.fwd.y, s.fwd.z]);
  }
  return out;
};

const scenarios: Record<string, unknown> = {};

{
  // Un capital va à 1500 u sur le côté : rotation, poussée, roue libre, retournement, freinage.
  const w = skirmish();
  const s = w.ships.find((x) => x.classId === 'bastion')!;
  isolate(w, [s]);
  w.issue(s.id, { type: 'move', point: { x: s.pos.x + 1500, y: s.pos.y, z: s.pos.z } });
  scenarios['capital-move'] = record(w, [s], 110);
}
{
  // Une frégate en repli combatif face à un raider désarmé.
  const w = skirmish();
  const s = w.ships.find((x) => x.classId === 'hoplite')!;
  const t = w.ships.find((x) => x.classId === 'ecorcheur')!;
  isolate(w, [s, t]);
  t.pos = { x: s.pos.x, y: s.pos.y, z: s.pos.z + 150 };
  t.weapons = [];
  s.weapons = [];
  w.issue(s.id, { type: 'kite', targetId: t.id });
  scenarios['frigate-kite'] = record(w, [s, t], 12);
}
{
  // Les Lanciers en ligne vers un point : postes de formation (commandement de flotte).
  const w = skirmish();
  const f = w.fleets.coalition;
  commandGroup(f, f.groups[1], { formation: 'line', movePoint: { x: 0, y: 40, z: -1500 } });
  scenarios['lancers-line'] = record(w, f.groups[1].members.map((id) => w.shipById(id)!), 40);
}
{
  // Une escorte en orbite autour d'un capital allié, puis une passe balistique d'une frégate sur la Tanière désarmée.
  const w = skirmish();
  const vigile = w.ships.find((x) => x.classId === 'vigile')!;
  const bastion = w.ships.find((x) => x.classId === 'bastion')!;
  const hop = w.ships.find((x) => x.classId === 'hoplite')!;
  const depot = w.ships.find((x) => x.classId === 'depot')!;
  isolate(w, [vigile, bastion, hop]);
  depot.weapons = [];
  for (const s of [vigile, bastion, hop]) s.weapons = [];
  w.issue(vigile.id, { type: 'escort', targetId: bastion.id });
  w.issue(bastion.id, { type: 'move', point: { x: 300, y: 40, z: -1400 } });
  w.issue(hop.id, { type: 'pass', targetId: depot.id });
  scenarios['escort-and-pass'] = record(w, [vigile, bastion, hop], 60);
}
{
  // Tenailles : deux groupes Coalition sur le Mastodonte (pas de tir, seulement les placements).
  const w = skirmish();
  for (const s of w.ships) s.weapons = [];
  for (const s of w.ships) if (s.hangar) s.hangar.stock = 0;
  const mas = w.ships.find((x) => x.classId === 'mastodonte')!;
  startManeuver(w, 'coalition', { type: 'pincer', targetId: mas.id, groups: [1, 2] });
  const ships = w.ships.filter((s) => s.side === 'coalition');
  scenarios['pincer'] = record(w, ships, 60);
}

console.log(JSON.stringify(scenarios));
