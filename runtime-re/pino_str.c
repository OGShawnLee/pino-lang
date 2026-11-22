/*
 * pino_str.c - reference-counted, zero-copy UTF-8 strings (design 056 2.3, 5.9).
 *
 * A PinoString is { owner, ptr, byte_len }. `owner` is the refcounted buffer that
 * keeps the bytes alive, or NULL for static literals. Slices (substring, trim,
 * split parts, regex matches) share the owner and cost one rc increment, never an
 * allocation. Every byte range is followed by a readable byte, so ptr[byte_len]
 * can always be read (it is the NUL for whole buffers and literals).
 *
 * Buffers record "all bytes are ASCII" in hdr.flags (PINO_FLAG_ASCII) when they
 * are sealed. For those, rune length and rune indexing are O(1).
 */
#include "pino_rt.h"

#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* ------------------------------------------------------------------------- */
/* Buffers, retain / release                                                  */
/* ------------------------------------------------------------------------- */

static size_t strbuf_total_size(int64_t cap) { return sizeof(PinoStrBuf) + (size_t)cap + 1; }

PinoStrBuf* pino_strbuf_alloc(int64_t cap) {
  if (cap < 0) {
    cap = 0;
  }
  PinoStrBuf* b = (PinoStrBuf*)PINO_RC_ALLOC(pino_cur_alloc, strbuf_total_size(cap), 0, 0, "string buffer");
  b->cap = cap;
  b->data[0] = '\0';
  return b;
}

PinoString pino_strbuf_seal(PinoStrBuf* buf, int64_t len) {
  buf->data[len] = '\0';
  bool ascii = true;
  const unsigned char* p = (const unsigned char*)buf->data;
  for (int64_t i = 0; i < len; i++) {
    if (p[i] >= 0x80) {
      ascii = false;
      break;
    }
  }
  if (ascii) {
    buf->hdr.flags |= PINO_FLAG_ASCII;
  }
  PinoString s = {buf, buf->data, len};
  return s;
}

PinoString pino_str_retain(PinoString s) {
  if (s.owner != NULL) {
    pino_rc_inc(&s.owner->hdr);
  }
  return s;
}

void pino_str_release(PinoString s) {
  if (s.owner != NULL && pino_rc_dec(&s.owner->hdr)) {
    pino_dealloc(pino_cur_alloc, s.owner, strbuf_total_size(s.owner->cap));
  }
}

PinoString pino_str_from_bytes(const char* bytes, int64_t len) {
  if (len <= 0) {
    return PINO_STR_EMPTY;
  }
  PinoStrBuf* b = pino_strbuf_alloc(len);
  memcpy(b->data, bytes, (size_t)len);
  return pino_strbuf_seal(b, len);
}

/* ------------------------------------------------------------------------- */
/* UTF-8                                                                      */
/* ------------------------------------------------------------------------- */

int pino_utf8_encode(int32_t rune, char out[4]) {
  uint32_t r = (uint32_t)rune;
  if (rune < 0 || r > 0x10FFFFu || (r >= 0xD800u && r <= 0xDFFFu)) {
    r = 0xFFFDu;
  }
  if (r < 0x80u) {
    out[0] = (char)r;
    return 1;
  }
  if (r < 0x800u) {
    out[0] = (char)(0xC0u | (r >> 6));
    out[1] = (char)(0x80u | (r & 0x3Fu));
    return 2;
  }
  if (r < 0x10000u) {
    out[0] = (char)(0xE0u | (r >> 12));
    out[1] = (char)(0x80u | ((r >> 6) & 0x3Fu));
    out[2] = (char)(0x80u | (r & 0x3Fu));
    return 3;
  }
  out[0] = (char)(0xF0u | (r >> 18));
  out[1] = (char)(0x80u | ((r >> 12) & 0x3Fu));
  out[2] = (char)(0x80u | ((r >> 6) & 0x3Fu));
  out[3] = (char)(0x80u | (r & 0x3Fu));
  return 4;
}

static bool is_cont(const char* p, const char* end) { return p < end && (((unsigned char)*p) & 0xC0u) == 0x80u; }

int32_t pino_utf8_decode(const char* p, const char* end, int* len_out) {
  const unsigned char c = (unsigned char)*p;
  int n = 1;
  int32_t r = 0xFFFD;
  if (c < 0x80u) {
    r = c;
  } else if (c >= 0xC2u && c <= 0xDFu && is_cont(p + 1, end)) {
    r = (int32_t)(((c & 0x1Fu) << 6) | ((unsigned char)p[1] & 0x3Fu));
    n = 2;
  } else if (c >= 0xE0u && c <= 0xEFu && is_cont(p + 1, end) && is_cont(p + 2, end)) {
    int32_t v = (int32_t)(((c & 0x0Fu) << 12) | (((unsigned char)p[1] & 0x3Fu) << 6) | ((unsigned char)p[2] & 0x3Fu));
    if (v >= 0x800 && !(v >= 0xD800 && v <= 0xDFFF)) {
      r = v;
      n = 3;
    }
  } else if (c >= 0xF0u && c <= 0xF4u && is_cont(p + 1, end) && is_cont(p + 2, end) && is_cont(p + 3, end)) {
    int32_t v = (int32_t)(((c & 0x07u) << 18) | (((unsigned char)p[1] & 0x3Fu) << 12) |
                          (((unsigned char)p[2] & 0x3Fu) << 6) | ((unsigned char)p[3] & 0x3Fu));
    if (v >= 0x10000 && v <= 0x10FFFF) {
      r = v;
      n = 4;
    }
  }
  *len_out = n;
  return r;
}

/* Number of runes in bytes [p, p+len): counts every byte that is not a continuation byte. */
static int64_t count_runes(const char* p, int64_t len) {
  int64_t n = 0;
  for (int64_t i = 0; i < len; i++) {
    if ((((unsigned char)p[i]) & 0xC0u) != 0x80u) {
      n++;
    }
  }
  return n;
}

static bool is_known_ascii(PinoString s) { return s.owner != NULL && (s.owner->hdr.flags & PINO_FLAG_ASCII) != 0; }

/* Byte offset of the start of rune number `rune_index` (0 <= rune_index <= rune count). */
static int64_t rune_to_byte_offset(PinoString s, int64_t rune_index) {
  if (is_known_ascii(s)) {
    return rune_index;
  }
  int64_t i = 0;
  int64_t seen = 0;
  while (i < s.byte_len) {
    if ((((unsigned char)s.ptr[i]) & 0xC0u) != 0x80u) {
      if (seen == rune_index) {
        return i;
      }
      seen++;
    }
    i++;
  }
  return s.byte_len;
}

int64_t pino_str_rune_len(PinoString s) {
  if (is_known_ascii(s)) {
    return s.byte_len;
  }
  return count_runes(s.ptr, s.byte_len);
}

int32_t pino_str_get_rune(PinoString s, int64_t index, PinoLoc loc) {
  int64_t n = pino_str_rune_len(s);
  if (index < 0 || index >= n) {
    char msg[160];
    snprintf(msg, sizeof msg, "Index %lld is out of range for a string of length %lld.", (long long)index,
             (long long)n);
    pino_panic_cstr_at(msg, loc);
  }
  if (is_known_ascii(s)) {
    return (unsigned char)s.ptr[index];
  }
  int64_t off = rune_to_byte_offset(s, index);
  int used = 0;
  return pino_utf8_decode(s.ptr + off, s.ptr + s.byte_len, &used);
}

PinoString pino_str_substring(PinoString s, int64_t start, int64_t len, PinoLoc loc) {
  int64_t n = pino_str_rune_len(s);
  if (start < 0 || len < 0 || start > n || len > n - start) {
    char msg[200];
    snprintf(msg, sizeof msg,
             "Substring starting at %lld with length %lld is out of range for a string of length %lld.",
             (long long)start, (long long)len, (long long)n);
    pino_panic_cstr_at(msg, loc);
  }
  int64_t b0 = rune_to_byte_offset(s, start);
  int64_t b1 = (start + len == n) ? s.byte_len : rune_to_byte_offset(s, start + len);
  PinoString r = {s.owner, s.ptr + b0, b1 - b0};
  return pino_str_retain(r);
}

int32_t pino_str_first_rune(PinoString s) {
  if (s.byte_len <= 0) {
    return 0;
  }
  int used = 0;
  return pino_utf8_decode(s.ptr, s.ptr + s.byte_len, &used);
}

/* ------------------------------------------------------------------------- */
/* Copies and concatenation                                                   */
/* ------------------------------------------------------------------------- */

PinoString pino_str_to_owned(PinoString s) { return pino_str_from_bytes(s.ptr, s.byte_len); }
PinoString pino_str_clone(PinoString s) { return pino_str_to_owned(s); }

PinoString pino_str_to_cstring(PinoString s) {
  if (s.ptr[s.byte_len] == '\0') {
    return pino_str_retain(s);
  }
  return pino_str_to_owned(s);
}

PinoString pino_str_concat_n(const PinoString* parts, int64_t n) {
  int64_t total = 0;
  int64_t non_empty = 0;
  int64_t last = -1;
  for (int64_t i = 0; i < n; i++) {
    if (parts[i].byte_len > 0) {
      total += parts[i].byte_len;
      non_empty++;
      last = i;
    }
  }
  if (non_empty == 0) {
    return PINO_STR_EMPTY;
  }
  if (non_empty == 1) {
    return pino_str_retain(parts[last]);
  }
  PinoStrBuf* b = pino_strbuf_alloc(total);
  char* dst = b->data;
  for (int64_t i = 0; i < n; i++) {
    if (parts[i].byte_len > 0) {
      memcpy(dst, parts[i].ptr, (size_t)parts[i].byte_len);
      dst += parts[i].byte_len;
    }
  }
  return pino_strbuf_seal(b, total);
}

PinoString pino_str_concat(PinoString a, PinoString b) {
  PinoString parts[2];
  parts[0] = a;
  parts[1] = b;
  return pino_str_concat_n(parts, 2);
}

PinoString pino_str_concat_rune(PinoString s, int32_t rune) {
  char enc[4];
  int n = pino_utf8_encode(rune, enc);
  PinoStrBuf* b = pino_strbuf_alloc(s.byte_len + n);
  memcpy(b->data, s.ptr, (size_t)s.byte_len);
  memcpy(b->data + s.byte_len, enc, (size_t)n);
  return pino_strbuf_seal(b, s.byte_len + n);
}

PinoString pino_rune_concat_str(int32_t rune, PinoString s) {
  char enc[4];
  int n = pino_utf8_encode(rune, enc);
  PinoStrBuf* b = pino_strbuf_alloc(s.byte_len + n);
  memcpy(b->data, enc, (size_t)n);
  memcpy(b->data + n, s.ptr, (size_t)s.byte_len);
  return pino_strbuf_seal(b, s.byte_len + n);
}

/* ------------------------------------------------------------------------- */
/* Case mapping and trimming                                                  */
/* ------------------------------------------------------------------------- */

/* ASCII plus the Latin-1 letters, basic Greek and Cyrillic, which keep their UTF-8 byte length. */
static int32_t rune_lower(int32_t r) {
  if (r >= 'A' && r <= 'Z') return r + 32;
  if (r >= 0xC0 && r <= 0xDE && r != 0xD7) return r + 0x20;
  if (r >= 0x391 && r <= 0x3A9 && r != 0x3A2) return r + 0x20;
  if (r >= 0x410 && r <= 0x42F) return r + 0x20;
  if (r >= 0x400 && r <= 0x40F) return r + 0x50;
  return r;
}

static int32_t rune_upper(int32_t r) {
  if (r >= 'a' && r <= 'z') return r - 32;
  if (r >= 0xE0 && r <= 0xFE && r != 0xF7) return r - 0x20;
  if (r >= 0x3B1 && r <= 0x3C9 && r != 0x3C2) return r - 0x20;
  if (r >= 0x430 && r <= 0x44F) return r - 0x20;
  if (r >= 0x450 && r <= 0x45F) return r - 0x50;
  return r;
}

static PinoString map_case(PinoString s, int32_t (*fn)(int32_t)) {
  if (s.byte_len <= 0) {
    return PINO_STR_EMPTY;
  }
  PinoStrBuf* b = pino_strbuf_alloc(s.byte_len);
  const char* p = s.ptr;
  const char* end = s.ptr + s.byte_len;
  char* dst = b->data;
  while (p < end) {
    int used = 0;
    int32_t r = pino_utf8_decode(p, end, &used);
    if (r == 0xFFFD && used == 1 && (unsigned char)*p >= 0x80u) {
      *dst++ = *p++; /* keep invalid bytes unchanged */
      continue;
    }
    char enc[4];
    int n = pino_utf8_encode(fn(r), enc);
    if (n != used) { /* cannot happen for the supported ranges; keep the original */
      memcpy(dst, p, (size_t)used);
      dst += used;
    } else {
      memcpy(dst, enc, (size_t)n);
      dst += n;
    }
    p += used;
  }
  return pino_strbuf_seal(b, s.byte_len);
}

PinoString pino_str_lower(PinoString s) { return map_case(s, rune_lower); }
PinoString pino_str_upper(PinoString s) { return map_case(s, rune_upper); }

static bool is_space(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\v' || c == '\f'; }

PinoString pino_str_trim_start(PinoString s) {
  const char* p = s.ptr;
  int64_t len = s.byte_len;
  while (len > 0 && is_space(*p)) {
    p++;
    len--;
  }
  PinoString r = {s.owner, p, len};
  return pino_str_retain(r);
}

PinoString pino_str_trim_end(PinoString s) {
  int64_t len = s.byte_len;
  while (len > 0 && is_space(s.ptr[len - 1])) {
    len--;
  }
  PinoString r = {s.owner, s.ptr, len};
  return pino_str_retain(r);
}

PinoString pino_str_trim(PinoString s) {
  const char* p = s.ptr;
  int64_t len = s.byte_len;
  while (len > 0 && is_space(*p)) {
    p++;
    len--;
  }
  while (len > 0 && is_space(p[len - 1])) {
    len--;
  }
  PinoString r = {s.owner, p, len};
  return pino_str_retain(r);
}

/* ------------------------------------------------------------------------- */
/* Searching                                                                  */
/* ------------------------------------------------------------------------- */

static const char* find_bytes(const char* h, int64_t hl, const char* n, int64_t nl) {
  if (nl == 0) return h;
  if (hl < nl) return NULL;
  const char* last = h + (hl - nl);
  for (const char* p = h; p <= last; p++) {
    if (*p == *n && memcmp(p, n, (size_t)nl) == 0) return p;
  }
  return NULL;
}

bool pino_str_contains(PinoString s, PinoString sub) {
  return find_bytes(s.ptr, s.byte_len, sub.ptr, sub.byte_len) != NULL;
}

bool pino_str_starts_with(PinoString s, PinoString prefix) {
  return prefix.byte_len <= s.byte_len && memcmp(s.ptr, prefix.ptr, (size_t)prefix.byte_len) == 0;
}

bool pino_str_ends_with(PinoString s, PinoString suffix) {
  return suffix.byte_len <= s.byte_len &&
         memcmp(s.ptr + (s.byte_len - suffix.byte_len), suffix.ptr, (size_t)suffix.byte_len) == 0;
}

int64_t pino_str_index_of(PinoString s, PinoString sub) {
  if (sub.byte_len == 0) return 0;
  const char* pos = find_bytes(s.ptr, s.byte_len, sub.ptr, sub.byte_len);
  if (pos == NULL) return -1;
  if (is_known_ascii(s)) return pos - s.ptr;
  return count_runes(s.ptr, pos - s.ptr);
}

int64_t pino_str_last_index_of(PinoString s, PinoString sub) {
  if (sub.byte_len == 0) return pino_str_rune_len(s);
  if (sub.byte_len > s.byte_len) return -1;
  for (int64_t i = s.byte_len - sub.byte_len; i >= 0; i--) {
    if (s.ptr[i] == sub.ptr[0] && memcmp(s.ptr + i, sub.ptr, (size_t)sub.byte_len) == 0) {
      return is_known_ascii(s) ? i : count_runes(s.ptr, i);
    }
  }
  return -1;
}

PinoString pino_str_replace(PinoString s, PinoString old_sub, PinoString new_sub) {
  if (s.byte_len == 0 || old_sub.byte_len == 0) {
    return pino_str_retain(s);
  }
  int64_t count = 0;
  const char* cur = s.ptr;
  int64_t rem = s.byte_len;
  for (;;) {
    const char* pos = find_bytes(cur, rem, old_sub.ptr, old_sub.byte_len);
    if (pos == NULL) break;
    count++;
    int64_t adv = (pos - cur) + old_sub.byte_len;
    cur += adv;
    rem -= adv;
  }
  if (count == 0) {
    return pino_str_retain(s);
  }
  int64_t total = s.byte_len + count * (new_sub.byte_len - old_sub.byte_len);
  if (total <= 0) {
    return PINO_STR_EMPTY;
  }
  PinoStrBuf* b = pino_strbuf_alloc(total);
  char* dst = b->data;
  cur = s.ptr;
  rem = s.byte_len;
  for (;;) {
    const char* pos = find_bytes(cur, rem, old_sub.ptr, old_sub.byte_len);
    if (pos == NULL) break;
    int64_t part = pos - cur;
    memcpy(dst, cur, (size_t)part);
    dst += part;
    memcpy(dst, new_sub.ptr, (size_t)new_sub.byte_len);
    dst += new_sub.byte_len;
    int64_t adv = part + old_sub.byte_len;
    cur += adv;
    rem -= adv;
  }
  memcpy(dst, cur, (size_t)rem);
  return pino_strbuf_seal(b, total);
}

/* ------------------------------------------------------------------------- */
/* Comparison and hashing                                                     */
/* ------------------------------------------------------------------------- */

bool pino_str_eq(PinoString a, PinoString b) {
  return a.byte_len == b.byte_len && (a.ptr == b.ptr || memcmp(a.ptr, b.ptr, (size_t)a.byte_len) == 0);
}

/* UTF-8 byte order equals code point order, so a byte comparison is enough (STR-10). */
int32_t pino_str_cmp(PinoString a, PinoString b) {
  int64_t n = a.byte_len < b.byte_len ? a.byte_len : b.byte_len;
  int c = memcmp(a.ptr, b.ptr, (size_t)n);
  if (c != 0) return c < 0 ? -1 : 1;
  if (a.byte_len == b.byte_len) return 0;
  return a.byte_len < b.byte_len ? -1 : 1;
}

uint64_t pino_str_hash(PinoString s) {
  uint64_t h = 0xCBF29CE484222325ull;
  for (int64_t i = 0; i < s.byte_len; i++) {
    h ^= (unsigned char)s.ptr[i];
    h *= 0x100000001B3ull;
  }
  return h;
}

/* ------------------------------------------------------------------------- */
/* Formatting and parsing                                                     */
/* ------------------------------------------------------------------------- */

PinoString pino_i64_to_str(int64_t v) {
  char buf[24];
  char* end = buf + sizeof buf;
  char* p = end;
  uint64_t u = v < 0 ? (uint64_t)0 - (uint64_t)v : (uint64_t)v;
  do {
    *--p = (char)('0' + (int)(u % 10u));
    u /= 10u;
  } while (u != 0);
  if (v < 0) {
    *--p = '-';
  }
  return pino_str_from_bytes(p, end - p);
}

PinoString pino_f64_to_str(double v) {
  if (v != v) {
    return PINO_STR_LIT("NaN");
  }
  if (v > 1.7976931348623157e308) {
    return PINO_STR_LIT("Infinity");
  }
  if (v < -1.7976931348623157e308) {
    return PINO_STR_LIT("-Infinity");
  }
  char buf[64];
  int n = 0;
  for (int prec = 15; prec <= 17; prec++) {
    n = snprintf(buf, sizeof buf, "%.*g", prec, v);
    if (strtod(buf, NULL) == v) {
      break;
    }
  }
  bool whole = true;
  for (int i = 0; i < n; i++) {
    char c = buf[i];
    if (!((c >= '0' && c <= '9') || c == '-')) {
      whole = false;
      break;
    }
  }
  if (whole && n + 2 < (int)sizeof buf) {
    buf[n++] = '.';
    buf[n++] = '0';
    buf[n] = '\0';
  }
  return pino_str_from_bytes(buf, n);
}

PinoString pino_bool_to_str(bool v) { return v ? PINO_STR_LIT("true") : PINO_STR_LIT("false"); }

PinoString pino_rune_to_str(int32_t r) {
  char enc[4];
  int n = pino_utf8_encode(r, enc);
  return pino_str_from_bytes(enc, n);
}

PinoString pino_handle_to_str(PinoHandle h) {
  char buf[48];
  int n = snprintf(buf, sizeof buf, "Handle(%lu, %lu)", (unsigned long)h.index, (unsigned long)h.gen);
  return pino_str_from_bytes(buf, n);
}

/* Copies the trimmed text of `s` into buf (NUL terminated). Returns false when it does not fit. */
static bool trimmed_copy(PinoString s, char* buf, size_t cap, size_t* len_out) {
  const char* p = s.ptr;
  int64_t len = s.byte_len;
  while (len > 0 && is_space(*p)) {
    p++;
    len--;
  }
  while (len > 0 && is_space(p[len - 1])) {
    len--;
  }
  if (len <= 0 || (size_t)len >= cap) {
    return false;
  }
  memcpy(buf, p, (size_t)len);
  buf[len] = '\0';
  *len_out = (size_t)len;
  return memchr(buf, '\0', (size_t)len) == NULL;
}

bool pino_parse_int(PinoString s, int64_t* out) {
  char buf[64];
  size_t len = 0;
  if (!trimmed_copy(s, buf, sizeof buf, &len)) {
    return false;
  }
  /* Hand-written so it behaves the same on every C runtime (msvcrt has no strtoll). */
  size_t i = 0;
  bool negative = false;
  if (buf[0] == '+' || buf[0] == '-') {
    negative = buf[0] == '-';
    i = 1;
  }
  if (i >= len) {
    return false;
  }
  uint64_t limit = negative ? (uint64_t)INT64_MAX + 1u : (uint64_t)INT64_MAX;
  uint64_t acc = 0;
  for (; i < len; i++) {
    if (buf[i] < '0' || buf[i] > '9') {
      return false;
    }
    uint64_t digit = (uint64_t)(buf[i] - '0');
    if (acc > (limit - digit) / 10u) {
      return false; /* does not fit in a 64-bit integer */
    }
    acc = acc * 10u + digit;
  }
  *out = negative ? (int64_t)(0u - acc) : (int64_t)acc;
  return true;
}

bool pino_parse_float(PinoString s, double* out) {
  char buf[128];
  size_t len = 0;
  if (!trimmed_copy(s, buf, sizeof buf, &len)) {
    return false;
  }
  for (size_t i = 0; i < len; i++) {
    char c = buf[i];
    if (!((c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.' || c == 'e' || c == 'E')) {
      return false;
    }
  }
  char* end = NULL;
  double v = strtod(buf, &end);
  if (end != buf + len) {
    return false;
  }
  *out = v;
  return true;
}

/* ------------------------------------------------------------------------- */
/* Vector of strings and split                                                */
/* ------------------------------------------------------------------------- */

PinoVec_string pino_vec_string_empty = {PINO_RC_IMMORTAL_HDR, 0, 0, NULL};

PinoVec_string* pino_vec_string_new(int64_t cap) {
  PinoVec_string* v =
      (PinoVec_string*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(PinoVec_string), 0, 0, "vector of strings");
  v->len = 0;
  v->cap = 0;
  v->data = NULL;
  if (cap > 0) {
    v->data = (PinoString*)PINO_ALLOC(pino_cur_alloc, (size_t)cap * sizeof(PinoString), "string vector elements");
    v->cap = cap;
  }
  return v;
}

void pino_vec_string_release(PinoVec_string* v) {
  if (v == NULL || !pino_rc_dec(&v->hdr)) {
    return;
  }
  for (int64_t i = 0; i < v->len; i++) {
    pino_str_release(v->data[i]);
  }
  if (v->data != NULL) {
    pino_dealloc(pino_cur_alloc, v->data, (size_t)v->cap * sizeof(PinoString));
  }
  pino_dealloc(pino_cur_alloc, v, sizeof(PinoVec_string));
}

void pino_vec_string_push(PinoVec_string** vp, PinoString s) {
  PinoVec_string* v = *vp;
  if (v->hdr.rc != 1) { /* shared, atomic, or the immortal empty singleton: copy first */
    PinoVec_string* copy = pino_vec_string_new(v->len + 4);
    for (int64_t i = 0; i < v->len; i++) {
      copy->data[i] = pino_str_retain(v->data[i]);
    }
    copy->len = v->len;
    pino_vec_string_release(v);
    *vp = copy;
    v = copy;
  }
  if (v->len == v->cap) {
    int64_t new_cap = v->cap < 4 ? 4 : v->cap * 2;
    v->data = (PinoString*)PINO_REALLOC(pino_cur_alloc, v->data, (size_t)v->cap * sizeof(PinoString),
                                        (size_t)new_cap * sizeof(PinoString), "string vector elements");
    v->cap = new_cap;
  }
  v->data[v->len++] = s;
}

PinoVec_string* pino_str_split(PinoString s, PinoString sep) {
  if (s.byte_len == 0) {
    return &pino_vec_string_empty;
  }
  PinoVec_string* v = pino_vec_string_new(4);
  if (sep.byte_len == 0) { /* one slice per rune */
    int64_t i = 0;
    while (i < s.byte_len) {
      int64_t next = i + 1;
      while (next < s.byte_len && (((unsigned char)s.ptr[next]) & 0xC0u) == 0x80u) {
        next++;
      }
      PinoString part = {s.owner, s.ptr + i, next - i};
      pino_vec_string_push(&v, pino_str_retain(part));
      i = next;
    }
    return v;
  }
  const char* cur = s.ptr;
  int64_t rem = s.byte_len;
  for (;;) {
    const char* pos = find_bytes(cur, rem, sep.ptr, sep.byte_len);
    if (pos == NULL) break;
    PinoString part = {s.owner, cur, pos - cur};
    pino_vec_string_push(&v, pino_str_retain(part));
    int64_t adv = (pos - cur) + sep.byte_len;
    cur += adv;
    rem -= adv;
  }
  PinoString last = {s.owner, cur, rem};
  pino_vec_string_push(&v, pino_str_retain(last));
  return v;
}
