# Python Benchmark: recursive fib(28) and loop of 100,000
import time

def fib(n):
    if n <= 1:
        return n
    return fib(n - 1) + fib(n - 2)

print("--- BENCHMARK PYTHON ---")

# 1. Fibonacci
fib_start = time.perf_counter()
fib_result = fib(28)
fib_duration = (time.perf_counter() - fib_start) * 1000
print("1. Fibonacci(28) recursivo:")
print(f"   Resultado: {fib_result}")
print(f"   Tiempo: {fib_duration:.2f} ms")

# 2. Loop
loop_start = time.perf_counter()
count = 0
for _ in range(100000):
    count += 1
loop_duration = (time.perf_counter() - loop_start) * 1000
print("2. Bucle simple de 100,000 iteraciones:")
print(f"   Resultado: {count}")
print(f"   Tiempo: {loop_duration:.2f} ms")
