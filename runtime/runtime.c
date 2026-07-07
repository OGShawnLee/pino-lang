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
#include <sys/time.h>
#include <unistd.h>
#endif

double pino_time(void) {
#ifdef _WIN32
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    unsigned long long ticks = ((unsigned long long)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
    return (double)(ticks - 116444736000000000ULL) / 10000.0;
#else
    struct timeval tv;
    gettimeofday(&tv, NULL);
    return (double)tv.tv_sec * 1000.0 + (double)tv.tv_usec / 1000.0;
#endif
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

regex* regex_compile(const char* pattern) {
    regex* re = (regex*)pino_malloc(sizeof(regex));
    re->pattern = pattern;
    memset(re->compiled, 0, sizeof(re->compiled));
    memset(re->ccl_buf, 0, sizeof(re->ccl_buf));
    re_compile_to(pattern, re->compiled, re->ccl_buf);
    return re;
}

int regex_has_match(regex* re, const char* text) {
    int match_length = 0;
    return re_matchp(re->compiled, text, &match_length) >= 0;
}

const char* regex_match_prefix(regex* re, const char* text) {
    int match_length = 0;
    int idx = re_matchp(re->compiled, text, &match_length);
    if (idx == 0) {
        char* match = (char*)pino_malloc(match_length + 1);
        memcpy(match, text, match_length);
        match[match_length] = '\0';
        return match;
    }
    return "";
}

const char* regex_find(regex* re, const char* text) {
    int match_length = 0;
    int idx = re_matchp(re->compiled, text, &match_length);
    if (idx >= 0) {
        char* match = (char*)pino_malloc(match_length + 1);
        memcpy(match, text + idx, match_length);
        match[match_length] = '\0';
        return match;
    }
    return "";
}

Vector_string* regex_find_all(regex* re, const char* text) {
    Vector_string* vec = Vector_string_construct(0);
    int match_length = 0;
    const char* ptr = text;
    while (*ptr != '\0') {
        int idx = re_matchp(re->compiled, ptr, &match_length);
        if (idx < 0) break;
        char* match = (char*)pino_malloc(match_length + 1);
        memcpy(match, ptr + idx, match_length);
        match[match_length] = '\0';
        Vector_string_push(vec, match);
        ptr += idx + (match_length > 0 ? match_length : 1);
    }
    return vec;
}

const char* regex_replace(regex* re, const char* text, const char* repl) {
    size_t text_len = strlen(text);
    size_t repl_len = strlen(repl);
    size_t buf_size = text_len + 1024;
    char* buffer = (char*)pino_malloc(buf_size);
    size_t buf_idx = 0;
    buffer[0] = '\0';

    int match_length = 0;
    const char* ptr = text;
    while (*ptr != '\0') {
        int idx = re_matchp(re->compiled, ptr, &match_length);
        if (idx < 0) {
            size_t rem = strlen(ptr);
            if (buf_idx + rem >= buf_size) {
                buf_size += rem + 1024;
                char* new_buf = (char*)pino_malloc(buf_size);
                memcpy(new_buf, buffer, buf_idx);
                buffer = new_buf;
            }
            memcpy(buffer + buf_idx, ptr, rem);
            buf_idx += rem;
            break;
        }
        if (idx > 0) {
            if (buf_idx + idx >= buf_size) {
                buf_size += idx + 1024;
                char* new_buf = (char*)pino_malloc(buf_size);
                memcpy(new_buf, buffer, buf_idx);
                buffer = new_buf;
            }
            memcpy(buffer + buf_idx, ptr, idx);
            buf_idx += idx;
        }
        if (repl_len > 0) {
            if (buf_idx + repl_len >= buf_size) {
                buf_size += repl_len + 1024;
                char* new_buf = (char*)pino_malloc(buf_size);
                memcpy(new_buf, buffer, buf_idx);
                buffer = new_buf;
            }
            memcpy(buffer + buf_idx, repl, repl_len);
            buf_idx += repl_len;
        }
        ptr += idx + (match_length > 0 ? match_length : 1);
    }
    buffer[buf_idx] = '\0';
    return buffer;
}
