#ifndef PINO_RUNTIME_H
#define PINO_RUNTIME_H

#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>
#include <string.h>
#include <time.h>
#include <gc.h>

void* pino_malloc(size_t size);

/* PinoString 16-byte immutable value type (Go / Vlang model) */
typedef struct {
    const char* data;
    int64_t len;
} PinoString;

#define PINO_STR(s) ((PinoString){ (s), (int64_t)(sizeof(s) - 1) })

static inline PinoString pino_string_from_cstr(const char* s) {
    if (!s) return (PinoString){ "", 0 };
    return (PinoString){ s, (int64_t)strlen(s) };
}

static inline PinoString pino_string_substring(PinoString str, int64_t start, int64_t len) {
    if (start < 0) start = 0;
    if (start > str.len) return (PinoString){ "", 0 };
    if (len < 0) len = 0;
    if (start + len > str.len) len = str.len - start;
    return (PinoString){ str.data + start, len };
}

static inline bool pino_string_equal(PinoString a, PinoString b) {
    if (a.len != b.len) return false;
    if (a.data == b.data) return true;
    return memcmp(a.data, b.data, (size_t)a.len) == 0;
}

static inline int pino_string_compare(PinoString a, PinoString b) {
    size_t min_len = a.len < b.len ? (size_t)a.len : (size_t)b.len;
    int cmp = memcmp(a.data, b.data, min_len);
    if (cmp != 0) return cmp;
    return (a.len > b.len) - (a.len < b.len);
}

static inline PinoString pino_string_to_owned(PinoString str) {
    if (str.len <= 0) return (PinoString){ "", 0 };
    char* buf = (char*)GC_MALLOC_ATOMIC(str.len + 1);
    memcpy(buf, str.data, (size_t)str.len);
    buf[str.len] = '\0';
    return (PinoString){ buf, str.len };
}

static inline const char* pino_string_to_cstring(PinoString str) {
    if (str.len <= 0) return "";
    char* buf = (char*)GC_MALLOC_ATOMIC(str.len + 1);
    memcpy(buf, str.data, (size_t)str.len);
    buf[str.len] = '\0';
    return buf;
}

static inline PinoString pino_string_concat(PinoString a, PinoString b) {
    int64_t total_len = a.len + b.len;
    if (total_len <= 0) return (PinoString){ "", 0 };
    char* buf = (char*)GC_MALLOC_ATOMIC(total_len + 1);
    if (a.len > 0) memcpy(buf, a.data, (size_t)a.len);
    if (b.len > 0) memcpy(buf + a.len, b.data, (size_t)b.len);
    buf[total_len] = '\0';
    return (PinoString){ buf, total_len };
}

static inline int pino_string_contains_rune(PinoString str, int rune) {
    for (int64_t i = 0; i < str.len; i++) {
        if ((unsigned char)str.data[i] == (unsigned char)rune) return 1;
    }
    return 0;
}

void pino_println_string(PinoString str);
void pino_println_cstring(const char* str);
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
    PinoString* items;
    int length;
    int capacity;
};

static inline Vector_string* Vector_string_construct(int length) {
    Vector_string* vec = (Vector_string*)pino_malloc(sizeof(Vector_string));
    vec->length = length;
    vec->capacity = length > 0 ? length : 4;
    vec->items = (PinoString*)pino_malloc(vec->capacity * sizeof(PinoString));
    memset(vec->items, 0, vec->capacity * sizeof(PinoString));
    return vec;
}

static inline Vector_string* Vector_string_push(Vector_string* vec, PinoString item) {
    if (vec->length >= vec->capacity) {
        vec->capacity = vec->capacity == 0 ? 4 : vec->capacity * 2;
        PinoString* new_items = (PinoString*)pino_malloc(vec->capacity * sizeof(PinoString));
        if (vec->items) {
            memcpy(new_items, vec->items, vec->length * sizeof(PinoString));
        }
        vec->items = new_items;
    }
    vec->items[vec->length++] = item;
    return vec;
}

static inline bool Vector_string_equals(const Vector_string* a, const Vector_string* b) {
    if (a == b) return true;
    if (!a || !b) return false;
    if (a->length != b->length) return false;
    for (int i = 0; i < a->length; i++) {
        if (!pino_string_equal(a->items[i], b->items[i])) return false;
    }
    return true;
}

#include <setjmp.h>

typedef struct {
    void* fn_ptr;
    void* env;
} PinoClosure;

extern jmp_buf _pino_test_jump_env;
extern int _pino_in_test;

void pino_report_assert_fail(const char* expr, const char* file, int line);
void pino_panic(PinoString message);
void pino_panic_cstr(const char* message);

#define pino_assert(cond, expr_str, file, line) \
    if (!(cond)) { \
        pino_report_assert_fail(expr_str, file, line); \
    }

typedef struct regex regex;
struct regex {
    PinoString pattern;
    struct slre compiled;
};

regex* regex_compile(PinoString pattern);
static inline bool regex_equals(const regex* a, const regex* b) {
    if (a == b) return true;
    if (!a || !b) return false;
    return pino_string_equal(a->pattern, b->pattern);
}
int regex_has_match(regex* re, PinoString text);
PinoString regex_match_prefix(regex* re, PinoString text);
PinoString regex_find(regex* re, PinoString text);
Vector_string* regex_find_all(regex* re, PinoString text);
PinoString regex_replace(regex* re, PinoString text, PinoString repl);

static inline int string_len(PinoString str) {
    return (int)str.len;
}
PinoString string_lower(PinoString str);
PinoString string_upper(PinoString str);
PinoString string_trim(PinoString str);
int string_contains(PinoString str, PinoString sub);
Vector_string* string_split(PinoString str, PinoString sep);
PinoString string_replace(PinoString str, PinoString old_sub, PinoString new_sub);
static inline PinoString string_substring(PinoString str, int start, int len) {
    return pino_string_substring(str, (int64_t)start, (int64_t)len);
}
int string_starts_with(PinoString str, PinoString prefix);
int string_ends_with(PinoString str, PinoString suffix);
int string_index_of(PinoString str, PinoString sub);
int string_last_index_of(PinoString str, PinoString sub);
PinoString string_trim_start(PinoString str);
PinoString string_trim_end(PinoString str);
unsigned long pino_string_hash(PinoString str);

#endif // PINO_RUNTIME_H
