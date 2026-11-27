package main

import (
	"fmt"
	"os"
	"strconv"
	"strings"
	"time"
)

func isQuick() bool {
	if len(os.Args) > 1 && os.Args[1] == "--quick" {
		return true
	}
	_, err := os.Stat("benchmarks/data/quick")
	return err == nil
}

func main() {
	n := 7500
	if isQuick() {
		n = 1500
	}

	t0 := time.Now()
	s := ""
	for i := 0; i < n; i++ {
		piece := "w" + strconv.Itoa(i%1000) + ","
		s = s + piece
	}

	parts := strings.Split(s, ",")
	joined := strings.Join(parts, ":")
	benchMs := float64(time.Since(t0).Nanoseconds()) / 1e6

	fmt.Println(len(s))
	fmt.Println(len(parts))
	fmt.Println(len(joined))
	fmt.Printf("PINO_BENCH_MS=%.3f\n", benchMs)
}
