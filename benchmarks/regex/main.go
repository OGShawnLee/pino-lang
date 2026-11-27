package main

import (
	"fmt"
	"os"
	"regexp"
	"time"
)

func main() {
	data, err := os.ReadFile("benchmarks/data/regex.txt")
	if err != nil {
		fmt.Fprintf(os.Stderr, "failed to read benchmarks/data/regex.txt: %v\n", err)
		os.Exit(1)
	}
	text := string(data)

	t0 := time.Now()
	emailRe := regexp.MustCompile(`\w+@\w+\.com`)
	identRe := regexp.MustCompile(`[A-Za-z_][A-Za-z0-9_]*`)

	emails := emailRe.FindAllString(text, -1)
	idents := identRe.FindAllString(text, -1)
	emailCount := len(emails)
	identCount := len(idents)
	benchMs := float64(time.Since(t0).Nanoseconds()) / 1e6

	fmt.Println(emailCount)
	fmt.Println(identCount)
	fmt.Printf("PINO_BENCH_MS=%.3f\n", benchMs)
}
