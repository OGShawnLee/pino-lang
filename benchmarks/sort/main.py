import os
import sys

def quicksort(arr, lo: int, hi: int) -> None:
    if lo >= hi:
        return
    pivot = arr[lo + (hi - lo) // 2]
    i = lo
    j = hi
    while i <= j:
        while arr[i] < pivot:
            i += 1
        while arr[j] > pivot:
            j -= 1
        if i <= j:
            arr[i], arr[j] = arr[j], arr[i]
            i += 1
            j -= 1
    if lo < j:
        quicksort(arr, lo, j)
    if i < hi:
        quicksort(arr, i, hi)

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    n = 5000 if quick else 25000

    state = 123456789
    arr = []
    for _ in range(n):
        state = (state * 1664525 + 1013904223) % 2147483648
        arr.append(state)

    quicksort(arr, 0, n - 1)

    checksum = 0
    for i in range(n):
        checksum = (checksum + (i + 1) * (arr[i] % 1000003)) % 1000000007

    print(arr[0])
    print(arr[n // 2])
    print(arr[n - 1])
    print(checksum)

if __name__ == "__main__":
    main()
