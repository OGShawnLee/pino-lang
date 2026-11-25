package main

import (
	"fmt"
	"os"
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
	var n int64 = 33
	if isQuick() {
		n = 25
	}
	fmt.Println(fib(n))
}
