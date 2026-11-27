package main

import (
	"fmt"
	"os"
	"time"
)

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

func evalA(i, j int) float64 {
	ij := i + j
	return 1.0 / float64(ij*(ij+1)/2+i+1)
}

func evalATimesU(u, au []float64, n int) {
	for i := 0; i < n; i++ {
		sum := 0.0
		for j := 0; j < n; j++ {
			sum += evalA(i, j) * u[j]
		}
		au[i] = sum
	}
}

func evalAtTimesU(u, au []float64, n int) {
	for i := 0; i < n; i++ {
		sum := 0.0
		for j := 0; j < n; j++ {
			sum += evalA(j, i) * u[j]
		}
		au[i] = sum
	}
}

func evalAtATimesU(u, out, tmp []float64, n int) {
	evalATimesU(u, tmp, n)
	evalAtTimesU(tmp, out, n)
}

func isQuick() bool {
	if len(os.Args) > 1 && os.Args[1] == "--quick" {
		return true
	}
	_, err := os.Stat("benchmarks/data/quick")
	return err == nil
}

func main() {
	n := 180
	if isQuick() {
		n = 80
	}

	t0 := time.Now()
	u := make([]float64, n)
	v := make([]float64, n)
	tmp := make([]float64, n)
	for i := 0; i < n; i++ {
		u[i] = 1.0
	}

	for iter := 0; iter < 10; iter++ {
		evalAtATimesU(u, v, tmp, n)
		evalAtATimesU(v, u, tmp, n)
	}

	vbv := 0.0
	vv := 0.0
	for i := 0; i < n; i++ {
		vbv += u[i] * v[i]
		vv += v[i] * v[i]
	}

	ans := mySqrt(vbv / vv)
	outVal := roundInt(ans * 1000000000.0)
	benchMs := float64(time.Since(t0).Nanoseconds()) / 1e6
	fmt.Println(outVal)
	fmt.Printf("PINO_BENCH_MS=%.3f\n", benchMs)
}
