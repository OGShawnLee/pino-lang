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
