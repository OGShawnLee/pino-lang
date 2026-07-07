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

#include "re.h"

struct Vector_string;
typedef struct Vector_string Vector_string;
struct Vector_string {
    const char** items;
    int length;
    int capacity;
};

static inline Vector_string* Vector_string_construct(int length) {
    Vector_string* vec = (Vector_string*)pino_malloc(sizeof(Vector_string));
    vec->length = length;
    vec->capacity = length > 0 ? length : 4;
    vec->items = (const char**)pino_malloc(vec->capacity * sizeof(const char*));
    memset(vec->items, 0, vec->capacity * sizeof(const char*));
    return vec;
}

static inline Vector_string* Vector_string_push(Vector_string* vec, const char* item) {
    if (vec->length >= vec->capacity) {
        vec->capacity = vec->capacity == 0 ? 4 : vec->capacity * 2;
        const char** new_items = (const char**)pino_malloc(vec->capacity * sizeof(const char*));
        if (vec->items) {
            memcpy(new_items, vec->items, vec->length * sizeof(const char*));
        }
        vec->items = new_items;
    }
    vec->items[vec->length++] = item;
    return vec;
}

typedef struct regex regex;
struct regex {
    const char* pattern;
    struct regex_t compiled[30];
    unsigned char ccl_buf[40];
};

regex* regex_compile(const char* pattern);
int regex_has_match(regex* re, const char* text);
const char* regex_match_prefix(regex* re, const char* text);
const char* regex_find(regex* re, const char* text);
Vector_string* regex_find_all(regex* re, const char* text);
const char* regex_replace(regex* re, const char* text, const char* repl);
int string_len(const char* str);
const char* string_lower(const char* str);
const char* string_upper(const char* str);
const char* string_trim(const char* str);
int string_contains(const char* str, const char* sub);
Vector_string* string_split(const char* str, const char* sep);
const char* string_replace(const char* str, const char* old_sub, const char* new_sub);
const char* string_substring(const char* str, int start, int len);
int string_starts_with(const char* str, const char* prefix);
int string_ends_with(const char* str, const char* suffix);
int string_index_of(const char* str, const char* sub);
const char* string_trim_start(const char* str);
const char* string_trim_end(const char* str);

#endif // PINO_RUNTIME_H
