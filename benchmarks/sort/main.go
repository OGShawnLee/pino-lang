package main

import (
	"fmt"
	"os"
	"time"
)

func quicksort(arr []int64, lo, hi int) {
	if lo >= hi {
		return
	}
	pivot := arr[lo+(hi-lo)/2]
	i := lo
	j := hi
	for i <= j {
		for arr[i] < pivot {
			i++
		}
		for arr[j] > pivot {
			j--
		}
		if i <= j {
			arr[i], arr[j] = arr[j], arr[i]
			i++
			j--
		}
	}
	if lo < j {
		quicksort(arr, lo, j)
	}
	if i < hi {
		quicksort(arr, i, hi)
	}
}

func isQuick() bool {
	if len(os.Args) > 1 && os.Args[1] == "--quick" {
		return true
	}
	_, err := os.Stat("benchmarks/data/quick")
	return err == nil
}

func main() {
	n := 25000
	if isQuick() {
		n = 5000
	}

	t0 := time.Now()
	var state int64 = 123456789
	arr := make([]int64, 0, n)
	for i := 0; i < n; i++ {
		state = (state*1664525 + 1013904223) % 2147483648
		arr = append(arr, state)
	}

	quicksort(arr, 0, n-1)

	var checksum int64 = 0
	for i := 0; i < n; i++ {
		checksum = (checksum + (int64(i+1) * (arr[i] % 1000003))) % 1000000007
	}
	benchMs := float64(time.Since(t0).Nanoseconds()) / 1e6

	fmt.Println(arr[0])
	fmt.Println(arr[n/2])
	fmt.Println(arr[n-1])
	fmt.Println(checksum)
	fmt.Printf("PINO_BENCH_MS=%.3f\n", benchMs)
}
