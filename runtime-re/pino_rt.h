/*
 * pino_rt.h - core header of the GC-free C99 reference-counting runtime used by
 * the `-re` C emitter (ticket 064, design 056 sections 2.2, 2.3, 4.3, 4.5, 4.6, 7).
 *
 * Rules for this header and every file in runtime-re/:
 *   - UTF-8 without BOM, LF line endings (TCC 0.9.27 rejects a BOM).
 *   - Plain C99. Builds with `tcc` 0.9.27 and `gcc -O2 -Wall -Wextra`.
 *   - No <gc.h>, no <stdatomic.h>, no __sync_* builtins under TCC.
 *
 * Ownership conventions (summary, see design 056 section 4.1):
 *   - Every function that takes a PinoString, PinoRegex* or PinoVec_string* only
 *     BORROWS it. The caller keeps its reference.
 *   - Every PinoString or pointer a function RETURNS is OWNED (+1). Release it
 *     with pino_str_release(), pino_regex_release() or pino_vec_string_release().
 *   - Static literals (PINO_STR_LIT) and immortal singletons need no release.
 */
#ifndef PINO_RT_H
#define PINO_RT_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(__GNUC__) || defined(__TINYC__)
#define PINO_NORETURN __attribute__((noreturn))
#else
#define PINO_NORETURN
#endif

/* ------------------------------------------------------------------------- */
/* Pluggable allocator (design 4.5)                                           */
/* ------------------------------------------------------------------------- */

typedef struct PinoAlloc {
  void* (*alloc)(void* ctx, size_t size);
  void* (*realloc)(void* ctx, void* ptr, size_t old_size, size_t new_size);
  void (*dealloc)(void* ctx, void* ptr, size_t size);
  void* ctx;
} PinoAlloc;

/* Default allocator, backed by malloc / realloc / free. */
extern PinoAlloc pino_sys_alloc;

/* The allocator the runtime itself (strings, regex, io) uses. Starts as
 * &pino_sys_alloc. Tests and arena-aware programs may swap it. */
extern PinoAlloc* pino_cur_alloc;

/* Allocation statistics, kept in every build mode. */
extern uint64_t pino_stat_alloc_count;
extern uint64_t pino_stat_dealloc_count;
extern uint64_t pino_stat_alloc_bytes;

#ifdef PINO_STATS
/* Opt-in deterministic memory & RC telemetry (-DPINO_STATS). */
extern uint64_t pino_stat_realloc_count;
extern uint64_t pino_stat_live_bytes;
extern uint64_t pino_stat_peak_live_bytes;
extern uint64_t pino_stat_rc_inc_count;
extern uint64_t pino_stat_rc_dec_count;
#endif

/* Allocate / reallocate / release through `a`. `type_name`, `file` and `line`
 * feed the -DPINO_DEBUG_RC live list; they are ignored otherwise. Allocation
 * failure prints a plain message and exits with code 101. `size` passed to
 * pino_dealloc / pino_realloc_at must equal the size that was allocated. */
void* pino_alloc_at(PinoAlloc* a, size_t size, const char* type_name, const char* file, int line);
void* pino_realloc_at(PinoAlloc* a, void* p, size_t old_size, size_t new_size, const char* type_name,
                      const char* file, int line);
void pino_dealloc(PinoAlloc* a, void* p, size_t size);

#define PINO_ALLOC(a, size, type_name) pino_alloc_at((a), (size), (type_name), __FILE__, __LINE__)
#define PINO_REALLOC(a, p, old_size, new_size, type_name) \
  pino_realloc_at((a), (p), (old_size), (new_size), (type_name), __FILE__, __LINE__)

/* ------------------------------------------------------------------------- */
/* 8-byte atomic-ready RC header (design 4.6)                                 */
/* ------------------------------------------------------------------------- */

#define PINO_RC_ARENA 0
#define PINO_RC_IMMORTAL INT32_MIN

#define PINO_FLAG_ATOMIC (1u << 0)
#define PINO_FLAG_IMMORTAL (1u << 1)
#define PINO_FLAG_ARENA (1u << 2)
#define PINO_FLAG_CYCLE_CANDIDATE (1u << 3)
#define PINO_FLAG_ASCII (1u << 4) /* PinoStrBuf only: every byte of the buffer is ASCII */

typedef struct PinoRcHeader {
  int32_t rc; /* > 0 local count | == 0 arena | INT32_MIN immortal | < 0 atomic (-rc) */
  uint16_t flags;
  uint16_t type_id;
} PinoRcHeader;

/* Initializer for a static immortal object header. */
#define PINO_RC_IMMORTAL_HDR \
  { PINO_RC_IMMORTAL, PINO_FLAG_IMMORTAL, 0 }

#ifdef PINO_DEBUG_RC
/* Aborts with a plain diagnostic (exit code 103) on use-after-free,
 * double free, or a pointer that does not come from pino_alloc_at. */
void pino_debug_check_rc(const PinoRcHeader* h, const char* op);
#define PINO_DEBUG_CHECK(h, op) pino_debug_check_rc((h), (op))
#else
#define PINO_DEBUG_CHECK(h, op) ((void)0)
#endif

/* Atomic shims. They act on the negative "atomic" count: more references means
 * a more negative rc, so an atomic increment of the reference count DECREMENTS
 * rc, and the last release brings rc back to 0. Both return the new rc. */
#if defined(__TINYC__)
/* TCC 0.9.27 has no <stdatomic.h>, no __sync_* and no exported Interlocked*
 * functions on x86-64, so use an inline `lock xadd`. */
#if defined(__x86_64__) || defined(__i386__)
static inline int32_t pino_atomic_add_fetch(volatile int32_t* p, int32_t d) {
  int32_t r = d;
  __asm__ __volatile__("lock; xaddl %0, %1" : "+r"(r), "+m"(*p) : : "memory", "cc");
  return r + d;
}
#else
#error "pino_rt.h: TCC atomic shim is only implemented for x86 and x86-64"
#endif
#elif defined(__GNUC__) || defined(__clang__)
static inline int32_t pino_atomic_add_fetch(volatile int32_t* p, int32_t d) {
  return __atomic_add_fetch(p, d, __ATOMIC_ACQ_REL);
}
#else
#error "pino_rt.h: no atomic shim for this compiler"
#endif

static inline int32_t pino_atomic_inc(volatile int32_t* rc) { return pino_atomic_add_fetch(rc, -1); }
static inline int32_t pino_atomic_dec(volatile int32_t* rc) { return pino_atomic_add_fetch(rc, 1); }

/* Under -DPINO_STATS, rc_inc / rc_dec count actual reference-count operations
 * on heap RC objects (local rc > 0 or atomic rc < 0), skipping arena and
 * immortal singletons. Default builds emit zero extra instructions here. */
static inline void pino_rc_inc(PinoRcHeader* h) {
  PINO_DEBUG_CHECK(h, "pino_rc_inc");
  if (h->rc > 0) {
#ifdef PINO_STATS
    pino_stat_rc_inc_count++;
#endif
    h->rc++;
    return;
  }
  if (h->rc < 0 && h->rc != PINO_RC_IMMORTAL) {
#ifdef PINO_STATS
    pino_stat_rc_inc_count++;
#endif
    pino_atomic_inc(&h->rc);
  }
}

/* Returns true when the count reached zero and the caller must now drop the
 * object's children and release its memory. Arena and immortal objects never
 * reach zero here. */
static inline bool pino_rc_dec(PinoRcHeader* h) {
  PINO_DEBUG_CHECK(h, "pino_rc_dec");
  if (h->rc > 0) {
#ifdef PINO_STATS
    pino_stat_rc_dec_count++;
#endif
    return --h->rc == 0;
  }
  if (h->rc < 0 && h->rc != PINO_RC_IMMORTAL) {
#ifdef PINO_STATS
    pino_stat_rc_dec_count++;
#endif
    return pino_atomic_dec(&h->rc) == 0;
  }
  return false;
}

/* Allocates `size` bytes, writes an RC header with rc = 1 at the start, and (in
 * debug mode) records the object as an RC object. `size` includes the header. */
void* pino_rc_alloc_at(PinoAlloc* a, size_t size, uint16_t type_id, uint16_t flags, const char* type_name,
                       const char* file, int line);
#define PINO_RC_ALLOC(a, size, type_id, flags, type_name) \
  pino_rc_alloc_at((a), (size), (type_id), (flags), (type_name), __FILE__, __LINE__)

/* Stamps PINO_FLAG_CYCLE_CANDIDATE on an RC object (and, in debug mode, marks
 * its leak record so leaks are reported as CYCLE-LEAK). */
void pino_rc_mark_cycle_candidate(PinoRcHeader* h);

#ifdef PINO_DEBUG_RC
/* Debug-mode introspection used by tests and by the leak report. */
int64_t pino_debug_live_count(void);
int64_t pino_debug_leak_count(int64_t* cycle_leaks_out);
void pino_debug_leak_report(void);
void pino_debug_test_leak_report(void);
#endif

/* Set to true by the generated `pino test --c` harness so panics, asserts, and
 * leak reports emit structured records on stderr without changing normal runs. */
extern bool pino_test_mode;

/* ------------------------------------------------------------------------- */
/* Source locations and backtrace frames (design 7.3)                         */
/* ------------------------------------------------------------------------- */

typedef struct PinoLoc {
  const char* file;
  int32_t line;
  int32_t col;
} PinoLoc;

#define PINO_LOC(file, line, col) ((PinoLoc){(file), (int32_t)(line), (int32_t)(col)})
#define PINO_HERE PINO_LOC(__FILE__, __LINE__, 0)

typedef struct PinoFrame {
  struct PinoFrame* prev;
  const char* name;
} PinoFrame;

extern PinoFrame* pino_frame_top;

/* Use once per function: PINO_FRAME_PUSH("name"); ... PINO_FRAME_POP(); on every return. */
#define PINO_FRAME_PUSH(fn_name)                          \
  PinoFrame pino_frame_self_ = {pino_frame_top, (fn_name)}; \
  pino_frame_top = &pino_frame_self_
#define PINO_FRAME_POP() (pino_frame_top = pino_frame_self_.prev)

/* ------------------------------------------------------------------------- */
/* Core value types                                                           */
/* ------------------------------------------------------------------------- */

typedef struct PinoStrBuf {
  PinoRcHeader hdr;
  int64_t cap;  /* capacity in bytes, not counting the trailing NUL */
  int64_t used; /* used bytes currently sealed in data[0..used) */
  char data[];  /* UTF-8 bytes, always followed by a NUL byte after the used part */
} PinoStrBuf;

typedef struct PinoString {
  PinoStrBuf* owner; /* NULL for static literals and other non-refcounted memory */
  const char* ptr;   /* never NULL, may point into owner->data */
  int64_t byte_len;
} PinoString;

#define PINO_STR_LIT(s) ((PinoString){NULL, (s), (int64_t)(sizeof(s) - 1)})
#define PINO_STR_EMPTY ((PinoString){NULL, "", 0})

typedef struct PinoHandle {
  uint32_t index;
  uint32_t gen;
} PinoHandle;

typedef void (*PinoDropFn)(PinoAlloc* a, void* obj);

typedef struct PinoEnvHdr {
  PinoRcHeader hdr;
  PinoDropFn drop; /* called with the env itself when rc reaches 0; frees its memory */
} PinoEnvHdr;

typedef struct PinoClosure {
  void* fn_ptr;
  PinoEnvHdr* env; /* NULL when the closure captures nothing */
} PinoClosure;

typedef struct PinoIfaceBox {
  PinoRcHeader hdr;
} PinoIfaceBox;

/* Layout checks (C99 compatible, no _Static_assert). */
typedef char pino_assert_rc_header_is_8[(sizeof(PinoRcHeader) == 8) ? 1 : -1];
typedef char pino_assert_handle_is_8[(sizeof(PinoHandle) == 8) ? 1 : -1];
#if UINTPTR_MAX == 0xFFFFFFFFFFFFFFFFu
typedef char pino_assert_closure_is_16[(sizeof(PinoClosure) == 16) ? 1 : -1];
typedef char pino_assert_string_is_24[(sizeof(PinoString) == 24) ? 1 : -1];
#endif

/* Releases one reference to a closure environment. */
static inline void pino_env_release(PinoAlloc* a, PinoEnvHdr* env) {
  if (env != NULL && pino_rc_dec(&env->hdr)) {
    env->drop(a, env);
  }
}

/* ------------------------------------------------------------------------- */
/* Panics (exit code 101, ERR-3..5) with plain-English messages               */
/* ------------------------------------------------------------------------- */

/* Prints to stderr:
 *   thread 'main' panicked at '<message>'
 *     at <file>:<line>:<col>
 *     <n>: <frame name>          (one line per PinoFrame, innermost first)
 * and exits with code 101. */
PINO_NORETURN void pino_panic_at(PinoString msg, PinoLoc loc);
PINO_NORETURN void pino_assert_fail_at(PinoString msg, PinoLoc loc);
PINO_NORETURN void pino_panic_cstr_at(const char* msg, PinoLoc loc);

/* TYPE-E4: "Cannot divide by zero." */
PINO_NORETURN void pino_panic_div_zero(PinoLoc loc);
/* TYPE-E6 / D-186: "Integer division overflowed: the smallest 64-bit integer cannot be divided by -1." */
PINO_NORETURN void pino_panic_div_overflow(PinoLoc loc);
/* VEC-E4 / VECLIB-E2: "Index <i> is out of range for a vector of length <len>." */
PINO_NORETURN void pino_panic_vec_index(int64_t index, int64_t len, PinoLoc loc);
/* MAP-E1: "Key <key> was not found in map." (`key_text` is the already formatted key) */
PINO_NORETURN void pino_panic_map_key(PinoString key_text, PinoLoc loc);
/* FN-34 / D-184: "Cannot pass the same element of '<collection>' as two 'mut' arguments to '<target>'."
 * `target` is the unqualified function name, or NULL for a call through a function
 * value, which prints "this function" without quotes. */
PINO_NORETURN void pino_panic_aliased_mut(const char* collection, const char* target, PinoLoc loc);
/* POOL-E1 / D-185: "This handle no longer points to a value in '<pool>'; it was removed." */
PINO_NORETURN void pino_panic_stale_handle(const char* pool_name, PinoLoc loc);

/* Prints "[WARNING] <message>" to stderr (VEC-15 clamping warnings). */
void pino_warn_stderr(const char* msg);

/* ------------------------------------------------------------------------- */
/* Checked integer and float math (TYPE-E4, TYPE-E6, D-186)                   */
/* ------------------------------------------------------------------------- */

/* Floor division. Panics on a zero divisor and on INT64_MIN // -1. */
static inline int64_t pino_floordiv_i64(int64_t a, int64_t b, PinoLoc loc) {
  if (b == 0) {
    pino_panic_div_zero(loc);
  }
  if (b == -1) {
    if (a == INT64_MIN) {
      pino_panic_div_overflow(loc);
    }
    return -a;
  }
  int64_t q = a / b;
  if ((a % b != 0) && ((a < 0) != (b < 0))) {
    q--;
  }
  return q;
}

/* Floor modulo (result takes the sign of the divisor). Panics on a zero divisor
 * and on INT64_MIN % -1. */
static inline int64_t pino_floormod_i64(int64_t a, int64_t b, PinoLoc loc) {
  if (b == 0) {
    pino_panic_div_zero(loc);
  }
  if (b == -1) {
    if (a == INT64_MIN) {
      pino_panic_div_overflow(loc);
    }
    return 0;
  }
  int64_t r = a % b;
  if (r != 0 && ((r < 0) != (b < 0))) {
    r += b;
  }
  return r;
}

double pino_floor_f64(double x);
double pino_fmod_f64(double a, double b);

static inline double pino_div_f64(double a, double b, PinoLoc loc) {
  if (b == 0.0) {
    pino_panic_div_zero(loc);
  }
  return a / b;
}

static inline double pino_floordiv_f64(double a, double b, PinoLoc loc) {
  if (b == 0.0) {
    pino_panic_div_zero(loc);
  }
  return pino_floor_f64(a / b);
}

static inline double pino_floormod_f64(double a, double b, PinoLoc loc) {
  if (b == 0.0) {
    pino_panic_div_zero(loc);
  }
  double r = pino_fmod_f64(a, b);
  if (r != 0.0 && ((r < 0.0) != (b < 0.0))) {
    r += b;
  }
  return r;
}

/* ------------------------------------------------------------------------- */
/* Strings (pino_str.c, design 2.3)                                           */
/* ------------------------------------------------------------------------- */

/* Allocates a string buffer with room for `cap` bytes plus a NUL, rc = 1. */
PinoStrBuf* pino_strbuf_alloc(int64_t cap);
/* Finishes a buffer: writes the NUL after `len` bytes, caches the ASCII flag,
 * and returns the owned string covering data[0..len). */
PinoString pino_strbuf_seal(PinoStrBuf* buf, int64_t len);

PinoString pino_str_retain(PinoString s);  /* +1 on owner, returns s */
void pino_str_release(PinoString s);       /* -1 on owner, frees the buffer at 0 */
PinoString pino_str_from_bytes(const char* bytes, int64_t len); /* owned copy */

int64_t pino_str_rune_len(PinoString s);                              /* STRLIB-1 */
int32_t pino_str_get_rune(PinoString s, int64_t index, PinoLoc loc);  /* STRLIB-19, RUNE-8 */
PinoString pino_str_substring(PinoString s, int64_t start, int64_t len, PinoLoc loc); /* STRLIB-18, E1 */
int32_t pino_str_first_rune(PinoString s);                            /* 0 for the empty string */

PinoString pino_str_to_owned(PinoString s);   /* STRLIB-9 */
PinoString pino_str_clone(PinoString s);      /* STRLIB-9 */
PinoString pino_str_to_cstring(PinoString s); /* STRLIB-10: ptr[byte_len] == '\0' guaranteed */

PinoString pino_str_concat(PinoString a, PinoString b);
PinoString pino_str_append_owned(PinoString a, PinoString b); /* consumes a, borrows b */
PinoString pino_str_concat_n(const PinoString* parts, int64_t n);
PinoString pino_str_concat_rune(PinoString s, int32_t rune);   /* STR-11 */
PinoString pino_rune_concat_str(int32_t rune, PinoString s);   /* STR-12 */

PinoString pino_str_lower(PinoString s);
PinoString pino_str_upper(PinoString s);
PinoString pino_str_trim(PinoString s);        /* zero-copy */
PinoString pino_str_trim_start(PinoString s);  /* zero-copy */
PinoString pino_str_trim_end(PinoString s);    /* zero-copy */

bool pino_str_contains(PinoString s, PinoString sub);
bool pino_str_starts_with(PinoString s, PinoString prefix);
bool pino_str_ends_with(PinoString s, PinoString suffix);
int64_t pino_str_index_of(PinoString s, PinoString sub);       /* rune index or -1 */
int64_t pino_str_last_index_of(PinoString s, PinoString sub);  /* rune index or -1 */
PinoString pino_str_replace(PinoString s, PinoString old_sub, PinoString new_sub);

bool pino_str_eq(PinoString a, PinoString b);
int32_t pino_str_cmp(PinoString a, PinoString b);  /* -1, 0, 1 by code point (STR-10) */
uint64_t pino_str_hash(PinoString s);              /* FNV-1a 64-bit */

/* Value formatters (D-022, POOL-6). The result is owned unless noted. */
PinoString pino_i64_to_str(int64_t v);
PinoString pino_f64_to_str(double v);   /* whole floats keep ".0" */
PinoString pino_bool_to_str(bool v);    /* static literal, needs no release */
PinoString pino_rune_to_str(int32_t r);
PinoString pino_handle_to_str(PinoHandle h); /* "Handle(<index>, <gen>)" */

/* Parsers. Return true on success and store the value in *out. */
bool pino_parse_int(PinoString s, int64_t* out);
bool pino_parse_float(PinoString s, double* out);
PinoString pino_parse_int_error(PinoString input);
PinoString pino_parse_float_error(PinoString input);

/* UTF-8 helpers shared by the runtime and by generated code. */
int pino_utf8_encode(int32_t rune, char out[4]);                         /* bytes written (1..4) */
int32_t pino_utf8_decode(const char* p, const char* end, int* len_out);  /* U+FFFD on bad input */

/* Vector of strings, layout-compatible with the emitter's monomorphized []string. */
typedef struct PinoVec_string {
  PinoRcHeader hdr;
  int64_t len;
  int64_t cap;
  PinoString* data;
} PinoVec_string;

extern PinoVec_string pino_vec_string_empty; /* immortal singleton, 0 allocations */

PinoVec_string* pino_vec_string_new(int64_t cap);
/* Appends `s`, taking ownership of it. Copies the vector first when it is shared
 * or the immortal empty singleton, so *vp may change. */
void pino_vec_string_push(PinoVec_string** vp, PinoString s);
void pino_vec_string_release(PinoVec_string* v);

PinoVec_string* pino_str_split(PinoString s, PinoString sep);

/* ------------------------------------------------------------------------- */
/* I/O, system, regex (pino_io.c, design 5.9)                                 */
/* ------------------------------------------------------------------------- */

/* IOError union tags, in the order of PreludeSource.cs. */
enum {
  PINO_IOERR_NOT_FOUND = 0,
  PINO_IOERR_PERMISSION_DENIED = 1,
  PINO_IOERR_ALREADY_EXISTS = 2,
  PINO_IOERR_GREMLIN = 3
};

/* Result of read_file / write_file. On success `ok` is true and `value` is the
 * file content (read) or the written path (write). On failure `err_tag` is an
 * IOError tag and `value` is a plain-English message. `value` is owned. */
typedef struct PinoFileResult {
  bool ok;
  int32_t err_tag;
  PinoString value;
} PinoFileResult;

void pino_println_n(const PinoString* parts, int64_t n);
/* Prints `prompt` (when not empty), reads one line. Returns true with the owned
 * line in *out (CR and LF removed). At end of input or on a read error returns
 * false and *out is the static message "There is no more input to read." (D-030, BUILTIN-9). */
bool pino_readline(PinoString prompt, PinoString* out);
PinoFileResult pino_read_file(PinoString path);
PinoFileResult pino_write_file(PinoString path, PinoString content);
bool pino_file_exists(PinoString path);
int32_t pino_errno_to_ioerror_tag(int err);

double pino_time(void); /* Unix epoch milliseconds on every platform (BUILTIN-7) */
void pino_rand_seed(uint64_t seed);
double pino_rand_float(void);          /* [0.0, 1.0) */
int64_t pino_rand_int(int64_t limit);  /* [0, limit), 0 when limit <= 0 */
void pino_sleep(int64_t ms);
void pino_clear(void);

struct PinoReProg;

typedef struct PinoRegex {
  PinoRcHeader hdr;
  PinoString pattern; /* owned copy */
  struct PinoReProg* prog;
} PinoRegex;

/* Panics with RGX-E1 "Invalid pattern '<pattern>': <detail>." on a bad pattern. */
PinoRegex* pino_regex_compile(PinoString pattern, PinoLoc loc);
void pino_regex_release(PinoRegex* r);
PinoString pino_regex_pattern(const PinoRegex* r); /* owned (+1) */
bool pino_regex_has_match(const PinoRegex* r, PinoString text);
PinoString pino_regex_match_prefix(const PinoRegex* r, PinoString text);
PinoString pino_regex_find(const PinoRegex* r, PinoString text);
PinoVec_string* pino_regex_find_all(const PinoRegex* r, PinoString text);
PinoString pino_regex_replace(const PinoRegex* r, PinoString text, PinoString repl);

#ifdef __cplusplus
}
#endif

#endif /* PINO_RT_H */
