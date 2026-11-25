package main

import (
	"fmt"
	"os"
)

func isQuick() bool {
	if len(os.Args) > 1 && os.Args[1] == "--quick" {
		return true
	}
	_, err := os.Stat("benchmarks/data/quick")
	return err == nil
}

func main() {
	size := 335
	if isQuick() {
		size = 150
	}
	const maxIter = 50
	invSize := 1.0 / float64(size)

	inside := 0
	for y := 0; y < size; y++ {
		ci := 2.0*float64(y)*invSize - 1.0
		for x := 0; x < size; x++ {
			cr := 2.0*float64(x)*invSize - 1.5
			zr := 0.0
			zi := 0.0
			escaped := false
			for iter := 0; iter < maxIter; iter++ {
				tr := zr*zr - zi*zi + cr
				ti := 2.0*zr*zi + ci
				zr = tr
				zi = ti
				if zr*zr+zi*zi > 4.0 {
					escaped = true
					break
				}
			}
			if !escaped {
				inside++
			}
		}
	}
	fmt.Println(inside)
}
