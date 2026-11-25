import { existsSync } from "fs";

interface Body {
  x: number;
  y: number;
  z: number;
  vx: number;
  vy: number;
  vz: number;
  mass: number;
}

function mySqrt(x: number): number {
  let g = x * 0.5 + 1.0;
  for (let i = 0; i < 16; i++) {
    g = 0.5 * (g + x / g);
  }
  return g;
}

function roundInt(v: number): number {
  return v >= 0.0 ? Math.trunc(v + 0.5) : Math.trunc(v - 0.5);
}

function advance(bodies: Body[], dt: number): void {
  const n = bodies.length;
  for (let i = 0; i < n; i++) {
    for (let j = i + 1; j < n; j++) {
      const dx = bodies[i].x - bodies[j].x;
      const dy = bodies[i].y - bodies[j].y;
      const dz = bodies[i].z - bodies[j].z;
      const dsq = dx * dx + dy * dy + dz * dz;
      const dist = mySqrt(dsq);
      const mag = dt / (dsq * dist);

      const mi = bodies[i].mass;
      const mj = bodies[j].mass;

      bodies[i].vx -= dx * mj * mag;
      bodies[i].vy -= dy * mj * mag;
      bodies[i].vz -= dz * mj * mag;

      bodies[j].vx += dx * mi * mag;
      bodies[j].vy += dy * mi * mag;
      bodies[j].vz += dz * mi * mag;
    }
  }
  for (let k = 0; k < n; k++) {
    bodies[k].x += dt * bodies[k].vx;
    bodies[k].y += dt * bodies[k].vy;
    bodies[k].z += dt * bodies[k].vz;
  }
}

function energy(bodies: Body[]): number {
  let e = 0.0;
  const n = bodies.length;
  for (let i = 0; i < n; i++) {
    const bi = bodies[i];
    e += 0.5 * bi.mass * (bi.vx * bi.vx + bi.vy * bi.vy + bi.vz * bi.vz);
    for (let j = i + 1; j < n; j++) {
      const bj = bodies[j];
      const dx = bi.x - bj.x;
      const dy = bi.y - bj.y;
      const dz = bi.z - bj.z;
      const dist = mySqrt(dx * dx + dy * dy + dz * dz);
      e -= (bi.mass * bj.mass) / dist;
    }
  }
  return e;
}

const quick = process.argv.includes("--quick") || existsSync("benchmarks/data/quick");
const steps = quick ? 3000 : 15000;

const pi = 3.141592653589793;
const solarMass = 4.0 * pi * pi;
const daysPerYear = 365.24;

const bodies: Body[] = [
  { x: 0.0, y: 0.0, z: 0.0, vx: 0.0, vy: 0.0, vz: 0.0, mass: solarMass },
  {
    x: 4.84143144246472090, y: -1.16032004402742839, z: -0.103622044471123109,
    vx: 0.00166007664274403694 * daysPerYear, vy: 0.00769901118419740425 * daysPerYear, vz: -0.0000690460016972063023 * daysPerYear,
    mass: 0.000954791938424326609 * solarMass,
  },
  {
    x: 8.34336671824457987, y: 4.12479856412430479, z: -0.403523417114321381,
    vx: -0.00276742510726862411 * daysPerYear, vy: 0.00499852801234917238 * daysPerYear, vz: 0.0000230417297573763929 * daysPerYear,
    mass: 0.000285885980666130812 * solarMass,
  },
  {
    x: 12.8943695621391310, y: -15.1111514016986312, z: -0.223307578892655734,
    vx: 0.00296460137564761618 * daysPerYear, vy: 0.00237847173959480950 * daysPerYear, vz: -0.0000296589568540237556 * daysPerYear,
    mass: 0.0000436624404335156298 * solarMass,
  },
  {
    x: 15.3796971148509165, y: -25.9193146099879641, z: 0.179258772950371181,
    vx: 0.00268067772490389322 * daysPerYear, vy: 0.00162824170038242295 * daysPerYear, vz: -0.0000951592254519715870 * daysPerYear,
    mass: 0.0000515138902046611451 * solarMass,
  },
];

let px = 0.0, py = 0.0, pz = 0.0;
for (const b of bodies) {
  px += b.vx * b.mass;
  py += b.vy * b.mass;
  pz += b.vz * b.mass;
}
bodies[0].vx = -px / solarMass;
bodies[0].vy = -py / solarMass;
bodies[0].vz = -pz / solarMass;

const e0 = energy(bodies);
for (let i = 0; i < steps; i++) {
  advance(bodies, 0.01);
}
const e1 = energy(bodies);

console.log(roundInt(e0 * 1000000000.0));
console.log(roundInt(e1 * 1000000000.0));
