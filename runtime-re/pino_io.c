/*
 * pino_io.c - panics, console and file I/O, time, random numbers, regex wrappers
 * (design 056 sections 5.9, 5.10, 7.2).
 */
#ifndef _WIN32
#define _POSIX_C_SOURCE 200809L
#endif

#include "pino_rt.h"

#include <errno.h>
#include <math.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <process.h>
#include <windows.h>
#else
#include <sys/stat.h>
#include <sys/time.h>
#include <time.h>
#include <unistd.h>
#endif

/* ------------------------------------------------------------------------- */
/* Panics                                                                     */
/* ------------------------------------------------------------------------- */

static PINO_NORETURN void pino_emit_test_failure(const char* kind, PinoString msg, PinoLoc loc, int exit_code) {
  fflush(stdout);
  fprintf(stderr, "\x1fPINO_TEST_FAIL\t%s\t%s\t%d\n", kind, loc.file != NULL ? loc.file : "<unknown>", (int)loc.line);
  if (msg.byte_len > 0) {
    fwrite(msg.ptr, 1, (size_t)msg.byte_len, stderr);
  }
  fprintf(stderr, "\n\x1fPINO_TEST_FAIL_END\n");
  fflush(stderr);
  _exit(exit_code);
}

void pino_panic_at(PinoString msg, PinoLoc loc) {
  if (pino_test_mode) {
    pino_emit_test_failure("panic", msg, loc, 101);
  }
  fflush(stdout);
  fprintf(stderr, "thread 'main' panicked at '%.*s'\n", (int)msg.byte_len, msg.ptr);
  fprintf(stderr, "  at %s:%d:%d\n", loc.file != NULL ? loc.file : "<unknown>", (int)loc.line, (int)loc.col);
  int n = 0;
  for (const PinoFrame* f = pino_frame_top; f != NULL; f = f->prev) {
    fprintf(stderr, "  %d: %s\n", n++, f->name != NULL ? f->name : "<unknown>");
  }
  fflush(stderr);
  _exit(101);
}

void pino_assert_fail_at(PinoString msg, PinoLoc loc) {
  if (pino_test_mode) {
    pino_emit_test_failure("assert", msg, loc, 104);
  }
  pino_panic_at(msg, loc);
}

void pino_panic_cstr_at(const char* msg, PinoLoc loc) {
  PinoString s = {NULL, msg, (int64_t)strlen(msg)};
  pino_panic_at(s, loc);
}

static PINO_NORETURN void panic_printf(PinoLoc loc, const char* fmt, ...) {
  char buf[1024];
  va_list ap;
  va_start(ap, fmt);
  vsnprintf(buf, sizeof buf, fmt, ap);
  va_end(ap);
  buf[sizeof buf - 1] = '\0';
  pino_panic_cstr_at(buf, loc);
}

void pino_panic_div_zero(PinoLoc loc) { pino_panic_cstr_at("Cannot divide by zero.", loc); }

void pino_panic_div_overflow(PinoLoc loc) {
  pino_panic_cstr_at("Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.", loc);
}

void pino_panic_vec_index(int64_t index, int64_t len, PinoLoc loc) {
  panic_printf(loc, "Index %lld is out of range for a vector of length %lld.", (long long)index, (long long)len);
}

void pino_panic_map_key(PinoString key_text, PinoLoc loc) {
  panic_printf(loc, "Key %.*s was not found in map.", (int)key_text.byte_len, key_text.ptr);
}

void pino_panic_aliased_mut(const char* collection, const char* target, PinoLoc loc) {
  if (target != NULL) {
    panic_printf(loc, "Cannot pass the same element of '%s' as two 'mut' arguments to '%s'.", collection, target);
  } else {
    panic_printf(loc, "Cannot pass the same element of '%s' as two 'mut' arguments to this function.", collection);
  }
}

void pino_panic_stale_handle(const char* pool_name, PinoLoc loc) {
  panic_printf(loc, "This handle no longer points to a value in '%s'; it was removed.", pool_name);
}

void pino_warn_stderr(const char* msg) {
  fflush(stdout);
  fprintf(stderr, "[WARNING] %s\n", msg);
  fflush(stderr);
}

double pino_floor_f64(double x) { return floor(x); }
double pino_fmod_f64(double a, double b) { return fmod(a, b); }

/* ------------------------------------------------------------------------- */
/* Console                                                                    */
/* ------------------------------------------------------------------------- */

void pino_println_n(const PinoString* parts, int64_t n) {
  for (int64_t i = 0; i < n; i++) {
    if (i > 0) {
      fputc(' ', stdout);
    }
    if (parts[i].byte_len > 0) {
#ifdef _WIN32
      /* Windows text-mode stdout turns '\n' into "\r\n", so skip any '\r' directly
       * preceding '\n' to prevent "\r\n" inside multiline strings becoming "\r\r\n". */
      const char* p = parts[i].ptr;
      int64_t len = parts[i].byte_len;
      int64_t start = 0;
      for (int64_t k = 0; k < len; k++) {
        if (p[k] == '\r' && k + 1 < len && p[k + 1] == '\n') {
          if (k > start) {
            fwrite(p + start, 1, (size_t)(k - start), stdout);
          }
          start = k + 1;
        }
      }
      if (len > start) {
        fwrite(p + start, 1, (size_t)(len - start), stdout);
      }
#else
      fwrite(parts[i].ptr, 1, (size_t)parts[i].byte_len, stdout);
#endif
    }
  }
  fputc('\n', stdout);
}

bool pino_readline(PinoString prompt, PinoString* out) {
  if (prompt.byte_len > 0) {
    fwrite(prompt.ptr, 1, (size_t)prompt.byte_len, stdout);
    fflush(stdout);
  }
  size_t cap = 128;
  size_t len = 0;
  char* buf = (char*)PINO_ALLOC(pino_cur_alloc, cap, "line buffer");
  int c;
  while ((c = fgetc(stdin)) != EOF && c != '\n') {
    if (c == '\r') {
      continue;
    }
    if (len == cap) {
      buf = (char*)PINO_REALLOC(pino_cur_alloc, buf, cap, cap * 2, "line buffer");
      cap *= 2;
    }
    buf[len++] = (char)c;
  }
  bool at_end = (c == EOF && len == 0) || ferror(stdin);
  if (at_end) {
    pino_dealloc(pino_cur_alloc, buf, cap);
    *out = PINO_STR_LIT("There is no more input to read.");
    return false;
  }
  *out = pino_str_from_bytes(buf, (int64_t)len);
  pino_dealloc(pino_cur_alloc, buf, cap);
  return true;
}

void pino_clear(void) {
  fflush(stdout);
#ifdef _WIN32
  HANDLE h = GetStdHandle(STD_OUTPUT_HANDLE);
  CONSOLE_SCREEN_BUFFER_INFO info;
  if (h == INVALID_HANDLE_VALUE || !GetConsoleScreenBufferInfo(h, &info)) {
    return; /* output is redirected: nothing to clear */
  }
  DWORD cells = (DWORD)info.dwSize.X * (DWORD)info.dwSize.Y;
  DWORD written = 0;
  COORD origin = {0, 0};
  FillConsoleOutputCharacterA(h, ' ', cells, origin, &written);
  FillConsoleOutputAttribute(h, info.wAttributes, cells, origin, &written);
  SetConsoleCursorPosition(h, origin);
#else
  if (isatty(STDOUT_FILENO)) {
    fputs("\033[2J\033[H", stdout);
    fflush(stdout);
  }
#endif
}

/* ------------------------------------------------------------------------- */
/* Files                                                                      */
/* ------------------------------------------------------------------------- */

int32_t pino_errno_to_ioerror_tag(int err) {
  switch (err) {
    case ENOENT:
      return PINO_IOERR_NOT_FOUND;
    case EACCES:
    case EPERM:
      return PINO_IOERR_PERMISSION_DENIED;
    case EEXIST:
      return PINO_IOERR_ALREADY_EXISTS;
    default:
      return PINO_IOERR_GREMLIN;
  }
}

/* Failure result. As in the legacy runtime, NotFound / PermissionDenied / AlreadyExists carry
 * the path and Gremlin carries the system error text. */
static PinoFileResult file_failure(int err, PinoString path) {
  PinoFileResult r;
  r.ok = false;
  r.err_tag = pino_errno_to_ioerror_tag(err);
  if (r.err_tag == PINO_IOERR_GREMLIN) {
    const char* text = strerror(err);
    r.value = pino_str_from_bytes(text, (int64_t)strlen(text));
  } else {
    r.value = pino_str_retain(path);
  }
  return r;
}

static size_t strbuf_size(int64_t cap) { return sizeof(PinoStrBuf) + (size_t)cap + 1; }

PinoFileResult pino_read_file(PinoString path) {
  PinoString cpath = pino_str_to_cstring(path);
  FILE* f = fopen(cpath.ptr, "rb");
  if (f == NULL) {
    int err = errno;
    pino_str_release(cpath);
    return file_failure(err, path);
  }
  pino_str_release(cpath);
  int64_t cap = 4096;
  int64_t len = 0;
  PinoStrBuf* b = pino_strbuf_alloc(cap);
  for (;;) {
    if (len == cap) {
      b = (PinoStrBuf*)PINO_REALLOC(pino_cur_alloc, b, strbuf_size(cap), strbuf_size(cap * 2), "string buffer");
      cap *= 2;
      b->cap = cap;
    }
    size_t got = fread(b->data + len, 1, (size_t)(cap - len), f);
    if (got == 0) {
      break;
    }
    len += (int64_t)got;
  }
  bool failed = ferror(f) != 0;
  int err = errno;
  fclose(f);
  if (failed) {
    pino_str_release(pino_strbuf_seal(b, 0));
    return file_failure(err != 0 ? err : EIO, path);
  }
  PinoFileResult r;
  r.ok = true;
  r.err_tag = 0;
  r.value = pino_strbuf_seal(b, len);
  return r;
}

PinoFileResult pino_write_file(PinoString path, PinoString content) {
  PinoString cpath = pino_str_to_cstring(path);
  FILE* f = fopen(cpath.ptr, "wb");
  if (f == NULL) {
    int err = errno;
    pino_str_release(cpath);
    return file_failure(err, path);
  }
  pino_str_release(cpath);
  bool failed = false;
  if (content.byte_len > 0 && fwrite(content.ptr, 1, (size_t)content.byte_len, f) != (size_t)content.byte_len) {
    failed = true;
  }
  if (fclose(f) != 0) {
    failed = true;
  }
  if (failed) {
    return file_failure(errno != 0 ? errno : EIO, path);
  }
  PinoFileResult r;
  r.ok = true;
  r.err_tag = 0;
  r.value = pino_str_retain(path);
  return r;
}

bool pino_file_exists(PinoString path) {
  PinoString cpath = pino_str_to_cstring(path);
  bool exists;
#ifdef _WIN32
  exists = GetFileAttributesA(cpath.ptr) != INVALID_FILE_ATTRIBUTES;
#else
  struct stat st;
  exists = stat(cpath.ptr, &st) == 0;
#endif
  pino_str_release(cpath);
  return exists;
}

/* ------------------------------------------------------------------------- */
/* Time, random numbers, sleep                                                */
/* ------------------------------------------------------------------------- */

double pino_time(void) {
#ifdef _WIN32
  FILETIME ft;
  GetSystemTimeAsFileTime(&ft);
  unsigned long long ticks = ((unsigned long long)ft.dwHighDateTime << 32) | (unsigned long long)ft.dwLowDateTime;
  /* ticks are 100 ns units since 1601-01-01; shift to the Unix epoch and convert to milliseconds. */
  return (double)(ticks - 116444736000000000ull) / 10000.0;
#else
  struct timeval tv;
  gettimeofday(&tv, NULL);
  return (double)tv.tv_sec * 1000.0 + (double)tv.tv_usec / 1000.0;
#endif
}

static uint64_t g_rand_state[4];
static bool g_rand_seeded = false;

static uint64_t splitmix64(uint64_t* x) {
  uint64_t z = (*x += 0x9E3779B97F4A7C15ull);
  z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ull;
  z = (z ^ (z >> 27)) * 0x94D049BB133111EBull;
  return z ^ (z >> 31);
}

void pino_rand_seed(uint64_t seed) {
  uint64_t x = seed;
  for (int i = 0; i < 4; i++) {
    g_rand_state[i] = splitmix64(&x);
  }
  g_rand_seeded = true;
}

static uint64_t rotl64(uint64_t x, int k) { return (x << k) | (x >> (64 - k)); }

static uint64_t rand_next(void) {
  if (!g_rand_seeded) {
    int marker = 0;
    pino_rand_seed((uint64_t)(pino_time() * 1000.0) ^ (uint64_t)(uintptr_t)&marker);
  }
  uint64_t* s = g_rand_state;
  uint64_t result = rotl64(s[1] * 5, 7) * 9; /* xoshiro256** */
  uint64_t t = s[1] << 17;
  s[2] ^= s[0];
  s[3] ^= s[1];
  s[1] ^= s[2];
  s[0] ^= s[3];
  s[2] ^= t;
  s[3] = rotl64(s[3], 45);
  return result;
}

double pino_rand_float(void) { return (double)(rand_next() >> 11) * (1.0 / 9007199254740992.0); }

int64_t pino_rand_int(int64_t limit) {
  if (limit <= 0) {
    return 0;
  }
  uint64_t lim = (uint64_t)limit;
  uint64_t threshold = ((uint64_t)0 - lim) % lim; /* rejects the biased low range */
  for (;;) {
    uint64_t r = rand_next();
    if (r >= threshold) {
      return (int64_t)(r % lim);
    }
  }
}

void pino_sleep(int64_t ms) {
  if (ms <= 0) {
    return;
  }
#ifdef _WIN32
  Sleep((DWORD)ms);
#else
  struct timespec ts;
  ts.tv_sec = (time_t)(ms / 1000);
  ts.tv_nsec = (long)(ms % 1000) * 1000000L;
  while (nanosleep(&ts, &ts) != 0 && errno == EINTR) {
  }
#endif
}
