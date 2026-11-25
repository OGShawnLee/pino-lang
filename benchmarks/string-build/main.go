package main

import (
	"fmt"
	"os"
	"strconv"
	"strings"
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

	s := ""
	for i := 0; i < n; i++ {
		piece := "w" + strconv.Itoa(i%1000) + ","
		s = s + piece
	}

	parts := strings.Split(s, ",")
	joined := strings.Join(parts, ":")

	fmt.Println(len(s))
	fmt.Println(len(parts))
	fmt.Println(len(joined))
}
