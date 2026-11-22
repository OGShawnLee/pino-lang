/*
 * test_runtime.c - standalone tests for runtime-re/ (ticket 064).
 *
 * Build (from the repository root), for example:
 *   tooling/tcc/tcc.exe -Iruntime-re -Iruntime runtime-re/tests/test_runtime.c \
 *       runtime-re/pino_alloc.c runtime-re/pino_str.c runtime-re/pino_io.c runtime/re.c -o test_runtime.exe
 * Add -DPINO_DEBUG_RC for the debug mode checks (poisoning, leak reports, use after free).
 *
 * Panic and leak behaviour is verified in child processes: the test program runs itself
 * again with `--child <case>` and checks the exit code and the text written to stderr.
 */
#include "../pino_rt.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifndef _WIN32
#include <sys/wait.h>
#endif

static int g_failures = 0;
static int g_checks = 0;
static const char* g_argv0 = NULL;

#define CHECK(cond)                                                       \
  do {                                                                    \
    g_checks++;                                                           \
    if (!(cond)) {                                                        \
      g_failures++;                                                       \
      printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond);              \
    }                                                                     \
  } while (0)

#define LIT(s) PINO_STR_LIT(s)

static bool str_is(PinoString s, const char* expected) {
  size_t n = strlen(expected);
  return (size_t)s.byte_len == n && memcmp(s.ptr, expected, n) == 0;
}

/* ------------------------------------------------------------------------- */
/* Child processes                                                            */
/* ------------------------------------------------------------------------- */

static int run_child(const char* name, char* err_text, size_t err_cap) {
  const char* tmp = getenv("TEMP");
  if (tmp == NULL) tmp = getenv("TMPDIR");
  if (tmp == NULL) tmp = "/tmp";
  char err_path[512];
  snprintf(err_path, sizeof err_path, "%s/pino_rt_child_%s.txt", tmp, name);
  char cmd[1536];
#ifdef _WIN32
  /* cmd.exe strips the outer pair of quotes, so wrap the whole command once more. */
  snprintf(cmd, sizeof cmd, "\"\"%s\" --child %s 2>\"%s\"\"", g_argv0, name, err_path);
#else
  snprintf(cmd, sizeof cmd, "\"%s\" --child %s 2>\"%s\"", g_argv0, name, err_path);
#endif
  int status = system(cmd);
  int code = status;
#ifndef _WIN32
  code = WIFEXITED(status) ? WEXITSTATUS(status) : -1;
#endif
  err_text[0] = '\0';
  FILE* f = fopen(err_path, "rb");
  if (f != NULL) {
    size_t n = fread(err_text, 1, err_cap - 1, f);
    err_text[n] = '\0';
    fclose(f);
    remove(err_path);
  }
  return code;
}

static bool contains(const char* text, const char* needle) { return strstr(text, needle) != NULL; }

/* Runs a child that must panic with exit code 101 and print `message` and a location. */
static void expect_panic(const char* name, const char* message) {
  char err[4096];
  int code = run_child(name, err, sizeof err);
  if (code != 101 || !contains(err, message) || !contains(err, "thread 'main' panicked at '") ||
      !contains(err, "  at ")) {
    printf("child '%s' exit=%d stderr=<<%s>>\n", name, code, err);
  }
  CHECK(code == 101);
  CHECK(contains(err, "thread 'main' panicked at '"));
  CHECK(contains(err, message));
  CHECK(contains(err, "  at "));
  CHECK(contains(err, "test_runtime.c:"));
}

static void inner_fn(void) {
  PINO_FRAME_PUSH("inner");
  pino_panic_cstr_at("boom", PINO_HERE);
  PINO_FRAME_POP();
}

static void outer_fn(void) {
  PINO_FRAME_PUSH("outer");
  inner_fn();
  PINO_FRAME_POP();
}

/* Child entry points. Each one is expected to terminate the process. */
static int child_main(const char* name) {
  PinoLoc loc = PINO_HERE;
  if (strcmp(name, "div_zero") == 0) {
    volatile int64_t z = 0;
    return (int)pino_floordiv_i64(5, z, loc);
  } else if (strcmp(name, "mod_zero") == 0) {
    volatile int64_t z = 0;
    return (int)pino_floormod_i64(5, z, loc);
  } else if (strcmp(name, "div_overflow") == 0) {
    volatile int64_t m = INT64_MIN;
    return (int)pino_floordiv_i64(m, -1, loc);
  } else if (strcmp(name, "mod_overflow") == 0) {
    volatile int64_t m = INT64_MIN;
    return (int)pino_floormod_i64(m, -1, loc);
  } else if (strcmp(name, "div_zero_f64") == 0) {
    volatile double z = 0.0;
    return (int)pino_div_f64(1.0, z, loc);
  } else if (strcmp(name, "substring") == 0) {
    pino_str_substring(LIT("hello"), 3, 5, loc);
  } else if (strcmp(name, "get_rune") == 0) {
    pino_str_get_rune(LIT("abc"), 3, loc);
  } else if (strcmp(name, "vec_index") == 0) {
    pino_panic_vec_index(5, 3, loc);
  } else if (strcmp(name, "map_key") == 0) {
    pino_panic_map_key(LIT("\"x\""), loc);
  } else if (strcmp(name, "alias") == 0) {
    pino_panic_aliased_mut("enemies", "duel", loc);
  } else if (strcmp(name, "alias_fn") == 0) {
    pino_panic_aliased_mut("enemies", NULL, loc);
  } else if (strcmp(name, "stale") == 0) {
    pino_panic_stale_handle("world", loc);
  } else if (strcmp(name, "regex_bad") == 0) {
    pino_regex_compile(LIT("["), loc);
  } else if (strcmp(name, "user_panic") == 0) {
    outer_fn();
#ifdef PINO_DEBUG_RC
  } else if (strcmp(name, "leak") == 0) {
    putenv("PINO_FAIL_ON_LEAK=1");
    void* p = PINO_ALLOC(pino_cur_alloc, 24, "intentionally leaked test block");
    (void)p;
    return 0; /* leaves through the normal exit path, so the atexit leak report runs */
  } else if (strcmp(name, "cycle_leak") == 0) {
    putenv("PINO_FAIL_ON_LEAK=1");
    PinoRcHeader* h = (PinoRcHeader*)PINO_RC_ALLOC(pino_cur_alloc, 16, 7, PINO_FLAG_CYCLE_CANDIDATE, "closure cell");
    (void)h;
    return 0;
  } else if (strcmp(name, "use_after_free") == 0) {
    PinoRcHeader* h = (PinoRcHeader*)PINO_RC_ALLOC(pino_cur_alloc, 16, 1, 0, "test object");
    if (pino_rc_dec(h)) {
      pino_dealloc(pino_cur_alloc, h, 16);
    }
    pino_rc_inc(h);
    return 0;
  } else if (strcmp(name, "double_release") == 0) {
    PinoRcHeader* h = (PinoRcHeader*)PINO_RC_ALLOC(pino_cur_alloc, 16, 1, 0, "test object");
    if (pino_rc_dec(h)) {
      pino_dealloc(pino_cur_alloc, h, 16);
    }
    pino_rc_dec(h);
    return 0;
  } else if (strcmp(name, "double_free") == 0) {
    void* p = PINO_ALLOC(pino_cur_alloc, 16, "test block");
    pino_dealloc(pino_cur_alloc, p, 16);
    pino_dealloc(pino_cur_alloc, p, 16);
    return 0;
#endif
  }
  return 0; /* reaching this means the expected panic did not happen */
}

/* ------------------------------------------------------------------------- */
/* Tests                                                                      */
/* ------------------------------------------------------------------------- */

static void test_layout(void) {
  CHECK(sizeof(PinoRcHeader) == 8);
  CHECK(sizeof(PinoHandle) == 8);
  CHECK(sizeof(PinoClosure) == 16);
  CHECK(sizeof(PinoString) == 24);
  CHECK(sizeof(PinoLoc) >= 16);
}

typedef struct {
  PinoRcHeader hdr;
  int64_t payload;
} TestObj;

static int g_counting_allocs = 0;
static int g_counting_frees = 0;

static void* counting_alloc(void* ctx, size_t size) {
  (void)ctx;
  g_counting_allocs++;
  return pino_sys_alloc.alloc(NULL, size);
}
static void* counting_realloc(void* ctx, void* p, size_t o, size_t n) {
  (void)ctx;
  return pino_sys_alloc.realloc(NULL, p, o, n);
}
static void counting_dealloc(void* ctx, void* p, size_t size) {
  (void)ctx;
  g_counting_frees++;
  pino_sys_alloc.dealloc(NULL, p, size);
}

static void test_rc_and_allocator(void) {
  /* regular count */
  TestObj* o = (TestObj*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(TestObj), 5, 0, "TestObj");
  CHECK(o->hdr.rc == 1 && o->hdr.type_id == 5);
  pino_rc_inc(&o->hdr);
  CHECK(o->hdr.rc == 2);
  CHECK(!pino_rc_dec(&o->hdr));
  CHECK(o->hdr.rc == 1);
  CHECK(pino_rc_dec(&o->hdr));
  CHECK(o->hdr.rc == 0);
  pino_dealloc(pino_cur_alloc, o, sizeof(TestObj));

  /* immortal singleton: never changes, never reaches zero */
  static TestObj immortal = {PINO_RC_IMMORTAL_HDR, 0};
  pino_rc_inc(&immortal.hdr);
  CHECK(immortal.hdr.rc == PINO_RC_IMMORTAL);
  CHECK(!pino_rc_dec(&immortal.hdr));
  CHECK(immortal.hdr.rc == PINO_RC_IMMORTAL);

  /* arena object: rc == 0 means no counting at all */
  TestObj arena = {{PINO_RC_ARENA, PINO_FLAG_ARENA, 0}, 0};
  pino_rc_inc(&arena.hdr);
  CHECK(arena.hdr.rc == 0);
  CHECK(!pino_rc_dec(&arena.hdr));
  CHECK(arena.hdr.rc == 0);

  /* atomic (negative) count: -rc is the number of references */
  TestObj* a = (TestObj*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(TestObj), 6, PINO_FLAG_ATOMIC, "TestObj");
  a->hdr.rc = -1;
  pino_rc_inc(&a->hdr);
  CHECK(a->hdr.rc == -2);
  CHECK(!pino_rc_dec(&a->hdr));
  CHECK(a->hdr.rc == -1);
  CHECK(pino_rc_dec(&a->hdr));
  CHECK(a->hdr.rc == 0);
  pino_dealloc(pino_cur_alloc, a, sizeof(TestObj));

  /* a pluggable allocator is really used, and the statistics follow */
  PinoAlloc counting = {counting_alloc, counting_realloc, counting_dealloc, NULL};
  uint64_t before_allocs = pino_stat_alloc_count;
  void* blk = PINO_ALLOC(&counting, 100, "counted block");
  CHECK(g_counting_allocs == 1);
  CHECK(pino_stat_alloc_count == before_allocs + 1);
  blk = PINO_REALLOC(&counting, blk, 100, 200, "counted block");
  pino_dealloc(&counting, blk, 200);
#ifndef PINO_DEBUG_RC
  CHECK(g_counting_frees >= 1); /* debug mode holds freed blocks in quarantine first */
#endif

  /* the runtime itself allocates through pino_cur_alloc */
  PinoAlloc* saved = pino_cur_alloc;
  int allocs_seen = g_counting_allocs;
  pino_cur_alloc = &counting;
  PinoString s = pino_str_from_bytes("through the plug", 16);
  CHECK(g_counting_allocs == allocs_seen + 1);
  pino_str_release(s);
  pino_cur_alloc = saved;
}

static void test_strings(void) {
  /* length in runes */
  CHECK(pino_str_rune_len(LIT("Hello")) == 5);
  CHECK(pino_str_rune_len(LIT("\xF0\x9F\x8C\xB2")) == 1); /* U+1F332 */
  CHECK(pino_str_rune_len(LIT("h\xC3\xA9llo\xF0\x9F\x8C\xB2")) == 6);
  CHECK(pino_str_rune_len(PINO_STR_EMPTY) == 0);

  /* rune indexing */
  PinoString mixed = LIT("a\xF0\x9F\x8C\xB2" "b");
  CHECK(pino_str_get_rune(mixed, 0, PINO_HERE) == 'a');
  CHECK(pino_str_get_rune(mixed, 1, PINO_HERE) == 0x1F332);
  CHECK(pino_str_get_rune(mixed, 2, PINO_HERE) == 'b');

  /* concatenation, owned buffers, ASCII fast path flag */
  PinoString hw = pino_str_concat(LIT("hello "), LIT("world"));
  CHECK(str_is(hw, "hello world"));
  CHECK(hw.owner != NULL && hw.owner->hdr.rc == 1);
  CHECK((hw.owner->hdr.flags & PINO_FLAG_ASCII) != 0);
  CHECK(hw.ptr[hw.byte_len] == '\0');

  /* O(1) zero-copy substring: no allocation, shared owner, rc + 1 */
  uint64_t allocs_before = pino_stat_alloc_count;
  PinoString sub = pino_str_substring(hw, 6, 5, PINO_HERE);
  CHECK(pino_stat_alloc_count == allocs_before);
  CHECK(str_is(sub, "world"));
  CHECK(sub.owner == hw.owner);
  CHECK(hw.owner->hdr.rc == 2);
  CHECK(sub.ptr == hw.ptr + 6);
  PinoString sub0 = pino_str_substring(hw, 0, 0, PINO_HERE);
  CHECK(sub0.byte_len == 0);
  CHECK(hw.owner->hdr.rc == 3);
  pino_str_release(sub0);
  pino_str_release(sub);
  CHECK(hw.owner->hdr.rc == 1);

  /* substring of a string with multi-byte runes */
  PinoString emoji = pino_str_concat(LIT("a\xF0\x9F\x8C\xB2" "b\xF0\x9F\x8C\xB2"), LIT("c"));
  CHECK(pino_str_rune_len(emoji) == 5);
  CHECK((emoji.owner->hdr.flags & PINO_FLAG_ASCII) == 0);
  allocs_before = pino_stat_alloc_count;
  PinoString mid = pino_str_substring(emoji, 1, 3, PINO_HERE);
  CHECK(pino_stat_alloc_count == allocs_before);
  CHECK(str_is(mid, "\xF0\x9F\x8C\xB2" "b\xF0\x9F\x8C\xB2"));
  PinoString tail = pino_str_substring(emoji, 4, 1, PINO_HERE);
  CHECK(str_is(tail, "c"));
  pino_str_release(tail);
  pino_str_release(mid);
  CHECK(pino_str_index_of(emoji, LIT("b")) == 2);
  CHECK(pino_str_last_index_of(emoji, LIT("\xF0\x9F\x8C\xB2")) == 3);
  pino_str_release(emoji);

  /* concat variants */
  PinoString parts[3] = {LIT("a"), PINO_STR_EMPTY, LIT("bc")};
  PinoString cn = pino_str_concat_n(parts, 3);
  CHECK(str_is(cn, "abc"));
  pino_str_release(cn);
  allocs_before = pino_stat_alloc_count;
  PinoString only = pino_str_concat(PINO_STR_EMPTY, hw); /* one non-empty part: shared, no copy */
  CHECK(pino_stat_alloc_count == allocs_before && only.owner == hw.owner);
  pino_str_release(only);
  PinoString cr = pino_str_concat_rune(LIT("x"), 0x1F332);
  CHECK(str_is(cr, "x\xF0\x9F\x8C\xB2"));
  pino_str_release(cr);
  PinoString rc = pino_rune_concat_str(0xE9, LIT("t"));
  CHECK(str_is(rc, "\xC3\xA9" "t"));
  pino_str_release(rc);

  /* to_owned / clone / to_cstring */
  PinoString own = pino_str_to_owned(hw);
  CHECK(pino_str_eq(own, hw) && own.owner != hw.owner);
  pino_str_release(own);
  PinoString cl = pino_str_clone(LIT("lit"));
  CHECK(str_is(cl, "lit") && cl.owner != NULL);
  pino_str_release(cl);
  PinoString lit_c = pino_str_to_cstring(LIT("abc"));
  CHECK(lit_c.owner == NULL && lit_c.ptr[lit_c.byte_len] == '\0');
  PinoString slice = pino_str_substring(hw, 0, 5, PINO_HERE); /* "hello", followed by ' ' */
  PinoString slice_c = pino_str_to_cstring(slice);
  CHECK(slice_c.owner != slice.owner && strcmp(slice_c.ptr, "hello") == 0);
  pino_str_release(slice_c);
  pino_str_release(slice);

  /* case mapping */
  PinoString lo = pino_str_lower(LIT("ABC \xC3\x80\xC3\x89 \xCE\x91 \xD0\x96"));
  CHECK(str_is(lo, "abc \xC3\xA0\xC3\xA9 \xCE\xB1 \xD0\xB6"));
  pino_str_release(lo);
  PinoString up = pino_str_upper(LIT("abc \xC3\xA0\xC3\xA9"));
  CHECK(str_is(up, "ABC \xC3\x80\xC3\x89"));
  pino_str_release(up);

  /* trim is zero-copy */
  PinoString padded = pino_str_concat(LIT("  \t padded text \r\n"), LIT("."));
  allocs_before = pino_stat_alloc_count;
  PinoString cut = pino_str_substring(padded, 0, 17, PINO_HERE);
  PinoString t = pino_str_trim(cut);
  CHECK(pino_stat_alloc_count == allocs_before);
  CHECK(str_is(t, "padded text"));
  CHECK(t.owner == padded.owner);
  pino_str_release(t);
  pino_str_release(cut);
  PinoString ts = pino_str_trim_start(padded);
  CHECK(str_is(ts, "padded text \r\n."));
  PinoString te = pino_str_trim_end(padded);
  CHECK(str_is(te, "  \t padded text \r\n."));
  pino_str_release(te);
  pino_str_release(ts);
  pino_str_release(padded);

  /* searching */
  PinoString text = LIT("one two one");
  CHECK(pino_str_contains(text, LIT("two")) && !pino_str_contains(text, LIT("three")));
  CHECK(pino_str_contains(text, PINO_STR_EMPTY));
  CHECK(pino_str_starts_with(text, LIT("one")) && !pino_str_starts_with(text, LIT("two")));
  CHECK(pino_str_ends_with(text, LIT("one")) && !pino_str_ends_with(text, LIT("two")));
  CHECK(pino_str_index_of(text, LIT("one")) == 0);
  CHECK(pino_str_index_of(text, LIT("zzz")) == -1);
  CHECK(pino_str_last_index_of(text, LIT("one")) == 8);
  CHECK(pino_str_last_index_of(LIT("aaa"), LIT("aa")) == 1);
  CHECK(pino_str_last_index_of(text, LIT("zzz")) == -1);

  /* replace */
  PinoString rep = pino_str_replace(text, LIT("one"), LIT("1"));
  CHECK(str_is(rep, "1 two 1"));
  pino_str_release(rep);
  PinoString none = pino_str_replace(hw, LIT("zzz"), LIT("y"));
  CHECK(none.owner == hw.owner);
  pino_str_release(none);
  PinoString del = pino_str_replace(LIT("a-b-c"), LIT("-"), PINO_STR_EMPTY);
  CHECK(str_is(del, "abc"));
  pino_str_release(del);

  /* split: parts are zero-copy slices */
  PinoString csv = pino_str_concat(LIT("a,b,,"), LIT("c"));
  allocs_before = pino_stat_alloc_count;
  PinoVec_string* sp = pino_str_split(csv, LIT(","));
  CHECK(sp->len == 4);
  CHECK(pino_stat_alloc_count == allocs_before + 2); /* vector header + element array only */
  CHECK(str_is(sp->data[0], "a") && str_is(sp->data[1], "b") && sp->data[2].byte_len == 0 && str_is(sp->data[3], "c"));
  CHECK(sp->data[0].owner == csv.owner);
  pino_vec_string_release(sp);
  CHECK(csv.owner->hdr.rc == 1);
  pino_str_release(csv);
  PinoVec_string* runes = pino_str_split(LIT("h\xC3\xA9y"), PINO_STR_EMPTY);
  CHECK(runes->len == 3 && str_is(runes->data[1], "\xC3\xA9"));
  pino_vec_string_release(runes);
  PinoVec_string* nothing = pino_str_split(PINO_STR_EMPTY, LIT(","));
  CHECK(nothing->len == 0 && nothing->hdr.rc == PINO_RC_IMMORTAL);
  pino_vec_string_release(nothing);

  /* equality, ordering, hash */
  PinoString abc_copy = pino_str_to_owned(LIT("abc"));
  CHECK(pino_str_eq(LIT("abc"), abc_copy));
  pino_str_release(abc_copy);
  CHECK(!pino_str_eq(LIT("abc"), LIT("abd")));
  CHECK(pino_str_cmp(LIT("a"), LIT("b")) == -1);
  CHECK(pino_str_cmp(LIT("b"), LIT("a")) == 1);
  CHECK(pino_str_cmp(LIT("ab"), LIT("ab")) == 0);
  CHECK(pino_str_cmp(LIT("ab"), LIT("abc")) == -1);
  CHECK(pino_str_cmp(LIT("\xF0\x9F\x8C\xB2"), LIT("z")) == 1); /* code point order */
  CHECK(pino_str_hash(PINO_STR_EMPTY) == 0xCBF29CE484222325ull);
  CHECK(pino_str_hash(LIT("a")) == 0xAF63DC4C8601EC8Cull);

  pino_str_release(hw);
}

static void test_formatting(void) {
  PinoString s;
  s = pino_i64_to_str(0);
  CHECK(str_is(s, "0"));
  pino_str_release(s);
  s = pino_i64_to_str(-42);
  CHECK(str_is(s, "-42"));
  pino_str_release(s);
  s = pino_i64_to_str(INT64_MIN);
  CHECK(str_is(s, "-9223372036854775808"));
  pino_str_release(s);
  s = pino_i64_to_str(INT64_MAX);
  CHECK(str_is(s, "9223372036854775807"));
  pino_str_release(s);

  s = pino_f64_to_str(1.0);
  CHECK(str_is(s, "1.0"));
  pino_str_release(s);
  s = pino_f64_to_str(42.0);
  CHECK(str_is(s, "42.0"));
  pino_str_release(s);
  s = pino_f64_to_str(-3.0);
  CHECK(str_is(s, "-3.0"));
  pino_str_release(s);
  s = pino_f64_to_str(3.14159);
  CHECK(str_is(s, "3.14159"));
  pino_str_release(s);
  s = pino_f64_to_str(0.1);
  CHECK(str_is(s, "0.1"));
  pino_str_release(s);
  s = pino_f64_to_str(-0.005);
  CHECK(str_is(s, "-0.005"));
  pino_str_release(s);

  uint64_t allocs = pino_stat_alloc_count;
  CHECK(str_is(pino_bool_to_str(true), "true"));
  CHECK(str_is(pino_bool_to_str(false), "false"));
  CHECK(pino_stat_alloc_count == allocs); /* static literals */

  s = pino_rune_to_str(0x1F332);
  CHECK(str_is(s, "\xF0\x9F\x8C\xB2"));
  pino_str_release(s);
  s = pino_rune_to_str('x');
  CHECK(str_is(s, "x"));
  pino_str_release(s);

  PinoHandle h = {3, 7};
  s = pino_handle_to_str(h);
  CHECK(str_is(s, "Handle(3, 7)"));
  pino_str_release(s);

  int64_t iv = 0;
  CHECK(pino_parse_int(LIT("42"), &iv) && iv == 42);
  CHECK(pino_parse_int(LIT("  -7 "), &iv) && iv == -7);
  CHECK(pino_parse_int(LIT("9223372036854775807"), &iv) && iv == INT64_MAX);
  CHECK(!pino_parse_int(LIT("9223372036854775808"), &iv));
  CHECK(!pino_parse_int(LIT("abc"), &iv));
  CHECK(!pino_parse_int(LIT("12abc"), &iv));
  CHECK(!pino_parse_int(LIT("1.5"), &iv));
  CHECK(!pino_parse_int(PINO_STR_EMPTY, &iv));
  double fv = 0.0;
  CHECK(pino_parse_float(LIT("3.5"), &fv) && fv == 3.5);
  CHECK(pino_parse_float(LIT("1e3"), &fv) && fv == 1000.0);
  CHECK(pino_parse_float(LIT(" -0.25 "), &fv) && fv == -0.25);
  CHECK(!pino_parse_float(LIT("abc"), &fv));
  CHECK(!pino_parse_float(LIT("1.5x"), &fv));
  CHECK(!pino_parse_float(LIT("inf"), &fv));
  CHECK(!pino_parse_float(PINO_STR_EMPTY, &fv));

  CHECK(pino_str_first_rune(LIT("\xF0\x9F\x8C\xB2yz")) == 0x1F332);
  CHECK(pino_str_first_rune(LIT("A")) == 'A');
}

static void test_regex(void) {
  PinoRegex* r = pino_regex_compile(LIT("[0-9]+"), PINO_HERE);
  CHECK(r->hdr.rc == 1);
  PinoString pat = pino_regex_pattern(r);
  CHECK(str_is(pat, "[0-9]+"));
  pino_str_release(pat);

  PinoString text = pino_str_concat(LIT("a1b22c"), LIT("333"));
  CHECK(pino_regex_has_match(r, text));
  CHECK(!pino_regex_has_match(r, LIT("abc")));

  PinoString found = pino_regex_find(r, text);
  CHECK(str_is(found, "1") && found.owner == text.owner);
  pino_str_release(found);
  PinoString none = pino_regex_find(r, LIT("abc"));
  CHECK(none.byte_len == 0);

  PinoString pre = pino_regex_match_prefix(r, LIT("123abc"));
  CHECK(str_is(pre, "123"));
  pino_str_release(pre);
  PinoString nopre = pino_regex_match_prefix(r, LIT("abc123"));
  CHECK(nopre.byte_len == 0);

  PinoVec_string* all = pino_regex_find_all(r, text);
  CHECK(all->len == 3 && str_is(all->data[0], "1") && str_is(all->data[1], "22") && str_is(all->data[2], "333"));
  pino_vec_string_release(all);
  PinoVec_string* none_all = pino_regex_find_all(r, LIT("abc"));
  CHECK(none_all->len == 0);
  pino_vec_string_release(none_all);

  PinoString rep = pino_regex_replace(r, text, LIT("#"));
  CHECK(str_is(rep, "a#b#c#"));
  pino_str_release(rep);
  PinoString same = pino_regex_replace(r, LIT("abc"), LIT("#"));
  CHECK(str_is(same, "abc"));
  pino_str_release(same);

  pino_str_release(text);
  pino_regex_release(r);

  PinoRegex* anchored = pino_regex_compile(LIT("^ab"), PINO_HERE);
  CHECK(pino_regex_has_match(anchored, LIT("abc")));
  CHECK(!pino_regex_has_match(anchored, LIT("cab")));
  pino_regex_release(anchored);
}

static void test_math(void) {
  PinoLoc loc = PINO_HERE;
  CHECK(pino_floordiv_i64(7, 2, loc) == 3);
  CHECK(pino_floordiv_i64(-7, 2, loc) == -4);
  CHECK(pino_floordiv_i64(7, -2, loc) == -4);
  CHECK(pino_floordiv_i64(-7, -2, loc) == 3);
  CHECK(pino_floordiv_i64(6, 3, loc) == 2);
  CHECK(pino_floordiv_i64(-6, 3, loc) == -2);
  CHECK(pino_floordiv_i64(INT64_MIN, 1, loc) == INT64_MIN);
  CHECK(pino_floordiv_i64(5, -1, loc) == -5);
  CHECK(pino_floormod_i64(7, 3, loc) == 1);
  CHECK(pino_floormod_i64(-7, 3, loc) == 2);
  CHECK(pino_floormod_i64(7, -3, loc) == -2);
  CHECK(pino_floormod_i64(-7, -3, loc) == -1);
  CHECK(pino_floormod_i64(INT64_MIN, 2, loc) == 0);
  CHECK(pino_floormod_i64(5, -1, loc) == 0);
  CHECK(pino_div_f64(1.0, 4.0, loc) == 0.25);
  CHECK(pino_floordiv_f64(7.0, 2.0, loc) == 3.0);
  CHECK(pino_floordiv_f64(-7.0, 2.0, loc) == -4.0);
  CHECK(pino_floormod_f64(-7.0, 3.0, loc) == 2.0);
  CHECK(pino_floormod_f64(7.5, -2.0, loc) == -0.5);
}

static void test_system(void) {
  double t = pino_time();
  CHECK(t > 1700000000000.0);
  CHECK(t < 4000000000000.0);

  pino_rand_seed(12345);
  int in_range = 1;
  int saw_low = 0;
  int saw_high = 0;
  for (int i = 0; i < 2000; i++) {
    double f = pino_rand_float();
    if (!(f >= 0.0 && f < 1.0)) in_range = 0;
    int64_t n = pino_rand_int(10);
    if (n < 0 || n >= 10) in_range = 0;
    if (n == 0) saw_low = 1;
    if (n == 9) saw_high = 1;
  }
  CHECK(in_range && saw_low && saw_high);
  CHECK(pino_rand_int(0) == 0 && pino_rand_int(-5) == 0 && pino_rand_int(1) == 0);

  pino_sleep(0);
  pino_sleep(1);
}

static void test_files(void) {
  const char* tmp = getenv("TEMP");
  if (tmp == NULL) tmp = getenv("TMPDIR");
  if (tmp == NULL) tmp = "/tmp";
  char path[512];
  snprintf(path, sizeof path, "%s/pino_rt_file_test.txt", tmp);
  PinoString p = pino_str_from_bytes(path, (int64_t)strlen(path));

  PinoFileResult w = pino_write_file(p, LIT("line one\nh\xC3\xA9llo \xF0\x9F\x8C\xB2\n"));
  CHECK(w.ok && pino_str_eq(w.value, p));
  pino_str_release(w.value);
  CHECK(pino_file_exists(p));

  PinoFileResult r = pino_read_file(p);
  CHECK(r.ok && str_is(r.value, "line one\nh\xC3\xA9llo \xF0\x9F\x8C\xB2\n"));
  CHECK(pino_str_rune_len(r.value) == 17);
  pino_str_release(r.value);

  remove(path);
  CHECK(!pino_file_exists(p));
  PinoFileResult missing = pino_read_file(p);
  CHECK(!missing.ok && missing.err_tag == PINO_IOERR_NOT_FOUND && pino_str_eq(missing.value, p));
  pino_str_release(missing.value);

  CHECK(pino_errno_to_ioerror_tag(2) == PINO_IOERR_NOT_FOUND);       /* ENOENT */
  CHECK(pino_errno_to_ioerror_tag(13) == PINO_IOERR_PERMISSION_DENIED); /* EACCES */
  CHECK(pino_errno_to_ioerror_tag(1) == PINO_IOERR_PERMISSION_DENIED);  /* EPERM */
  CHECK(pino_errno_to_ioerror_tag(17) == PINO_IOERR_ALREADY_EXISTS);    /* EEXIST */
  CHECK(pino_errno_to_ioerror_tag(5) == PINO_IOERR_GREMLIN);

  PinoFileResult bad_dir = pino_write_file(LIT("/definitely/not/a/real/dir/file.txt"), LIT("x"));
  CHECK(!bad_dir.ok);
  pino_str_release(bad_dir.value);
  pino_str_release(p);
}

static void test_panics(void) {
  expect_panic("div_zero", "Cannot divide by zero.");
  expect_panic("mod_zero", "Cannot divide by zero.");
  expect_panic("div_zero_f64", "Cannot divide by zero.");
  expect_panic("div_overflow",
               "Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.");
  expect_panic("mod_overflow",
               "Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.");
  expect_panic("substring",
               "Substring starting at 3 with length 5 is out of range for a string of length 5.");
  expect_panic("get_rune", "Index 3 is out of range for a string of length 3.");
  expect_panic("vec_index", "Index 5 out of range for vector of length 3.");
  expect_panic("map_key", "Key \"x\" not found in map.");
  expect_panic("alias", "Cannot pass the same element of 'enemies' as two 'mut' arguments to 'duel'.");
  expect_panic("alias_fn", "Cannot pass the same element of 'enemies' as two 'mut' arguments to this function.");
  expect_panic("stale", "This handle no longer points to a value in 'world'; it was removed.");
  expect_panic("regex_bad", "Invalid pattern '[': No closing bracket.");
  expect_panic("user_panic", "boom");

  char err[4096];
  int code = run_child("user_panic", err, sizeof err);
  CHECK(code == 101);
  CHECK(contains(err, "0: inner"));
  CHECK(contains(err, "1: outer"));
}

#ifdef PINO_DEBUG_RC
static void test_debug_mode(void) {
  int64_t live_before = pino_debug_live_count();
  void* p = PINO_ALLOC(pino_cur_alloc, 40, "debug test block");
  CHECK(pino_debug_live_count() == live_before + 1);
  int64_t cycles = 0;
  CHECK(pino_debug_leak_count(&cycles) == live_before + 1);
  memset(p, 0x11, 40);
  pino_dealloc(pino_cur_alloc, p, 40);
  CHECK(pino_debug_live_count() == live_before);

  /* the freed block is poisoned with 0xDD and marked with 0xDEADFACE (still readable in quarantine) */
  const unsigned char* bytes = (const unsigned char*)p;
  int all_poisoned = 1;
  for (int i = 0; i < 40; i++) {
    if (bytes[i] != 0xDD) all_poisoned = 0;
  }
  CHECK(all_poisoned);
  uint32_t magic = 0;
  memcpy(&magic, bytes - 64, sizeof magic);
  CHECK(magic == 0xDEADFACEu);

  /* a cycle candidate is counted separately */
  PinoRcHeader* cell = (PinoRcHeader*)PINO_RC_ALLOC(pino_cur_alloc, 16, 3, 0, "closure cell");
  pino_rc_mark_cycle_candidate(cell);
  CHECK((cell->flags & PINO_FLAG_CYCLE_CANDIDATE) != 0);
  int64_t plain = pino_debug_leak_count(&cycles);
  CHECK(cycles == 1 && plain == live_before);
  CHECK(pino_rc_dec(cell));
  pino_dealloc(pino_cur_alloc, cell, 16);
  CHECK(pino_debug_live_count() == live_before);

  /* child processes: leak report, cycle leak, use after free, double free */
  char err[4096];
  int code = run_child("leak", err, sizeof err);
  if (code != 102) printf("leak child exit=%d stderr=<<%s>>\n", code, err);
  CHECK(code == 102);
  CHECK(contains(err, "LEAK: intentionally leaked test block"));
  CHECK(!contains(err, "CYCLE-LEAK"));
  CHECK(contains(err, "test_runtime.c:"));

  code = run_child("cycle_leak", err, sizeof err);
  CHECK(code == 0); /* closure cycles are reported but do not fail the run */
  CHECK(contains(err, "CYCLE-LEAK: closure cell rc=1"));

  code = run_child("use_after_free", err, sizeof err);
  CHECK(code == 103);
  CHECK(contains(err, "already freed"));
  code = run_child("double_release", err, sizeof err);
  CHECK(code == 103);
  CHECK(contains(err, "already freed"));
  code = run_child("double_free", err, sizeof err);
  CHECK(code == 103);
  CHECK(contains(err, "double free"));
}
#endif

int main(int argc, char** argv) {
  g_argv0 = argv[0];
  if (argc >= 3 && strcmp(argv[1], "--child") == 0) {
    return child_main(argv[2]);
  }

  test_layout();
  test_rc_and_allocator();
  test_strings();
  test_formatting();
  test_regex();
  test_math();
  test_system();
  test_files();
  test_panics();
#ifdef PINO_DEBUG_RC
  test_debug_mode();
  CHECK(pino_debug_live_count() == 0);
#endif
  CHECK(pino_stat_alloc_count == pino_stat_dealloc_count); /* nothing leaked in normal mode either */

  if (g_failures == 0) {
    printf("OK: %d checks passed.\n", g_checks);
    return 0;
  }
  printf("FAILED: %d of %d checks failed.\n", g_failures, g_checks);
  return 1;
}
