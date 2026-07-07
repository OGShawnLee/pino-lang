#ifndef PINO_RUNTIME_H
#define PINO_RUNTIME_H

#include <stddef.h>
#include <time.h>

void* pino_malloc(size_t size);
void pino_println_string(const char* str);
void pino_println_int(int val);
void pino_println_float(double val);

double pino_time(void);
double pino_rand_float(void);
int pino_rand_int(int limit);
void pino_sleep(int ms);
void pino_clear(void);

#endif // PINO_RUNTIME_H
