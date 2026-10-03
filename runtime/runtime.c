#include <stdio.h>
#include <stdlib.h>
#include "runtime.h"

jmp_buf _pino_test_jump_env;
int _pino_in_test = 0;

void pino_report_assert_fail(const char* expr, const char* file, int line) {
    if (_pino_in_test) {
        printf("       Assertion failed: %s\n", expr);
        printf("       at %s:%d\n", file, line);
        longjmp(_pino_test_jump_env, 1);
    } else {
        fprintf(stderr, "thread 'main' panicked at assertion failed: '%s'\n", expr);
        fprintf(stderr, "  at %s:%d\n", file, line);
        exit(101);
    }
}

void pino_panic(PinoString message) {
    if (_pino_in_test) {
        printf("       Panicked: %.*s\n", (int)message.len, message.data);
        longjmp(_pino_test_jump_env, 1);
    } else {
        fprintf(stderr, "thread 'main' panicked at '%.*s'\n", (int)message.len, message.data);
        exit(101);
    }
}

void pino_panic_cstr(const char* message) {
    pino_panic(pino_string_from_cstr(message));
}

#include <gc.h>

void* pino_malloc(size_t size) {
    return GC_MALLOC(size);
}

void pino_println_string(PinoString str) {
    printf("%.*s\n", (int)str.len, str.data);
}

void pino_println_cstring(const char* str) {
    printf("%s\n", str ? str : "");
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
    static LARGE_INTEGER freq;
    static int initialized = 0;
    if (!initialized) {
        QueryPerformanceFrequency(&freq);
        initialized = 1;
    }
    LARGE_INTEGER count;
    QueryPerformanceCounter(&count);
    return ((double)count.QuadPart * 1000.0) / (double)freq.QuadPart;
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

static const char* pino_memmem(const char* haystack, size_t hlen, const char* needle, size_t nlen) {
    if (nlen == 0) return haystack;
    if (hlen < nlen) return NULL;
    const char* end = haystack + (hlen - nlen);
    for (const char* p = haystack; p <= end; p++) {
        if (*p == *needle && memcmp(p, needle, nlen) == 0) return p;
    }
    return NULL;
}

regex* regex_compile(PinoString pattern) {
    regex* re = (regex*)pino_malloc(sizeof(regex));
    re->pattern = pattern;
    
    char* wrapped = (char*)pino_malloc(pattern.len + 3);
    if (pattern.len > 0 && pattern.data[0] == '^') {
        wrapped[0] = '^';
        wrapped[1] = '(';
        memcpy(wrapped + 2, pattern.data + 1, (size_t)(pattern.len - 1));
        wrapped[pattern.len + 1] = ')';
        wrapped[pattern.len + 2] = '\0';
    } else {
        wrapped[0] = '(';
        memcpy(wrapped + 1, pattern.data, (size_t)pattern.len);
        wrapped[pattern.len + 1] = ')';
        wrapped[pattern.len + 2] = '\0';
    }
    slre_compile(&re->compiled, wrapped);
    return re;
}

int regex_has_match(regex* re, PinoString text) {
    struct cap caps[20];
    return slre_match(&re->compiled, text.data, (int)text.len, caps, 20) == NULL;
}

PinoString regex_match_prefix(regex* re, PinoString text) {
    struct cap caps[20];
    if (slre_match(&re->compiled, text.data, (int)text.len, caps, 20) == NULL) {
        if (caps[0].ptr == text.data) {
            return (PinoString){ caps[0].ptr, (int64_t)caps[0].len };
        }
    }
    return (PinoString){ "", 0 };
}

PinoString regex_find(regex* re, PinoString text) {
    struct cap caps[20];
    if (slre_match(&re->compiled, text.data, (int)text.len, caps, 20) == NULL) {
        return (PinoString){ caps[0].ptr, (int64_t)caps[0].len };
    }
    return (PinoString){ "", 0 };
}

Vector_string* regex_find_all(regex* re, PinoString text) {
    Vector_string* vec = Vector_string_construct(0);
    struct cap caps[20];
    const char* ptr = text.data;
    int len = (int)text.len;
    while (len > 0 && slre_match(&re->compiled, ptr, len, caps, 20) == NULL) {
        Vector_string_push(vec, (PinoString){ caps[0].ptr, (int64_t)caps[0].len });
        int offset = (int)(caps[0].ptr - ptr) + (caps[0].len > 0 ? caps[0].len : 1);
        ptr += offset;
        len -= offset;
    }
    return vec;
}

PinoString regex_replace(regex* re, PinoString text, PinoString repl) {
    size_t text_len = (size_t)text.len;
    size_t repl_len = (size_t)repl.len;
    size_t buf_size = text_len + 1024;
    char* buffer = (char*)pino_malloc(buf_size);
    size_t buf_idx = 0;

    struct cap caps[20];
    const char* ptr = text.data;
    int len = (int)text.len;
    while (len > 0) {
        if (slre_match(&re->compiled, ptr, len, caps, 20) != NULL) {
            size_t rem = (size_t)len;
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
        size_t before_len = (size_t)(caps[0].ptr - ptr);
        if (buf_idx + before_len + repl_len >= buf_size) {
            buf_size += before_len + repl_len + 1024;
            char* new_buf = (char*)pino_malloc(buf_size);
            memcpy(new_buf, buffer, buf_idx);
            buffer = new_buf;
        }
        memcpy(buffer + buf_idx, ptr, before_len);
        buf_idx += before_len;
        memcpy(buffer + buf_idx, repl.data, repl_len);
        buf_idx += repl_len;

        int offset = (int)before_len + (caps[0].len > 0 ? caps[0].len : 1);
        ptr += offset;
        len -= offset;
    }
    buffer[buf_idx] = '\0';
    return (PinoString){ buffer, (int64_t)buf_idx };
}

PinoString string_lower(PinoString str) {
    if (str.len <= 0) return (PinoString){ "", 0 };
    char* res = (char*)GC_MALLOC_ATOMIC(str.len + 1);
    for (int64_t i = 0; i < str.len; i++) {
        unsigned char c = (unsigned char)str.data[i];
        res[i] = (c >= 'A' && c <= 'Z') ? (c + 32) : c;
    }
    res[str.len] = '\0';
    return (PinoString){ res, str.len };
}

PinoString string_upper(PinoString str) {
    if (str.len <= 0) return (PinoString){ "", 0 };
    char* res = (char*)GC_MALLOC_ATOMIC(str.len + 1);
    for (int64_t i = 0; i < str.len; i++) {
        unsigned char c = (unsigned char)str.data[i];
        res[i] = (c >= 'a' && c <= 'z') ? (c - 32) : c;
    }
    res[str.len] = '\0';
    return (PinoString){ res, str.len };
}

PinoString string_trim_start(PinoString str) {
    const char* p = str.data;
    int64_t len = str.len;
    while (len > 0 && (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n' || *p == '\v' || *p == '\f')) {
        p++;
        len--;
    }
    return (PinoString){ p, len };
}

PinoString string_trim_end(PinoString str) {
    const char* p = str.data;
    int64_t len = str.len;
    while (len > 0) {
        char c = p[len - 1];
        if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\v' || c == '\f') {
            len--;
        } else {
            break;
        }
    }
    return (PinoString){ p, len };
}

PinoString string_trim(PinoString str) {
    return string_trim_end(string_trim_start(str));
}

int string_contains(PinoString str, PinoString sub) {
    if (sub.len == 0) return 1;
    if (str.len < sub.len) return 0;
    return pino_memmem(str.data, (size_t)str.len, sub.data, (size_t)sub.len) != NULL;
}

int string_starts_with(PinoString str, PinoString prefix) {
    if (prefix.len > str.len) return 0;
    return memcmp(str.data, prefix.data, (size_t)prefix.len) == 0;
}

int string_ends_with(PinoString str, PinoString suffix) {
    if (suffix.len > str.len) return 0;
    return memcmp(str.data + (str.len - suffix.len), suffix.data, (size_t)suffix.len) == 0;
}

int string_index_of(PinoString str, PinoString sub) {
    if (sub.len == 0) return 0;
    const char* pos = pino_memmem(str.data, (size_t)str.len, sub.data, (size_t)sub.len);
    if (!pos) return -1;
    int char_idx = 0;
    const char* cur = str.data;
    while (cur < pos) {
        if ((*cur & 0xC0) != 0x80) char_idx++;
        cur++;
    }
    return char_idx;
}

int string_last_index_of(PinoString str, PinoString sub) {
    if (sub.len == 0) {
        int char_cnt = 0;
        for (int64_t i = 0; i < str.len; i++) {
            if ((str.data[i] & 0xC0) != 0x80) char_cnt++;
        }
        return char_cnt;
    }
    const char* last_pos = NULL;
    const char* cur = str.data;
    size_t rem = (size_t)str.len;
    while (rem >= (size_t)sub.len) {
        const char* pos = pino_memmem(cur, rem, sub.data, (size_t)sub.len);
        if (!pos) break;
        last_pos = pos;
        size_t adv = (pos - cur) + (sub.len > 0 ? (size_t)sub.len : 1);
        cur += adv;
        rem -= adv;
    }
    if (!last_pos) return -1;
    int char_idx = 0;
    const char* p = str.data;
    while (p < last_pos) {
        if ((*p & 0xC0) != 0x80) char_idx++;
        p++;
    }
    return char_idx;
}

PinoString string_replace(PinoString str, PinoString old_sub, PinoString new_sub) {
    if (str.len == 0 || old_sub.len == 0) return str;

    size_t count = 0;
    const char* cur = str.data;
    size_t rem = (size_t)str.len;
    while (rem >= (size_t)old_sub.len) {
        const char* pos = pino_memmem(cur, rem, old_sub.data, (size_t)old_sub.len);
        if (!pos) break;
        count++;
        size_t adv = (pos - cur) + (size_t)old_sub.len;
        cur += adv;
        rem -= adv;
    }

    if (count == 0) return str;

    int64_t res_len = str.len + (int64_t)count * (new_sub.len - old_sub.len);
    char* res = (char*)GC_MALLOC_ATOMIC(res_len + 1);
    char* dst = res;
    cur = str.data;
    rem = (size_t)str.len;
    while (rem >= (size_t)old_sub.len) {
        const char* pos = pino_memmem(cur, rem, old_sub.data, (size_t)old_sub.len);
        if (!pos) break;
        size_t part_len = pos - cur;
        memcpy(dst, cur, part_len);
        dst += part_len;
        memcpy(dst, new_sub.data, (size_t)new_sub.len);
        dst += new_sub.len;
        size_t adv = part_len + (size_t)old_sub.len;
        cur += adv;
        rem -= adv;
    }
    if (rem > 0) {
        memcpy(dst, cur, rem);
        dst += rem;
    }
    *dst = '\0';
    return (PinoString){ res, res_len };
}

Vector_string* string_split(PinoString str, PinoString sep) {
    Vector_string* vec = Vector_string_construct(0);
    if (str.len == 0) return vec;
    if (sep.len == 0) {
        int64_t i = 0;
        while (i < str.len) {
            int64_t next = i + 1;
            while (next < str.len && (str.data[next] & 0xC0) == 0x80) {
                next++;
            }
            Vector_string_push(vec, (PinoString){ str.data + i, next - i });
            i = next;
        }
        return vec;
    }

    const char* start = str.data;
    size_t rem = (size_t)str.len;
    while (rem >= (size_t)sep.len) {
        const char* pos = pino_memmem(start, rem, sep.data, (size_t)sep.len);
        if (!pos) break;
        size_t part_len = pos - start;
        Vector_string_push(vec, (PinoString){ start, (int64_t)part_len });
        size_t adv = part_len + (size_t)sep.len;
        start += adv;
        rem -= adv;
    }
    Vector_string_push(vec, (PinoString){ start, (int64_t)rem });
    return vec;
}

unsigned long pino_string_hash(PinoString str) {
    unsigned long hash = 5381;
    for (int64_t i = 0; i < str.len; i++) {
        hash = ((hash << 5) + hash) + (unsigned char)str.data[i];
    }
    return hash;
}
