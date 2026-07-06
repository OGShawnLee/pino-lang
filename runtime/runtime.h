#ifndef PINO_RUNTIME_H
#define PINO_RUNTIME_H

#include <stddef.h>

void* pino_malloc(size_t size);
void pino_println_string(const char* str);
void pino_println_int(int val);
void pino_println_float(double val);

#endif // PINO_RUNTIME_H
