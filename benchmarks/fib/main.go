package main

import (
	"fmt"
	"os"
	"time"
)

func fib(n int64) int64 {
	if n < 2 {
		return n
	}
	return fib(n-1) + fib(n-2)
}

func isQuick() bool {
	if len(os.Args) > 1 && os.Args[1] == "--quick" {
		return true
	}
	_, err := os.Stat("benchmarks/data/quick")
	return err == nil
}

func main() {
	var n int64 = 29
	if isQuick() {
		n = 25
	}
	t0 := time.Now()
	ans := fib(n)
	benchMs := float64(time.Since(t0).Nanoseconds()) / 1e6
	fmt.Println(ans)
	fmt.Printf("PINO_BENCH_MS=%.3f\n", benchMs)
}
