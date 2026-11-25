package main

import (
	"fmt"
	"os"
	"strings"
)

func main() {
	data, err := os.ReadFile("benchmarks/data/word-count.txt")
	if err != nil {
		fmt.Fprintf(os.Stderr, "failed to read benchmarks/data/word-count.txt: %v\n", err)
		os.Exit(1)
	}
	text := string(data)

	counts := make(map[string]int)
	total := 0

	lines := strings.Split(text, "\n")
	for _, line := range lines {
		words := strings.Split(line, " ")
		for _, w := range words {
			if len(w) == 0 {
				continue
			}
			total++
			counts[w]++
		}
	}

	fmt.Println(total)
	fmt.Println(len(counts))

	used := make(map[string]bool)
	keys := make([]string, 0, len(counts))
	for k := range counts {
		keys = append(keys, k)
	}
	limit := 10
	if len(keys) < limit {
		limit = len(keys)
	}

	for iter := 0; iter < limit; iter++ {
		bestWord := ""
		bestCount := -1
		for _, k := range keys {
			if used[k] {
				continue
			}
			c := counts[k]
			if c > bestCount || (c == bestCount && k < bestWord) {
				bestCount = c
				bestWord = k
			}
		}
		used[bestWord] = true
		fmt.Printf("%s %d\n", bestWord, bestCount)
	}
}
