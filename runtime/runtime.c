#include <stdio.h>
#include <stdlib.h>
#include "runtime.h"

#ifdef PINO_GC
#include <gc.h>
#endif

void* pino_malloc(size_t size) {
#ifdef PINO_GC
    return GC_MALLOC(size);
#else
    return malloc(size);
#endif
}

void pino_println_string(const char* str) {
    printf("%s\n", str);
}

void pino_println_int(int val) {
    printf("%d\n", val);
}

void pino_println_float(double val) {
    printf("%g\n", val);
}

#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#endif

long long pino_time(void) {
    return (long long)time(NULL) * 1000;
}

double pino_rand_float(void) {
    return (double)rand() / ((double)RAND_MAX + 1.0);
}

int pino_rand_int(int limit) {
    if (limit <= 0) return 0;
    return rand() % limit;
}

void pino_sleep(int ms) {
#ifdef _WIN32
    Sleep(ms);
#else
    usleep(ms * 1000);
#endif
}

void pino_clear(void) {
#ifdef _WIN32
    system("cls");
#else
    system("clear");
#endif
}
