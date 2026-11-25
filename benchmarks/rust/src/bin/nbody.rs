use std::env;
use std::path::Path;

struct Body {
    x: f64,
    y: f64,
    z: f64,
    vx: f64,
    vy: f64,
    vz: f64,
    mass: f64,
}

fn my_sqrt(x: f64) -> f64 {
    let mut g = x * 0.5 + 1.0;
    for _ in 0..16 {
        g = 0.5 * (g + x / g);
    }
    g
}

fn round_int(v: f64) -> i64 {
    if v >= 0.0 {
        (v + 0.5) as i64
    } else {
        (v - 0.5) as i64
    }
}

fn advance(bodies: &mut [Body], dt: f64) {
    let n = bodies.len();
    for i in 0..n {
        for j in (i + 1)..n {
            let dx = bodies[i].x - bodies[j].x;
            let dy = bodies[i].y - bodies[j].y;
            let dz = bodies[i].z - bodies[j].z;
            let dsq = dx * dx + dy * dy + dz * dz;
            let dist = my_sqrt(dsq);
            let mag = dt / (dsq * dist);

            let mi = bodies[i].mass;
            let mj = bodies[j].mass;

            bodies[i].vx -= dx * mj * mag;
            bodies[i].vy -= dy * mj * mag;
            bodies[i].vz -= dz * mj * mag;

            bodies[j].vx += dx * mi * mag;
            bodies[j].vy += dy * mi * mag;
            bodies[j].vz += dz * mi * mag;
        }
    }
    for body in bodies.iter_mut() {
        body.x += dt * body.vx;
        body.y += dt * body.vy;
        body.z += dt * body.vz;
    }
}

fn energy(bodies: &[Body]) -> f64 {
    let mut e = 0.0;
    let n = bodies.len();
    for i in 0..n {
        let bi = &bodies[i];
        e += 0.5 * bi.mass * (bi.vx * bi.vx + bi.vy * bi.vy + bi.vz * bi.vz);
        for j in (i + 1)..n {
            let bj = &bodies[j];
            let dx = bi.x - bj.x;
            let dy = bi.y - bj.y;
            let dz = bi.z - bj.z;
            let dist = my_sqrt(dx * dx + dy * dy + dz * dz);
            e -= (bi.mass * bj.mass) / dist;
        }
    }
    e
}

fn main() {
    let quick = env::args().skip(1).any(|a| a == "--quick")
        || Path::new("benchmarks/data/quick").exists();
    let steps = if quick { 3000 } else { 50000 };

    let pi = 3.141592653589793_f64;
    let solar_mass = 4.0 * pi * pi;
    let days_per_year = 365.24_f64;

    let mut bodies = vec![
        Body { x: 0.0, y: 0.0, z: 0.0, vx: 0.0, vy: 0.0, vz: 0.0, mass: solar_mass },
        Body {
            x: 4.84143144246472090, y: -1.16032004402742839, z: -0.103622044471123109,
            vx: 0.00166007664274403694 * days_per_year, vy: 0.00769901118419740425 * days_per_year, vz: -0.0000690460016972063023 * days_per_year,
            mass: 0.000954791938424326609 * solar_mass,
        },
        Body {
            x: 8.34336671824457987, y: 4.12479856412430479, z: -0.403523417114321381,
            vx: -0.00276742510726862411 * days_per_year, vy: 0.00499852801234917238 * days_per_year, vz: 0.0000230417297573763929 * days_per_year,
            mass: 0.000285885980666130812 * solar_mass,
        },
        Body {
            x: 12.8943695621391310, y: -15.1111514016986312, z: -0.223307578892655734,
            vx: 0.00296460137564761618 * days_per_year, vy: 0.00237847173959480950 * days_per_year, vz: -0.0000296589568540237556 * days_per_year,
            mass: 0.0000436624404335156298 * solar_mass,
        },
        Body {
            x: 15.3796971148509165, y: -25.9193146099879641, z: 0.179258772950371181,
            vx: 0.00268067772490389322 * days_per_year, vy: 0.00162824170038242295 * days_per_year, vz: -0.0000951592254519715870 * days_per_year,
            mass: 0.0000515138902046611451 * solar_mass,
        },
    ];

    let (mut px, mut py, mut pz) = (0.0, 0.0, 0.0);
    for b in &bodies {
        px += b.vx * b.mass;
        py += b.vy * b.mass;
        pz += b.vz * b.mass;
    }
    bodies[0].vx = -px / solar_mass;
    bodies[0].vy = -py / solar_mass;
    bodies[0].vz = -pz / solar_mass;

    let e0 = energy(&bodies);
    for _ in 0..steps {
        advance(&mut bodies, 0.01);
    }
    let e1 = energy(&bodies);

    println!("{}", round_int(e0 * 1000000000.0));
    println!("{}", round_int(e1 * 1000000000.0));
}
