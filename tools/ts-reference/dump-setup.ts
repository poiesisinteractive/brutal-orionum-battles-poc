// Référence S1 : état du monde TS juste après sa construction (coques, puits, RNG).
// À lancer depuis une copie de fleet-proto au commit 3eae78c :
//   npx tsx dump-setup.ts <graine> <scénario> <terrain on|off> > setup.json
import { World } from './src/sim/world';

const seed = Number(process.argv[2] ?? 42);
const scenario = (process.argv[3] ?? 'wolf359-raid') as 'wolf359-raid' | 'well-field';
const terrain = (process.argv[4] ?? 'on') === 'on';
const w = new World(seed, { scenario, fleetSize: 'skirmish', pillars: { terrain } });

console.log(JSON.stringify({
  seed, scenario, terrain,
  ships: w.ships.map((s) => ({
    id: s.id, classId: s.classId, name: s.name, side: s.side,
    pos: [s.pos.x, s.pos.y, s.pos.z], fwd: [s.fwd.x, s.fwd.y, s.fwd.z],
    hull: s.hull, face: s.faces[0], subsystemHp: s.subsystems.engines.hp,
    cooldowns: s.weapons.map((x) => x.cooldown),
    empAmmo: s.empAmmo, hangarStock: s.hangar?.stock ?? -1, orionum: !!s.orionum,
  })),
  wells: w.wells.map((g) => ({ id: g.id, name: g.name, pos: [g.pos.x, g.pos.y, g.pos.z], core: g.core, pull: g.pull, reach: g.reach })),
  depotId: w.depotId,
  rngNext: w.rng.next(),
}, null, 1));
