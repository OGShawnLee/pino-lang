package main

import (
	"fmt"
	"os"
)

type Tree struct {
	Left  *Tree
	Right *Tree
}

func makeTree(depth int) *Tree {
	if depth <= 0 {
		return &Tree{}
	}
	return &Tree{
		Left:  makeTree(depth - 1),
		Right: makeTree(depth - 1),
	}
}

func checkTree(t *Tree) int64 {
	if t.Left == nil {
		return 1
	}
	return 1 + checkTree(t.Left) + checkTree(t.Right)
}

func pow2(exp int) int {
	r := 1
	for i := 0; i < exp; i++ {
		r *= 2
	}
	return r
}

func isQuick() bool {
	if len(os.Args) > 1 && os.Args[1] == "--quick" {
		return true
	}
	_, err := os.Stat("benchmarks/data/quick")
	return err == nil
}

func main() {
	minDepth := 4
	maxDepth := 14
	if isQuick() {
		maxDepth = 9
	}
	stretchDepth := maxDepth + 1

	stretchCheck := checkTree(makeTree(stretchDepth))
	longLived := makeTree(maxDepth)

	totalCheck := stretchCheck
	for d := minDepth; d <= maxDepth; d += 2 {
		iterations := pow2(maxDepth - d + minDepth)
		var check int64 = 0
		for i := 0; i < iterations; i++ {
			check += checkTree(makeTree(d))
		}
		totalCheck += check
	}

	longCheck := checkTree(longLived)
	totalCheck += longCheck

	fmt.Println(stretchCheck)
	fmt.Println(totalCheck)
	fmt.Println(longCheck)
}
