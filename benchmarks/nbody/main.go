package main

import (
	"fmt"
	"os"
	"time"
)

type Body struct {
	x, y, z    float64
	vx, vy, vz float64
	mass       float64
}

func mySqrt(x float64) float64 {
	g := x*0.5 + 1.0
	for i := 0; i < 16; i++ {
		g = 0.5 * (g + x/g)
	}
	return g
}

func roundInt(v float64) int64 {
	if v >= 0.0 {
		return int64(v + 0.5)
	}
	return int64(v - 0.5)
}

func advance(bodies []Body, dt float64) {
	n := len(bodies)
	for i := 0; i < n; i++ {
		for j := i + 1; j < n; j++ {
			dx := bodies[i].x - bodies[j].x
			dy := bodies[i].y - bodies[j].y
			dz := bodies[i].z - bodies[j].z
			dsq := dx*dx + dy*dy + dz*dz
			dist := mySqrt(dsq)
			mag := dt / (dsq * dist)

			mi := bodies[i].mass
			mj := bodies[j].mass

			bodies[i].vx -= dx * mj * mag
			bodies[i].vy -= dy * mj * mag
			bodies[i].vz -= dz * mj * mag

			bodies[j].vx += dx * mi * mag
			bodies[j].vy += dy * mi * mag
			bodies[j].vz += dz * mi * mag
		}
	}
	for k := 0; k < n; k++ {
		bodies[k].x += dt * bodies[k].vx
		bodies[k].y += dt * bodies[k].vy
		bodies[k].z += dt * bodies[k].vz
	}
}

func energy(bodies []Body) float64 {
	e := 0.0
	n := len(bodies)
	for i := 0; i < n; i++ {
		bi := bodies[i]
		e += 0.5 * bi.mass * (bi.vx*bi.vx + bi.vy*bi.vy + bi.vz*bi.vz)
		for j := i + 1; j < n; j++ {
			bj := bodies[j]
			dx := bi.x - bj.x
			dy := bi.y - bj.y
			dz := bi.z - bj.z
			dist := mySqrt(dx*dx + dy*dy + dz*dz)
			e -= (bi.mass * bj.mass) / dist
		}
	}
	return e
}

func isQuick() bool {
	if len(os.Args) > 1 && os.Args[1] == "--quick" {
		return true
	}
	_, err := os.Stat("benchmarks/data/quick")
	return err == nil
}

func main() {
	steps := 15000
	if isQuick() {
		steps = 3000
	}

	const pi = 3.141592653589793
	const solarMass = 4.0 * pi * pi
	const daysPerYear = 365.24

	bodies := []Body{
		{0.0, 0.0, 0.0, 0.0, 0.0, 0.0, solarMass},
		{
			4.84143144246472090, -1.16032004402742839, -0.103622044471123109,
			0.00166007664274403694 * daysPerYear, 0.00769901118419740425 * daysPerYear, -0.0000690460016972063023 * daysPerYear,
			0.000954791938424326609 * solarMass,
		},
		{
			8.34336671824457987, 4.12479856412430479, -0.403523417114321381,
			-0.00276742510726862411 * daysPerYear, 0.00499852801234917238 * daysPerYear, 0.0000230417297573763929 * daysPerYear,
			0.000285885980666130812 * solarMass,
		},
		{
			12.8943695621391310, -15.1111514016986312, -0.223307578892655734,
			0.00296460137564761618 * daysPerYear, 0.00237847173959480950 * daysPerYear, -0.0000296589568540237556 * daysPerYear,
			0.0000436624404335156298 * solarMass,
		},
		{
			15.3796971148509165, -25.9193146099879641, 0.179258772950371181,
			0.00268067772490389322 * daysPerYear, 0.00162824170038242295 * daysPerYear, -0.0000951592254519715870 * daysPerYear,
			0.0000515138902046611451 * solarMass,
		},
	}

	px, py, pz := 0.0, 0.0, 0.0
	for _, b := range bodies {
		px += b.vx * b.mass
		py += b.vy * b.mass
		pz += b.vz * b.mass
	}
	bodies[0].vx = -px / solarMass
	bodies[0].vy = -py / solarMass
	bodies[0].vz = -pz / solarMass

	t0 := time.Now()
	e0 := energy(bodies)
	for i := 0; i < steps; i++ {
		advance(bodies, 0.01)
	}
	e1 := energy(bodies)
	benchMs := float64(time.Since(t0).Nanoseconds()) / 1e6

	fmt.Println(roundInt(e0 * 1000000000.0))
	fmt.Println(roundInt(e1 * 1000000000.0))
	fmt.Printf("PINO_BENCH_MS=%.3f\n", benchMs)
}
