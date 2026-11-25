package main

import (
	"fmt"
	"os"
	"regexp"
)

func main() {
	data, err := os.ReadFile("benchmarks/data/regex.txt")
	if err != nil {
		fmt.Fprintf(os.Stderr, "failed to read benchmarks/data/regex.txt: %v\n", err)
		os.Exit(1)
	}
	text := string(data)

	emailRe := regexp.MustCompile(`\w+@\w+\.com`)
	identRe := regexp.MustCompile(`[A-Za-z_][A-Za-z0-9_]*`)

	emails := emailRe.FindAllString(text, -1)
	idents := identRe.FindAllString(text, -1)

	fmt.Println(len(emails))
	fmt.Println(len(idents))
}
