#include <stdio.h>
#include <stdlib.h>
#include "runtime.h"

void* pino_malloc(size_t size) {
    return malloc(size);
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
