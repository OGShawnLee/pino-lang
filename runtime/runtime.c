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

int string_len(const char* str) {
    if (!str) return 0;
    int len = 0;
    while (*str) {
        if ((*str & 0xC0) != 0x80) len++;
        str++;
    }
    return len;
}

const char* string_lower(const char* str) {
    if (!str) return "";
    size_t len = strlen(str);
    char* res = (char*)pino_malloc(len + 1);
    for (size_t i = 0; i < len; i++) {
        unsigned char c = str[i];
        if (c >= 'A' && c <= 'Z') {
            res[i] = c + 32;
        } else {
            res[i] = c;
        }
    }
    res[len] = '\0';
    return res;
}

const char* string_upper(const char* str) {
    if (!str) return "";
    size_t len = strlen(str);
    char* res = (char*)pino_malloc(len + 1);
    for (size_t i = 0; i < len; i++) {
        unsigned char c = str[i];
        if (c >= 'a' && c <= 'z') {
            res[i] = c - 32;
        } else {
            res[i] = c;
        }
    }
    res[len] = '\0';
    return res;
}

const char* string_trim_start(const char* str) {
    if (!str) return "";
    while (*str == ' ' || *str == '\t' || *str == '\r' || *str == '\n' || *str == '\v' || *str == '\f') {
        str++;
    }
    size_t len = strlen(str);
    char* res = (char*)pino_malloc(len + 1);
    memcpy(res, str, len);
    res[len] = '\0';
    return res;
}

const char* string_trim_end(const char* str) {
    if (!str) return "";
    size_t len = strlen(str);
    while (len > 0) {
        char c = str[len - 1];
        if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\v' || c == '\f') {
            len--;
        } else {
            break;
        }
    }
    char* res = (char*)pino_malloc(len + 1);
    memcpy(res, str, len);
    res[len] = '\0';
    return res;
}

const char* string_trim(const char* str) {
    if (!str) return "";
    while (*str == ' ' || *str == '\t' || *str == '\r' || *str == '\n' || *str == '\v' || *str == '\f') {
        str++;
    }
    size_t len = strlen(str);
    while (len > 0) {
        char c = str[len - 1];
        if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\v' || c == '\f') {
            len--;
        } else {
            break;
        }
    }
    char* res = (char*)pino_malloc(len + 1);
    memcpy(res, str, len);
    res[len] = '\0';
    return res;
}

int string_contains(const char* str, const char* sub) {
    if (!str || !sub) return 0;
    return strstr(str, sub) != NULL;
}

int string_starts_with(const char* str, const char* prefix) {
    if (!str || !prefix) return 0;
    return strncmp(str, prefix, strlen(prefix)) == 0;
}

int string_ends_with(const char* str, const char* suffix) {
    if (!str || !suffix) return 0;
    size_t str_len = strlen(str);
    size_t suf_len = strlen(suffix);
    if (suf_len > str_len) return 0;
    return strcmp(str + str_len - suf_len, suffix) == 0;
}

int string_index_of(const char* str, const char* sub) {
    if (!str || !sub) return -1;
    const char* pos = strstr(str, sub);
    if (!pos) return -1;
    int char_idx = 0;
    while (str < pos) {
        if ((*str & 0xC0) != 0x80) char_idx++;
        str++;
    }
    return char_idx;
}

const char* string_substring(const char* str, int start, int len) {
    if (!str || start < 0 || len <= 0) return "";
    while (start > 0 && *str) {
        if ((*str & 0xC0) != 0x80) start--;
        str++;
    }
    while (*str && (*str & 0xC0) == 0x80) {
        str++;
    }
    const char* end = str;
    int count = len;
    while (count > 0 && *end) {
        if ((*end & 0xC0) != 0x80) count--;
        end++;
    }
    while (*end && (*end & 0xC0) == 0x80) {
        end++;
    }
    size_t byte_len = end - str;
    char* res = (char*)pino_malloc(byte_len + 1);
    memcpy(res, str, byte_len);
    res[byte_len] = '\0';
    return res;
}

const char* string_replace(const char* str, const char* old_sub, const char* new_sub) {
    if (!str || !old_sub || !new_sub) return str ? str : "";
    size_t old_len = strlen(old_sub);
    size_t new_len = strlen(new_sub);
    if (old_len == 0) return str;

    size_t count = 0;
    const char* tmp = str;
    while ((tmp = strstr(tmp, old_sub))) {
        count++;
        tmp += old_len;
    }

    size_t res_len = strlen(str) + count * (new_len - old_len);
    char* res = (char*)pino_malloc(res_len + 1);
    char* dst = res;
    while (*str) {
        if (strstr(str, old_sub) == str) {
            memcpy(dst, new_sub, new_len);
            dst += new_len;
            str += old_len;
        } else {
            *dst++ = *str++;
        }
    }
    *dst = '\0';
    return res;
}

Vector_string* string_split(const char* str, const char* sep) {
    Vector_string* vec = Vector_string_construct(0);
    if (!str || !sep) return vec;
    size_t sep_len = strlen(sep);
    if (sep_len == 0) {
        while (*str) {
            const char* next = str + 1;
            while (*next && (*next & 0xC0) == 0x80) {
                next++;
            }
            size_t byte_len = next - str;
            char* item = (char*)pino_malloc(byte_len + 1);
            memcpy(item, str, byte_len);
            item[byte_len] = '\0';
            Vector_string_push(vec, item);
            str = next;
        }
        return vec;
    }

    const char* start = str;
    const char* pos;
    while ((pos = strstr(start, sep))) {
        size_t len = pos - start;
        char* item = (char*)pino_malloc(len + 1);
        memcpy(item, start, len);
        item[len] = '\0';
        Vector_string_push(vec, item);
        start = pos + sep_len;
    }
    size_t len = strlen(start);
    char* item = (char*)pino_malloc(len + 1);
    memcpy(item, start, len);
    item[len] = '\0';
    Vector_string_push(vec, item);
    return vec;
}
