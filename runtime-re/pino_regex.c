/*
 * pino_regex.c - Pino's linear-time regular expression engine (D-190).
 *
 * Mirror of pino-csharp-re/Pino.Vm/Regex/ in plain C99:
 *   1. Recursive-descent parser -> arena-allocated AST
 *   2. AST simplification + literal/prefix/anchor/unambiguous-greedy analysis
 *   3. Thompson NFA bytecode compiler + ASCII byte equivalence class partitioning
 *   4. Exact-literal / prefix memchr fast paths, ASCII byte-class lazy DFA
 *      (Russ Cox 2007/2010) with sound TRANS_RESET tracking, and Pike VM
 *      (Russ Cox 2009) for guaranteed O(M * N) leftmost-first matching
 *   5. High-level PinoRegex runtime entry points (pino_regex_*)
 *
 * Written from scratch for Pino Lang (MIT).
 * UTF-8 without BOM, LF line endings, plain C99.
 */
#include "pino_regex.h"
#include "pino_casefold.h"
#include "pino_rt.h"

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* ------------------------------------------------------------------------- */
/* Constants and basic types                                                  */
/* ------------------------------------------------------------------------- */

#define PINO_RE_MAX_RUNE 0x10FFFF
#define PINO_RE_MAX_REPEAT 1000
#define PINO_DFA_MAX_STATES 1024
#define PINO_DFA_HASH_CAP 2048 /* power of two >= 2 * PINO_DFA_MAX_STATES */
#define PINO_DFA_UNKNOWN (-1)
#define PINO_DFA_DEAD (-2)
#define PINO_DFA_RESET_BIT 0x40000000
#define PINO_DFA_STATE_MASK 0x3FFFFFFF

enum {
  RE_FLAG_I = 1u << 0,
  RE_FLAG_M = 1u << 1,
  RE_FLAG_S = 1u << 2
};

enum {
  RE_ASSERT_TEXT_START = 1u << 0,
  RE_ASSERT_TEXT_END = 1u << 1,
  RE_ASSERT_LINE_START = 1u << 2,
  RE_ASSERT_LINE_END = 1u << 3,
  RE_ASSERT_WORD_BOUNDARY = 1u << 4,
  RE_ASSERT_NOT_WORD_BOUNDARY = 1u << 5
};

typedef struct ReRange {
  int32_t lo;
  int32_t hi;
} ReRange;

typedef struct ReCharClass {
  uint64_t ascii_lo;
  uint64_t ascii_hi;
  ReRange* ranges;
  size_t len;
} ReCharClass;

typedef enum ReInstOp {
  INST_RUNE = 0,
  INST_RUNE_FOLD = 1,
  INST_ANY = 2,
  INST_ANY_NL = 3,
  INST_CLASS = 4,
  INST_ASSERT = 5,
  INST_SPLIT = 6,
  INST_JUMP = 7,
  INST_MATCH = 8
} ReInstOp;

typedef struct ReInst {
  uint8_t op;
  int32_t arg;
  int32_t out2;
} ReInst;

typedef struct ReDfaState {
  int32_t* nfa_pcs;
  int32_t nfa_count;
  uint32_t hash;
} ReDfaState;

typedef struct ReDfa {
  bool anchored;
  int32_t num_classes;
  int32_t state_count;
  int32_t state_cap;
  ReDfaState* states;
  uint8_t* is_match;
  int32_t* trans; /* [state_cap * num_classes] */
  int32_t hash_slots[PINO_DFA_HASH_CAP];
} ReDfa;

struct PinoReProg {
  ReInst* insts;
  size_t inst_len;
  size_t inst_cap;

  ReCharClass* classes;
  size_t class_len;
  size_t class_cap;

  char* literal_prefix;
  size_t literal_prefix_len;
  bool is_exact_literal;
  bool is_anchored_start;
  bool has_asserts;
  bool can_use_dfa;
  bool is_unambiguous_greedy;

  uint8_t ascii_byte_class[128];
  int32_t ascii_class_rep[128];
  int32_t num_ascii_classes;

  ReDfa* unanchored_dfa;
  ReDfa* anchored_dfa;
};

/* ------------------------------------------------------------------------- */
/* UTF-8 and ASCII helpers                                                    */
/* ------------------------------------------------------------------------- */

static inline bool re_is_ascii_word(int32_t r) {
  return (r >= '0' && r <= '9') || (r >= 'A' && r <= 'Z') || (r >= 'a' && r <= 'z') || r == '_';
}

static int32_t re_decode_before(const char* bytes, size_t pos) {
  if (pos == 0) {
    return -1;
  }
  unsigned char prev = (unsigned char)bytes[pos - 1];
  if (prev < 0x80u) {
    return (int32_t)prev;
  }
  size_t start = pos - 1;
  size_t limit = pos > 4 ? pos - 4 : 0;
  while (start > limit && (((unsigned char)bytes[start]) & 0xC0u) == 0x80u) {
    start--;
  }
  int w = 0;
  int32_t r = pino_utf8_decode(bytes + start, bytes + pos, &w);
  return (start + (size_t)w == pos) ? r : 0xFFFD;
}

size_t pino_re_next_rune_pos(const char* text, size_t text_len, size_t pos) {
  if (pos >= text_len) {
    return text_len;
  }
  int w = 0;
  (void)pino_utf8_decode(text + pos, text + text_len, &w);
  return pos + (size_t)w;
}

/* ------------------------------------------------------------------------- */
/* Character class helpers                                                    */
/* ------------------------------------------------------------------------- */

static inline bool cc_matches_ascii(const ReCharClass* cc, int32_t b) {
  if (b < 64) {
    return ((cc->ascii_lo >> (unsigned)b) & 1ULL) != 0;
  }
  return ((cc->ascii_hi >> (unsigned)(b - 64)) & 1ULL) != 0;
}

static bool cc_matches(const ReCharClass* cc, int32_t rune) {
  if (rune >= 0 && rune < 128) {
    return cc_matches_ascii(cc, rune);
  }
  size_t lo = 0;
  size_t hi = cc->len;
  while (lo < hi) {
    size_t mid = lo + ((hi - lo) >> 1);
    if (rune < cc->ranges[mid].lo) {
      hi = mid;
    } else if (rune > cc->ranges[mid].hi) {
      lo = mid + 1;
    } else {
      return true;
    }
  }
  return false;
}

static void cc_compute_ascii_mask(ReCharClass* cc) {
  uint64_t lo = 0;
  uint64_t hi = 0;
  for (size_t i = 0; i < cc->len; i++) {
    if (cc->ranges[i].lo >= 128) {
      break;
    }
    int32_t end = cc->ranges[i].hi < 127 ? cc->ranges[i].hi : 127;
    for (int32_t c = cc->ranges[i].lo; c <= end; c++) {
      if (c < 64) {
        lo |= 1ULL << (unsigned)c;
      } else {
        hi |= 1ULL << (unsigned)(c - 64);
      }
    }
  }
  cc->ascii_lo = lo;
  cc->ascii_hi = hi;
}

static bool cc_intersects(const ReCharClass* a, const ReCharClass* b) {
  if ((a->ascii_lo & b->ascii_lo) != 0 || (a->ascii_hi & b->ascii_hi) != 0) {
    return true;
  }
  size_t i = 0;
  size_t j = 0;
  while (i < a->len && j < b->len) {
    if (a->ranges[i].hi < b->ranges[j].lo) {
      i++;
    } else if (b->ranges[j].hi < a->ranges[i].lo) {
      j++;
    } else {
      return true;
    }
  }
  return false;
}

static void cc_free(ReCharClass* cc) {
  if (cc->ranges != NULL) {
    pino_dealloc(pino_cur_alloc, cc->ranges, cc->len * sizeof(ReRange));
    cc->ranges = NULL;
    cc->len = 0;
  }
}

typedef struct RangeVec {
  ReRange* data;
  size_t len;
  size_t cap;
} RangeVec;

static void rv_push(RangeVec* v, int32_t lo, int32_t hi) {
  if (v->len == v->cap) {
    size_t old_cap = v->cap;
    size_t new_cap = old_cap == 0 ? 16 : old_cap * 2;
    if (old_cap == 0) {
      v->data = (ReRange*)PINO_ALLOC(pino_cur_alloc, new_cap * sizeof(ReRange), "re ranges");
    } else {
      v->data = (ReRange*)PINO_REALLOC(pino_cur_alloc, v->data, old_cap * sizeof(ReRange),
                                       new_cap * sizeof(ReRange), "re ranges");
    }
    v->cap = new_cap;
  }
  v->data[v->len].lo = lo;
  v->data[v->len].hi = hi;
  v->len++;
}

static void rv_free(RangeVec* v) {
  if (v->data != NULL) {
    pino_dealloc(pino_cur_alloc, v->data, v->cap * sizeof(ReRange));
    v->data = NULL;
    v->len = 0;
    v->cap = 0;
  }
}

static int range_cmp(const void* a, const void* b) {
  const ReRange* ra = (const ReRange*)a;
  const ReRange* rb = (const ReRange*)b;
  if (ra->lo != rb->lo) {
    return ra->lo < rb->lo ? -1 : 1;
  }
  if (ra->hi != rb->hi) {
    return ra->hi < rb->hi ? -1 : 1;
  }
  return 0;
}

static ReCharClass cc_build(RangeVec* raw, bool negate) {
  ReCharClass cc;
  cc.ascii_lo = 0;
  cc.ascii_hi = 0;
  cc.ranges = NULL;
  cc.len = 0;

  if (raw->len > 0) {
    qsort(raw->data, raw->len, sizeof(ReRange), range_cmp);
    size_t out = 0;
    for (size_t i = 1; i < raw->len; i++) {
      if (raw->data[i].lo <= raw->data[out].hi + 1) {
        if (raw->data[i].hi > raw->data[out].hi) {
          raw->data[out].hi = raw->data[i].hi;
        }
      } else {
        out++;
        raw->data[out] = raw->data[i];
      }
    }
    size_t merged_len = out + 1;
    if (!negate) {
      cc.ranges = (ReRange*)PINO_ALLOC(pino_cur_alloc, merged_len * sizeof(ReRange), "re cc");
      memcpy(cc.ranges, raw->data, merged_len * sizeof(ReRange));
      cc.len = merged_len;
    } else {
      RangeVec inv = {NULL, 0, 0};
      int32_t cur = 0;
      for (size_t i = 0; i < merged_len; i++) {
        if (cur < raw->data[i].lo) {
          rv_push(&inv, cur, raw->data[i].lo - 1);
        }
        if (raw->data[i].hi >= PINO_RE_MAX_RUNE) {
          cur = PINO_RE_MAX_RUNE + 1;
          break;
        }
        cur = raw->data[i].hi + 1;
      }
      if (cur <= PINO_RE_MAX_RUNE) {
        rv_push(&inv, cur, PINO_RE_MAX_RUNE);
      }
      if (inv.len > 0) {
        cc.ranges = (ReRange*)PINO_ALLOC(pino_cur_alloc, inv.len * sizeof(ReRange), "re cc");
        memcpy(cc.ranges, inv.data, inv.len * sizeof(ReRange));
        cc.len = inv.len;
      }
      rv_free(&inv);
    }
  } else if (negate) {
    cc.ranges = (ReRange*)PINO_ALLOC(pino_cur_alloc, sizeof(ReRange), "re cc");
    cc.ranges[0].lo = 0;
    cc.ranges[0].hi = PINO_RE_MAX_RUNE;
    cc.len = 1;
  }

  cc_compute_ascii_mask(&cc);
  return cc;
}

static void rv_add_folded_range(RangeVec* target, int32_t lo, int32_t hi) {
  rv_push(target, lo, hi);

  size_t left = 0;
  size_t right = PINO_CF_ORBIT_LEN;
  while (left < right) {
    size_t mid = left + ((right - left) >> 1);
    if (pino_cf_orbit[mid].hi < lo) {
      left = mid + 1;
    } else {
      right = mid;
    }
  }

  for (size_t idx = left; idx < PINO_CF_ORBIT_LEN && pino_cf_orbit[idx].lo <= hi; idx++) {
    const PinoFoldRun* run = &pino_cf_orbit[idx];
    int32_t start = lo > run->lo ? lo : run->lo;
    int32_t end = hi < run->hi ? hi : run->hi;
    if (run->stride == 1u) {
      rv_push(target, start + run->delta, end + run->delta);
      for (int32_t r = start; r <= end; r++) {
        int32_t n1 = pino_casefold_next(r);
        int32_t n2 = pino_casefold_next(n1);
        if (n2 != r) {
          int32_t cur = n2;
          while (cur != r) {
            rv_push(target, cur, cur);
            cur = pino_casefold_next(cur);
          }
        }
      }
    } else {
      if (((start - run->lo) & 1) != 0) {
        start++;
      }
      for (int32_t r = start; r <= end; r += 2) {
        int32_t cur = pino_casefold_next(r);
        while (cur != r) {
          rv_push(target, cur, cur);
          cur = pino_casefold_next(cur);
        }
      }
    }
  }
}

static void rv_add_shorthand(RangeVec* v, int32_t ch) {
  RangeVec tmp = {NULL, 0, 0};
  bool negate = false;
  switch (ch) {
    case 'd':
      rv_push(v, '0', '9');
      return;
    case 'D':
      rv_push(&tmp, '0', '9');
      negate = true;
      break;
    case 'w':
      rv_push(v, '0', '9');
      rv_push(v, 'A', 'Z');
      rv_push(v, '_', '_');
      rv_push(v, 'a', 'z');
      return;
    case 'W':
      rv_push(&tmp, '0', '9');
      rv_push(&tmp, 'A', 'Z');
      rv_push(&tmp, '_', '_');
      rv_push(&tmp, 'a', 'z');
      negate = true;
      break;
    case 's':
      rv_push(v, '\t', '\n');
      rv_push(v, '\f', '\r');
      rv_push(v, ' ', ' ');
      return;
    case 'S':
      rv_push(&tmp, '\t', '\n');
      rv_push(&tmp, '\f', '\r');
      rv_push(&tmp, ' ', ' ');
      negate = true;
      break;
    default:
      return;
  }
  ReCharClass cc = cc_build(&tmp, negate);
  rv_free(&tmp);
  for (size_t i = 0; i < cc.len; i++) {
    rv_push(v, cc.ranges[i].lo, cc.ranges[i].hi);
  }
  cc_free(&cc);
}

/* ------------------------------------------------------------------------- */
/* Arena-allocated AST and Parser                                             */
/* ------------------------------------------------------------------------- */

typedef enum AstKind {
  AST_EMPTY = 0,
  AST_LITERAL = 1,
  AST_ANY = 2,
  AST_CLASS = 3,
  AST_ASSERT = 4,
  AST_CONCAT = 5,
  AST_ALT = 6,
  AST_REPEAT = 7
} AstKind;

typedef struct AstNode {
  uint8_t kind;
  uint8_t flag;  /* fold_case / dot_all / greedy / assert_kind */
  int32_t val;   /* rune / class_idx / min / child_start */
  int32_t val2;  /* max / child_count */
  int32_t child; /* repeat child node id */
} AstNode;

typedef struct AstPool {
  AstNode* nodes;
  size_t node_len;
  size_t node_cap;

  int32_t* children;
  size_t child_len;
  size_t child_cap;

  ReCharClass* classes;
  size_t class_len;
  size_t class_cap;
} AstPool;

static void ast_init(AstPool* a) {
  memset(a, 0, sizeof(*a));
}

static void ast_free(AstPool* a) {
  for (size_t i = 0; i < a->class_len; i++) {
    cc_free(&a->classes[i]);
  }
  if (a->classes != NULL) {
    pino_dealloc(pino_cur_alloc, a->classes, a->class_cap * sizeof(ReCharClass));
  }
  if (a->children != NULL) {
    pino_dealloc(pino_cur_alloc, a->children, a->child_cap * sizeof(int32_t));
  }
  if (a->nodes != NULL) {
    pino_dealloc(pino_cur_alloc, a->nodes, a->node_cap * sizeof(AstNode));
  }
  memset(a, 0, sizeof(*a));
}

static int32_t ast_new_node(AstPool* a, AstNode node) {
  if (a->node_len == a->node_cap) {
    size_t old_cap = a->node_cap;
    size_t new_cap = old_cap == 0 ? 32 : old_cap * 2;
    if (old_cap == 0) {
      a->nodes = (AstNode*)PINO_ALLOC(pino_cur_alloc, new_cap * sizeof(AstNode), "re ast nodes");
    } else {
      a->nodes = (AstNode*)PINO_REALLOC(pino_cur_alloc, a->nodes, old_cap * sizeof(AstNode),
                                        new_cap * sizeof(AstNode), "re ast nodes");
    }
    a->node_cap = new_cap;
  }
  int32_t id = (int32_t)a->node_len;
  a->nodes[a->node_len++] = node;
  return id;
}

static int32_t ast_add_class(AstPool* a, ReCharClass cc) {
  if (a->class_len == a->class_cap) {
    size_t old_cap = a->class_cap;
    size_t new_cap = old_cap == 0 ? 8 : old_cap * 2;
    if (old_cap == 0) {
      a->classes = (ReCharClass*)PINO_ALLOC(pino_cur_alloc, new_cap * sizeof(ReCharClass), "re ast classes");
    } else {
      a->classes = (ReCharClass*)PINO_REALLOC(pino_cur_alloc, a->classes, old_cap * sizeof(ReCharClass),
                                              new_cap * sizeof(ReCharClass), "re ast classes");
    }
    a->class_cap = new_cap;
  }
  int32_t id = (int32_t)a->class_len;
  a->classes[a->class_len++] = cc;
  return id;
}

static int32_t ast_alloc_children(AstPool* a, const int32_t* items, size_t count) {
  if (a->child_len + count > a->child_cap) {
    size_t old_cap = a->child_cap;
    size_t new_cap = old_cap == 0 ? 32 : old_cap * 2;
    while (new_cap < a->child_len + count) {
      new_cap *= 2;
    }
    if (old_cap == 0) {
      a->children = (int32_t*)PINO_ALLOC(pino_cur_alloc, new_cap * sizeof(int32_t), "re ast children");
    } else {
      a->children = (int32_t*)PINO_REALLOC(pino_cur_alloc, a->children, old_cap * sizeof(int32_t),
                                           new_cap * sizeof(int32_t), "re ast children");
    }
    a->child_cap = new_cap;
  }
  int32_t start = (int32_t)a->child_len;
  memcpy(a->children + a->child_len, items, count * sizeof(int32_t));
  a->child_len += count;
  return start;
}

typedef struct ReParser {
  const char* src;
  size_t len;
  size_t pos;
  AstPool* ast;
  bool has_err;
  char* err_buf;
  size_t err_cap;
} ReParser;

static void set_error_reason(ReParser* p, const char* reason_fmt, ...) {
  if (p->has_err) {
    return;
  }
  p->has_err = true;
  if (p->err_buf == NULL || p->err_cap == 0) {
    return;
  }
  char reason[256];
  va_list ap;
  va_start(ap, reason_fmt);
  vsnprintf(reason, sizeof(reason), reason_fmt, ap);
  va_end(ap);

  snprintf(p->err_buf, p->err_cap, "Invalid pattern '%.*s': %s.", (int)p->len, p->src, reason);
}

static void format_rune_utf8(int32_t rune, char out[8]) {
  int n = pino_utf8_encode(rune, out);
  out[n] = '\0';
}

static int32_t parser_read_rune(ReParser* p) {
  int w = 0;
  int32_t r = pino_utf8_decode(p->src + p->pos, p->src + p->len, &w);
  p->pos += (size_t)w;
  return r;
}

static int hex_digit(unsigned char c) {
  if (c >= '0' && c <= '9') {
    return (int)(c - '0');
  }
  if (c >= 'a' && c <= 'f') {
    return (int)(c - 'a' + 10);
  }
  if (c >= 'A' && c <= 'F') {
    return (int)(c - 'A' + 10);
  }
  return -1;
}

static int32_t parse_hex_escape(ReParser* p) {
  if (p->pos < p->len && p->src[p->pos] == '{') {
    p->pos++;
    int32_t value = 0;
    int digits = 0;
    while (p->pos < p->len && p->src[p->pos] != '}') {
      int d = hex_digit((unsigned char)p->src[p->pos]);
      if (d < 0) {
        set_error_reason(p, "invalid escape '\\x'");
        return 0;
      }
      p->pos++;
      digits++;
      if (value <= PINO_RE_MAX_RUNE) {
        value = (value << 4) | d;
      }
    }
    if (p->pos >= p->len || p->src[p->pos] != '}' || digits == 0 || value > PINO_RE_MAX_RUNE ||
        (value >= 0xD800 && value <= 0xDFFF)) {
      set_error_reason(p, "invalid escape '\\x'");
      return 0;
    }
    p->pos++;
    return value;
  }

  if (p->pos + 2 > p->len) {
    set_error_reason(p, "invalid escape '\\x'");
    return 0;
  }
  int hi = hex_digit((unsigned char)p->src[p->pos]);
  int lo = hex_digit((unsigned char)p->src[p->pos + 1]);
  if (hi < 0 || lo < 0) {
    set_error_reason(p, "invalid escape '\\x'");
    return 0;
  }
  p->pos += 2;
  return (hi << 4) | lo;
}

static int32_t parse_single_escape_rune(ReParser* p, int32_t r) {
  switch (r) {
    case 'n':
      return '\n';
    case 't':
      return '\t';
    case 'r':
      return '\r';
    case 'f':
      return '\f';
    case 'v':
      return '\v';
    case '0':
      return '\0';
    case 'x':
      return parse_hex_escape(p);
    case '\\':
    case '.':
    case '+':
    case '*':
    case '?':
    case '(':
    case ')':
    case '[':
    case ']':
    case '{':
    case '}':
    case '^':
    case '$':
    case '|':
    case '-':
      return r;
    default: {
      char buf[8];
      format_rune_utf8(r, buf);
      set_error_reason(p, "invalid escape '\\%s'", buf);
      return 0;
    }
  }
}

static bool parse_class_item(ReParser* p, bool* is_shorthand_out, int32_t* rune_out, int32_t* shorthand_ch_out) {
  if (p->src[p->pos] == '\\') {
    p->pos++;
    if (p->pos >= p->len) {
      set_error_reason(p, "invalid escape '\\'");
      return false;
    }
    int32_t r = parser_read_rune(p);
    if (r == 'd' || r == 'D' || r == 'w' || r == 'W' || r == 's' || r == 'S') {
      *is_shorthand_out = true;
      *shorthand_ch_out = r;
      *rune_out = 0;
      return true;
    }
    int32_t esc = parse_single_escape_rune(p, r);
    if (p->has_err) {
      return false;
    }
    *is_shorthand_out = false;
    *rune_out = esc;
    *shorthand_ch_out = 0;
    return true;
  }
  *is_shorthand_out = false;
  *rune_out = parser_read_rune(p);
  *shorthand_ch_out = 0;
  return true;
}

static int32_t parse_char_class(ReParser* p, uint32_t flags) {
  p->pos++; /* consume '[' */
  if (p->pos >= p->len) {
    set_error_reason(p, "missing ]");
    return -1;
  }
  bool negate = false;
  if (p->src[p->pos] == '^') {
    negate = true;
    p->pos++;
  }
  bool fold = (flags & RE_FLAG_I) != 0;
  RangeVec ranges = {NULL, 0, 0};
  bool first = true;

  while (p->pos < p->len) {
    if (!first && p->src[p->pos] == ']') {
      p->pos++;
      ReCharClass cc = cc_build(&ranges, negate);
      rv_free(&ranges);
      int32_t cid = ast_add_class(p->ast, cc);
      AstNode n = {AST_CLASS, 0, cid, 0, -1};
      return ast_new_node(p->ast, n);
    }
    first = false;

    bool is_short = false;
    int32_t r1 = 0;
    int32_t short_ch = 0;
    if (!parse_class_item(p, &is_short, &r1, &short_ch)) {
      rv_free(&ranges);
      return -1;
    }
    if (is_short) {
      rv_add_shorthand(&ranges, short_ch);
      continue;
    }

    if (p->pos < p->len && p->src[p->pos] == '-' && p->pos + 1 < p->len && p->src[p->pos + 1] != ']') {
      p->pos++; /* consume '-' */
      bool end_short = false;
      int32_t r2 = 0;
      int32_t end_short_ch = 0;
      if (!parse_class_item(p, &end_short, &r2, &end_short_ch)) {
        rv_free(&ranges);
        return -1;
      }
      if (end_short) {
        char b1[8];
        format_rune_utf8(r1, b1);
        set_error_reason(p, "invalid character range %s-\\", b1);
        rv_free(&ranges);
        return -1;
      }
      if (r1 > r2) {
        char b1[8], b2[8];
        format_rune_utf8(r1, b1);
        format_rune_utf8(r2, b2);
        set_error_reason(p, "invalid character range %s-%s", b1, b2);
        rv_free(&ranges);
        return -1;
      }
      if (fold) {
        rv_add_folded_range(&ranges, r1, r2);
      } else {
        rv_push(&ranges, r1, r2);
      }
    } else if (fold) {
      rv_add_folded_range(&ranges, r1, r1);
    } else {
      rv_push(&ranges, r1, r1);
    }
  }

  rv_free(&ranges);
  set_error_reason(p, "missing ]");
  return -1;
}

static bool try_parse_count(const char* s, size_t len, size_t* idx, int32_t* out) {
  int32_t v = 0;
  size_t start = *idx;
  while (*idx < len && s[*idx] >= '0' && s[*idx] <= '9') {
    int32_t d = (int32_t)(s[*idx] - '0');
    if (v <= PINO_RE_MAX_REPEAT) {
      v = v * 10 + d;
    }
    (*idx)++;
  }
  *out = v;
  return *idx > start;
}

static bool parse_brace_quantifier(ReParser* p, int32_t* min_out, int32_t* max_out,
                                   const char** raw_ptr, size_t* raw_len) {
  p->pos++; /* consume '{' */
  size_t start = p->pos;
  size_t close = p->len;
  for (size_t i = p->pos; i < p->len; i++) {
    if (p->src[i] == '}') {
      close = i;
      break;
    }
  }
  if (close == p->len) {
    set_error_reason(p, "invalid repetition count {%.*s}", (int)(p->len - start), p->src + start);
    return false;
  }
  const char* inner = p->src + start;
  size_t ilen = close - start;
  p->pos = close + 1;
  *raw_ptr = inner;
  *raw_len = ilen;

  if (ilen == 0) {
    set_error_reason(p, "invalid repetition count {}");
    return false;
  }

  size_t idx = 0;
  int32_t min_v = 0;
  int32_t max_v = 0;
  if (!try_parse_count(inner, ilen, &idx, &min_v)) {
    set_error_reason(p, "invalid repetition count {%.*s}", (int)ilen, inner);
    return false;
  }

  if (idx == ilen) {
    if (min_v > PINO_RE_MAX_REPEAT) {
      set_error_reason(p, "repetition count above 1000");
      return false;
    }
    max_v = min_v;
  } else if (inner[idx] == ',') {
    idx++;
    if (idx == ilen) {
      if (min_v > PINO_RE_MAX_REPEAT) {
        set_error_reason(p, "repetition count above 1000");
        return false;
      }
      max_v = -1;
    } else {
      if (!try_parse_count(inner, ilen, &idx, &max_v) || idx != ilen) {
        set_error_reason(p, "invalid repetition count {%.*s}", (int)ilen, inner);
        return false;
      }
      if (min_v > PINO_RE_MAX_REPEAT || max_v > PINO_RE_MAX_REPEAT) {
        set_error_reason(p, "repetition count above 1000");
        return false;
      }
      if (min_v > max_v) {
        set_error_reason(p, "invalid repetition count {%.*s}", (int)ilen, inner);
        return false;
      }
    }
  } else {
    set_error_reason(p, "invalid repetition count {%.*s}", (int)ilen, inner);
    return false;
  }

  *min_out = min_v;
  *max_out = max_v;
  return true;
}

static int32_t parse_alt(ReParser* p, uint32_t* flags, bool inside_group);

/* Returns node id >= 0 for a group node, -2 for a flag-only directive, -1 on error. */
static int32_t parse_group(ReParser* p, uint32_t* flags) {
  p->pos++; /* consume '(' */
  if (p->pos >= p->len) {
    set_error_reason(p, "missing )");
    return -1;
  }
  if (p->src[p->pos] != '?') {
    uint32_t saved = *flags;
    int32_t inner = parse_alt(p, flags, true);
    if (p->has_err) {
      return -1;
    }
    if (p->pos >= p->len || p->src[p->pos] != ')') {
      set_error_reason(p, "missing )");
      return -1;
    }
    p->pos++;
    *flags = saved;
    return inner;
  }

  p->pos++; /* consume '?' */
  if (p->pos >= p->len) {
    set_error_reason(p, "missing )");
    return -1;
  }

  char next = p->src[p->pos];
  if (next == '=') {
    set_error_reason(p, "lookaround like '(?=' isn't supported; Pino's regex always runs in linear time");
    return -1;
  }
  if (next == '!') {
    set_error_reason(p, "lookaround like '(?!' isn't supported; Pino's regex always runs in linear time");
    return -1;
  }
  if (next == '<' && p->pos + 1 < p->len) {
    char after = p->src[p->pos + 1];
    if (after == '=') {
      set_error_reason(p, "lookaround like '(?<=' isn't supported; Pino's regex always runs in linear time");
      return -1;
    }
    if (after == '!') {
      set_error_reason(p, "lookaround like '(?<!' isn't supported; Pino's regex always runs in linear time");
      return -1;
    }
  }

  if (next == ':') {
    p->pos++;
    uint32_t saved = *flags;
    int32_t inner = parse_alt(p, flags, true);
    if (p->has_err) {
      return -1;
    }
    if (p->pos >= p->len || p->src[p->pos] != ')') {
      set_error_reason(p, "missing )");
      return -1;
    }
    p->pos++;
    *flags = saved;
    return inner;
  }

  uint32_t add = 0;
  uint32_t clear = 0;
  bool negating = false;
  bool seen_any = false;
  bool seen_after_dash = false;

  while (p->pos < p->len) {
    int32_t r = parser_read_rune(p);
    if (r == ')') {
      if (!seen_any || (negating && !seen_after_dash)) {
        set_error_reason(p, "unknown flag ')'");
        return -1;
      }
      *flags = (*flags | add) & ~clear;
      return -2;
    }
    if (r == ':') {
      if (!seen_any || (negating && !seen_after_dash)) {
        set_error_reason(p, "unknown flag ':'");
        return -1;
      }
      uint32_t saved = *flags;
      *flags = (*flags | add) & ~clear;
      int32_t inner = parse_alt(p, flags, true);
      if (p->has_err) {
        return -1;
      }
      if (p->pos >= p->len || p->src[p->pos] != ')') {
        set_error_reason(p, "missing )");
        return -1;
      }
      p->pos++;
      *flags = saved;
      return inner;
    }
    if (r == '-') {
      if (negating) {
        set_error_reason(p, "unknown flag '-'");
        return -1;
      }
      negating = true;
      continue;
    }
    uint32_t bit = 0;
    if (r == 'i') {
      bit = RE_FLAG_I;
    } else if (r == 'm') {
      bit = RE_FLAG_M;
    } else if (r == 's') {
      bit = RE_FLAG_S;
    } else {
      char buf[8];
      format_rune_utf8(r, buf);
      set_error_reason(p, "unknown flag '%s'", buf);
      return -1;
    }
    if (negating) {
      clear |= bit;
      add &= ~bit;
      seen_after_dash = true;
    } else {
      add |= bit;
      clear &= ~bit;
    }
    seen_any = true;
  }

  set_error_reason(p, "missing )");
  return -1;
}

static int32_t parse_escape_atom(ReParser* p, uint32_t flags) {
  p->pos++; /* consume '\' */
  if (p->pos >= p->len) {
    set_error_reason(p, "invalid escape '\\'");
    return -1;
  }
  int32_t r = parser_read_rune(p);
  if (r >= '1' && r <= '9') {
    set_error_reason(p, "backreferences like '\\%c' aren't supported; Pino's regex always runs in linear time", (char)r);
    return -1;
  }
  if (r == 'b' || r == 'B' || r == 'A' || r == 'z') {
    uint8_t ak = (r == 'b')   ? RE_ASSERT_WORD_BOUNDARY
                 : (r == 'B') ? RE_ASSERT_NOT_WORD_BOUNDARY
                 : (r == 'A') ? RE_ASSERT_TEXT_START
                              : RE_ASSERT_TEXT_END;
    AstNode n = {AST_ASSERT, ak, 0, 0, -1};
    return ast_new_node(p->ast, n);
  }
  if (r == 'd' || r == 'D' || r == 'w' || r == 'W' || r == 's' || r == 'S') {
    RangeVec rv = {NULL, 0, 0};
    rv_add_shorthand(&rv, r);
    ReCharClass cc = cc_build(&rv, false);
    rv_free(&rv);
    int32_t cid = ast_add_class(p->ast, cc);
    AstNode n = {AST_CLASS, 0, cid, 0, -1};
    return ast_new_node(p->ast, n);
  }
  int32_t esc = parse_single_escape_rune(p, r);
  if (p->has_err) {
    return -1;
  }
  bool fold = (flags & RE_FLAG_I) != 0;
  AstNode n = {AST_LITERAL, fold ? 1u : 0u, fold ? pino_casefold_canonical(esc) : esc, 0, -1};
  return ast_new_node(p->ast, n);
}

typedef struct Int32Vec {
  int32_t* data;
  size_t len;
  size_t cap;
} Int32Vec;

static void iv_push(Int32Vec* v, int32_t x) {
  if (v->len == v->cap) {
    size_t old_cap = v->cap;
    size_t new_cap = old_cap == 0 ? 16 : old_cap * 2;
    if (old_cap == 0) {
      v->data = (int32_t*)PINO_ALLOC(pino_cur_alloc, new_cap * sizeof(int32_t), "re iv");
    } else {
      v->data = (int32_t*)PINO_REALLOC(pino_cur_alloc, v->data, old_cap * sizeof(int32_t),
                                       new_cap * sizeof(int32_t), "re iv");
    }
    v->cap = new_cap;
  }
  v->data[v->len++] = x;
}

static void iv_free(Int32Vec* v) {
  if (v->data != NULL) {
    pino_dealloc(pino_cur_alloc, v->data, v->cap * sizeof(int32_t));
    v->data = NULL;
    v->len = 0;
    v->cap = 0;
  }
}

static int32_t parse_concat(ReParser* p, uint32_t* flags, bool inside_group) {
  Int32Vec items = {NULL, 0, 0};
  bool can_repeat = false;
  bool last_was_repeat = false;
  bool last_lazy_set = false;

  while (p->pos < p->len) {
    char b = p->src[p->pos];
    if (b == '|') {
      break;
    }
    if (b == ')') {
      if (inside_group) {
        break;
      }
      set_error_reason(p, "unexpected )");
      iv_free(&items);
      return -1;
    }

    if (b == '*' || b == '+' || b == '?') {
      p->pos++;
      if (b == '?' && last_was_repeat && !last_lazy_set) {
        int32_t prev_id = items.data[items.len - 1];
        p->ast->nodes[prev_id].flag = 0u; /* greedy = false */
        last_lazy_set = true;
        continue;
      }
      if (!can_repeat || last_was_repeat) {
        set_error_reason(p, "nothing to repeat before '%c'", b);
        iv_free(&items);
        return -1;
      }
      int32_t min_v = (b == '+') ? 1 : 0;
      int32_t max_v = (b == '?') ? 1 : -1;
      AstNode rep = {AST_REPEAT, 1u, min_v, max_v, items.data[items.len - 1]};
      items.data[items.len - 1] = ast_new_node(p->ast, rep);
      last_was_repeat = true;
      last_lazy_set = false;
      continue;
    }

    if (b == '{') {
      int32_t min_v = 0, max_v = 0;
      const char* raw_ptr = NULL;
      size_t raw_len = 0;
      if (!parse_brace_quantifier(p, &min_v, &max_v, &raw_ptr, &raw_len)) {
        iv_free(&items);
        return -1;
      }
      if (!can_repeat || last_was_repeat) {
        set_error_reason(p, "nothing to repeat before '{%.*s}'", (int)raw_len, raw_ptr);
        iv_free(&items);
        return -1;
      }
      AstNode rep = {AST_REPEAT, 1u, min_v, max_v, items.data[items.len - 1]};
      items.data[items.len - 1] = ast_new_node(p->ast, rep);
      last_was_repeat = true;
      last_lazy_set = false;
      continue;
    }

    if (b == '(') {
      int32_t g = parse_group(p, flags);
      if (p->has_err) {
        iv_free(&items);
        return -1;
      }
      if (g == -2) {
        can_repeat = false;
        last_was_repeat = false;
        last_lazy_set = false;
      } else {
        iv_push(&items, g);
        can_repeat = true;
        last_was_repeat = false;
        last_lazy_set = false;
      }
      continue;
    }

    if (b == '[') {
      int32_t cc = parse_char_class(p, *flags);
      if (p->has_err) {
        iv_free(&items);
        return -1;
      }
      iv_push(&items, cc);
      can_repeat = true;
      last_was_repeat = false;
      last_lazy_set = false;
      continue;
    }

    if (b == '.') {
      p->pos++;
      AstNode n = {AST_ANY, ((*flags & RE_FLAG_S) != 0) ? 1u : 0u, 0, 0, -1};
      iv_push(&items, ast_new_node(p->ast, n));
      can_repeat = true;
      last_was_repeat = false;
      last_lazy_set = false;
      continue;
    }

    if (b == '^') {
      p->pos++;
      uint8_t ak = ((*flags & RE_FLAG_M) != 0) ? RE_ASSERT_LINE_START : RE_ASSERT_TEXT_START;
      AstNode n = {AST_ASSERT, ak, 0, 0, -1};
      iv_push(&items, ast_new_node(p->ast, n));
      can_repeat = true;
      last_was_repeat = false;
      last_lazy_set = false;
      continue;
    }

    if (b == '$') {
      p->pos++;
      uint8_t ak = ((*flags & RE_FLAG_M) != 0) ? RE_ASSERT_LINE_END : RE_ASSERT_TEXT_END;
      AstNode n = {AST_ASSERT, ak, 0, 0, -1};
      iv_push(&items, ast_new_node(p->ast, n));
      can_repeat = true;
      last_was_repeat = false;
      last_lazy_set = false;
      continue;
    }

    if (b == '\\') {
      int32_t e = parse_escape_atom(p, *flags);
      if (p->has_err) {
        iv_free(&items);
        return -1;
      }
      iv_push(&items, e);
      can_repeat = true;
      last_was_repeat = false;
      last_lazy_set = false;
      continue;
    }

    int32_t r = parser_read_rune(p);
    bool fold = (*flags & RE_FLAG_I) != 0;
    AstNode n = {AST_LITERAL, fold ? 1u : 0u, fold ? pino_casefold_canonical(r) : r, 0, -1};
    iv_push(&items, ast_new_node(p->ast, n));
    can_repeat = true;
    last_was_repeat = false;
    last_lazy_set = false;
  }

  int32_t res;
  if (items.len == 0) {
    AstNode empty = {AST_EMPTY, 0, 0, 0, -1};
    res = ast_new_node(p->ast, empty);
  } else if (items.len == 1) {
    res = items.data[0];
  } else {
    int32_t start = ast_alloc_children(p->ast, items.data, items.len);
    AstNode cat = {AST_CONCAT, 0, start, (int32_t)items.len, -1};
    res = ast_new_node(p->ast, cat);
  }
  iv_free(&items);
  return res;
}

static int32_t parse_alt(ReParser* p, uint32_t* flags, bool inside_group) {
  int32_t first = parse_concat(p, flags, inside_group);
  if (p->has_err) {
    return -1;
  }
  if (p->pos >= p->len || p->src[p->pos] != '|') {
    return first;
  }
  Int32Vec branches = {NULL, 0, 0};
  iv_push(&branches, first);
  while (p->pos < p->len && p->src[p->pos] == '|') {
    p->pos++;
    int32_t br = parse_concat(p, flags, inside_group);
    if (p->has_err) {
      iv_free(&branches);
      return -1;
    }
    iv_push(&branches, br);
  }
  int32_t start = ast_alloc_children(p->ast, branches.data, branches.len);
  AstNode alt = {AST_ALT, 0, start, (int32_t)branches.len, -1};
  int32_t res = ast_new_node(p->ast, alt);
  iv_free(&branches);
  return res;
}

/* ------------------------------------------------------------------------- */
/* AST Simplification and Static Analysis                                     */
/* ------------------------------------------------------------------------- */

static int32_t ast_simplify(AstPool* a, int32_t id) {
  AstNode n = a->nodes[id];
  if (n.kind == AST_CONCAT) {
    Int32Vec flat = {NULL, 0, 0};
    for (int32_t i = 0; i < n.val2; i++) {
      int32_t cid = ast_simplify(a, a->children[n.val + i]);
      AstNode cn = a->nodes[cid];
      if (cn.kind == AST_EMPTY) {
        continue;
      }
      if (cn.kind == AST_CONCAT) {
        for (int32_t j = 0; j < cn.val2; j++) {
          iv_push(&flat, a->children[cn.val + j]);
        }
      } else {
        iv_push(&flat, cid);
      }
    }
    int32_t res;
    if (flat.len == 0) {
      AstNode e = {AST_EMPTY, 0, 0, 0, -1};
      res = ast_new_node(a, e);
    } else if (flat.len == 1) {
      res = flat.data[0];
    } else {
      int32_t st = ast_alloc_children(a, flat.data, flat.len);
      AstNode cat = {AST_CONCAT, 0, st, (int32_t)flat.len, -1};
      res = ast_new_node(a, cat);
    }
    iv_free(&flat);
    return res;
  }
  if (n.kind == AST_ALT) {
    Int32Vec flat = {NULL, 0, 0};
    for (int32_t i = 0; i < n.val2; i++) {
      int32_t bid = ast_simplify(a, a->children[n.val + i]);
      AstNode bn = a->nodes[bid];
      if (bn.kind == AST_ALT) {
        for (int32_t j = 0; j < bn.val2; j++) {
          iv_push(&flat, a->children[bn.val + j]);
        }
      } else {
        iv_push(&flat, bid);
      }
    }
    int32_t res;
    if (flat.len == 1) {
      res = flat.data[0];
    } else {
      int32_t st = ast_alloc_children(a, flat.data, flat.len);
      AstNode alt = {AST_ALT, 0, st, (int32_t)flat.len, -1};
      res = ast_new_node(a, alt);
    }
    iv_free(&flat);
    return res;
  }
  if (n.kind == AST_REPEAT) {
    int32_t ch = ast_simplify(a, n.child);
    if (n.val2 == 0) {
      AstNode e = {AST_EMPTY, 0, 0, 0, -1};
      return ast_new_node(a, e);
    }
    if (n.val == 1 && n.val2 == 1) {
      return ch;
    }
    AstNode rep = {AST_REPEAT, n.flag, n.val, n.val2, ch};
    return ast_new_node(a, rep);
  }
  return id;
}

static bool ast_is_anchored_start(const AstPool* a, int32_t id) {
  const AstNode* n = &a->nodes[id];
  switch (n->kind) {
    case AST_ASSERT:
      return n->flag == RE_ASSERT_TEXT_START;
    case AST_CONCAT:
      return n->val2 > 0 && ast_is_anchored_start(a, a->children[n->val]);
    case AST_ALT:
      if (n->val2 <= 0) {
        return false;
      }
      for (int32_t i = 0; i < n->val2; i++) {
        if (!ast_is_anchored_start(a, a->children[n->val + i])) {
          return false;
        }
      }
      return true;
    case AST_REPEAT:
      return n->val > 0 && ast_is_anchored_start(a, n->child);
    default:
      return false;
  }
}

static bool ast_can_match_empty(const AstPool* a, int32_t id) {
  const AstNode* n = &a->nodes[id];
  switch (n->kind) {
    case AST_EMPTY:
    case AST_ASSERT:
      return true;
    case AST_LITERAL:
    case AST_ANY:
    case AST_CLASS:
      return false;
    case AST_CONCAT:
      for (int32_t i = 0; i < n->val2; i++) {
        if (!ast_can_match_empty(a, a->children[n->val + i])) {
          return false;
        }
      }
      return true;
    case AST_ALT:
      for (int32_t i = 0; i < n->val2; i++) {
        if (ast_can_match_empty(a, a->children[n->val + i])) {
          return true;
        }
      }
      return false;
    case AST_REPEAT:
      return n->val == 0 || ast_can_match_empty(a, n->child);
    default:
      return true;
  }
}

static bool ast_is_single_rune(const AstPool* a, int32_t id) {
  uint8_t k = a->nodes[id].kind;
  return k == AST_LITERAL || k == AST_ANY || k == AST_CLASS;
}

static ReCharClass ast_single_to_cc(const AstPool* a, int32_t id) {
  const AstNode* n = &a->nodes[id];
  RangeVec rv = {NULL, 0, 0};
  if (n->kind == AST_LITERAL) {
    if (n->flag) {
      rv_add_folded_range(&rv, n->val, n->val);
    } else {
      rv_push(&rv, n->val, n->val);
    }
  } else if (n->kind == AST_ANY) {
    if (n->flag) {
      rv_push(&rv, 0, PINO_RE_MAX_RUNE);
    } else {
      rv_push(&rv, 0, '\n' - 1);
      rv_push(&rv, '\n' + 1, PINO_RE_MAX_RUNE);
    }
  } else if (n->kind == AST_CLASS) {
    const ReCharClass* src = &a->classes[n->val];
    for (size_t i = 0; i < src->len; i++) {
      rv_push(&rv, src->ranges[i].lo, src->ranges[i].hi);
    }
  }
  ReCharClass cc = cc_build(&rv, false);
  rv_free(&rv);
  return cc;
}

static bool ast_try_first_cc(const AstPool* a, int32_t id, ReCharClass* out);

static bool ast_try_suffix_first_cc(const AstPool* a, const int32_t* items, int32_t start, int32_t count,
                                    ReCharClass* out) {
  RangeVec rv = {NULL, 0, 0};
  for (int32_t i = start; i < count; i++) {
    ReCharClass fc;
    if (!ast_try_first_cc(a, items[i], &fc)) {
      rv_free(&rv);
      return false;
    }
    for (size_t j = 0; j < fc.len; j++) {
      rv_push(&rv, fc.ranges[j].lo, fc.ranges[j].hi);
    }
    cc_free(&fc);
    if (!ast_can_match_empty(a, items[i])) {
      break;
    }
  }
  if (rv.len == 0) {
    rv_free(&rv);
    return false;
  }
  *out = cc_build(&rv, false);
  rv_free(&rv);
  return true;
}

static bool ast_try_first_cc(const AstPool* a, int32_t id, ReCharClass* out) {
  const AstNode* n = &a->nodes[id];
  if (ast_is_single_rune(a, id)) {
    *out = ast_single_to_cc(a, id);
    return true;
  }
  if (n->kind == AST_CONCAT) {
    return ast_try_suffix_first_cc(a, a->children + n->val, 0, n->val2, out);
  }
  if (n->kind == AST_REPEAT) {
    return ast_try_first_cc(a, n->child, out);
  }
  if (n->kind == AST_ALT) {
    RangeVec rv = {NULL, 0, 0};
    for (int32_t i = 0; i < n->val2; i++) {
      ReCharClass fc;
      if (!ast_try_first_cc(a, a->children[n->val + i], &fc)) {
        rv_free(&rv);
        return false;
      }
      for (size_t j = 0; j < fc.len; j++) {
        rv_push(&rv, fc.ranges[j].lo, fc.ranges[j].hi);
      }
      cc_free(&fc);
    }
    *out = cc_build(&rv, false);
    rv_free(&rv);
    return true;
  }
  return false;
}

static bool ast_is_unambiguous_greedy(const AstPool* a, int32_t id) {
  const AstNode* n = &a->nodes[id];
  switch (n->kind) {
    case AST_EMPTY:
    case AST_LITERAL:
    case AST_ANY:
    case AST_CLASS:
      return true;
    case AST_ASSERT:
      return false;
    case AST_REPEAT:
      return n->flag != 0 && ast_is_single_rune(a, n->child);
    case AST_CONCAT: {
      const int32_t* items = a->children + n->val;
      for (int32_t i = 0; i < n->val2; i++) {
        int32_t item_id = items[i];
        const AstNode* it = &a->nodes[item_id];
        bool is_last = (i == n->val2 - 1);
        if (ast_is_single_rune(a, item_id)) {
          continue;
        }
        if (it->kind == AST_REPEAT) {
          if (!it->flag) {
            return false;
          }
          if (it->val == it->val2 && ast_is_unambiguous_greedy(a, it->child)) {
            continue;
          }
          if (is_last) {
            if (ast_is_single_rune(a, it->child)) {
              continue;
            }
            if (it->val == 0 && it->val2 == 1 && !ast_can_match_empty(a, it->child) &&
                ast_is_unambiguous_greedy(a, it->child)) {
              continue;
            }
            return false;
          }
          if (!ast_is_single_rune(a, it->child)) {
            return false;
          }
          ReCharClass rep_cc = ast_single_to_cc(a, it->child);
          ReCharClass next_cc;
          bool ok = ast_try_suffix_first_cc(a, items, i + 1, n->val2, &next_cc);
          bool hit = ok && cc_intersects(&rep_cc, &next_cc);
          cc_free(&rep_cc);
          if (ok) {
            cc_free(&next_cc);
          }
          if (!ok || hit) {
            return false;
          }
          continue;
        }
        if (is_last && ast_is_unambiguous_greedy(a, item_id)) {
          continue;
        }
        return false;
      }
      return true;
    }
    case AST_ALT: {
      if (n->val2 <= 0) {
        return false;
      }
      ReCharClass* firsts = (ReCharClass*)PINO_ALLOC(pino_cur_alloc, (size_t)n->val2 * sizeof(ReCharClass), "re firsts");
      int32_t filled = 0;
      bool ok = true;
      for (int32_t i = 0; i < n->val2; i++) {
        int32_t bid = a->children[n->val + i];
        if (ast_can_match_empty(a, bid) || !ast_is_unambiguous_greedy(a, bid)) {
          ok = false;
          break;
        }
        if (!ast_try_first_cc(a, bid, &firsts[filled])) {
          ok = false;
          break;
        }
        for (int32_t j = 0; j < filled; j++) {
          if (cc_intersects(&firsts[j], &firsts[filled])) {
            ok = false;
            break;
          }
        }
        filled++;
        if (!ok) {
          break;
        }
      }
      for (int32_t i = 0; i < filled; i++) {
        cc_free(&firsts[i]);
      }
      pino_dealloc(pino_cur_alloc, firsts, (size_t)n->val2 * sizeof(ReCharClass));
      return ok;
    }
    default:
      return false;
  }
}

static void extract_literal_prefix(const AstPool* a, int32_t root, PinoReProg* prog) {
  const AstNode* n = &a->nodes[root];
  if (n->kind == AST_LITERAL && !n->flag) {
    char buf[4];
    int nb = pino_utf8_encode(n->val, buf);
    prog->literal_prefix = (char*)PINO_ALLOC(pino_cur_alloc, (size_t)nb, "re prefix");
    memcpy(prog->literal_prefix, buf, (size_t)nb);
    prog->literal_prefix_len = (size_t)nb;
    prog->is_exact_literal = true;
    return;
  }
  if (n->kind != AST_CONCAT) {
    return;
  }
  size_t lit_count = 0;
  size_t total_bytes = 0;
  for (int32_t i = 0; i < n->val2; i++) {
    const AstNode* it = &a->nodes[a->children[n->val + i]];
    if (it->kind == AST_LITERAL && !it->flag) {
      char buf[4];
      total_bytes += (size_t)pino_utf8_encode(it->val, buf);
      lit_count++;
    } else {
      break;
    }
  }
  if (total_bytes == 0) {
    return;
  }
  prog->literal_prefix = (char*)PINO_ALLOC(pino_cur_alloc, total_bytes, "re prefix");
  size_t off = 0;
  for (size_t i = 0; i < lit_count; i++) {
    const AstNode* it = &a->nodes[a->children[n->val + (int32_t)i]];
    off += (size_t)pino_utf8_encode(it->val, prog->literal_prefix + off);
  }
  prog->literal_prefix_len = total_bytes;
  prog->is_exact_literal = (lit_count == (size_t)n->val2);
}

/* ------------------------------------------------------------------------- */
/* Thompson NFA Compiler                                                      */
/* ------------------------------------------------------------------------- */

static int32_t prog_emit(PinoReProg* prog, ReInst inst) {
  if (prog->inst_len == prog->inst_cap) {
    size_t old_cap = prog->inst_cap;
    size_t new_cap = old_cap == 0 ? 32 : old_cap * 2;
    if (old_cap == 0) {
      prog->insts = (ReInst*)PINO_ALLOC(pino_cur_alloc, new_cap * sizeof(ReInst), "re insts");
    } else {
      prog->insts = (ReInst*)PINO_REALLOC(pino_cur_alloc, prog->insts, old_cap * sizeof(ReInst),
                                          new_cap * sizeof(ReInst), "re insts");
    }
    prog->inst_cap = new_cap;
  }
  int32_t pc = (int32_t)prog->inst_len;
  prog->insts[prog->inst_len++] = inst;
  return pc;
}

static int32_t prog_add_class_copy(PinoReProg* prog, const ReCharClass* src) {
  if (prog->class_len == prog->class_cap) {
    size_t old_cap = prog->class_cap;
    size_t new_cap = old_cap == 0 ? 8 : old_cap * 2;
    if (old_cap == 0) {
      prog->classes = (ReCharClass*)PINO_ALLOC(pino_cur_alloc, new_cap * sizeof(ReCharClass), "re classes");
    } else {
      prog->classes = (ReCharClass*)PINO_REALLOC(pino_cur_alloc, prog->classes, old_cap * sizeof(ReCharClass),
                                                 new_cap * sizeof(ReCharClass), "re classes");
    }
    prog->class_cap = new_cap;
  }
  ReCharClass copy = *src;
  if (src->len > 0) {
    copy.ranges = (ReRange*)PINO_ALLOC(pino_cur_alloc, src->len * sizeof(ReRange), "re cc copy");
    memcpy(copy.ranges, src->ranges, src->len * sizeof(ReRange));
  } else {
    copy.ranges = NULL;
  }
  int32_t id = (int32_t)prog->class_len;
  prog->classes[prog->class_len++] = copy;
  return id;
}

static void compile_node(const AstPool* a, int32_t id, PinoReProg* prog) {
  const AstNode* n = &a->nodes[id];
  switch (n->kind) {
    case AST_EMPTY:
      break;
    case AST_LITERAL: {
      ReInst ins = {n->flag ? (uint8_t)INST_RUNE_FOLD : (uint8_t)INST_RUNE, n->val, 0};
      prog_emit(prog, ins);
      break;
    }
    case AST_ANY: {
      ReInst ins = {n->flag ? (uint8_t)INST_ANY_NL : (uint8_t)INST_ANY, 0, 0};
      prog_emit(prog, ins);
      break;
    }
    case AST_CLASS: {
      int32_t cid = prog_add_class_copy(prog, &a->classes[n->val]);
      ReInst ins = {(uint8_t)INST_CLASS, cid, 0};
      prog_emit(prog, ins);
      break;
    }
    case AST_ASSERT: {
      prog->has_asserts = true;
      ReInst ins = {(uint8_t)INST_ASSERT, (int32_t)n->flag, 0};
      prog_emit(prog, ins);
      break;
    }
    case AST_CONCAT:
      for (int32_t i = 0; i < n->val2; i++) {
        compile_node(a, a->children[n->val + i], prog);
      }
      break;
    case AST_ALT: {
      int32_t count = n->val2;
      int32_t* fixups = (int32_t*)PINO_ALLOC(pino_cur_alloc, (size_t)(count - 1) * sizeof(int32_t), "re fixups");
      for (int32_t i = 0; i < count - 1; i++) {
        int32_t split_pc = prog_emit(prog, (ReInst){(uint8_t)INST_SPLIT, 0, 0});
        prog->insts[split_pc].arg = split_pc + 1;
        compile_node(a, a->children[n->val + i], prog);
        fixups[i] = prog_emit(prog, (ReInst){(uint8_t)INST_JUMP, 0, 0});
        prog->insts[split_pc].out2 = (int32_t)prog->inst_len;
      }
      compile_node(a, a->children[n->val + count - 1], prog);
      int32_t end_pc = (int32_t)prog->inst_len;
      for (int32_t i = 0; i < count - 1; i++) {
        prog->insts[fixups[i]].arg = end_pc;
      }
      pino_dealloc(pino_cur_alloc, fixups, (size_t)(count - 1) * sizeof(int32_t));
      break;
    }
    case AST_REPEAT: {
      bool greedy = n->flag != 0;
      for (int32_t i = 0; i < n->val; i++) {
        compile_node(a, n->child, prog);
      }
      if (n->val2 < 0) {
        int32_t split_pc = prog_emit(prog, (ReInst){(uint8_t)INST_SPLIT, 0, 0});
        int32_t body_pc = (int32_t)prog->inst_len;
        compile_node(a, n->child, prog);
        prog_emit(prog, (ReInst){(uint8_t)INST_JUMP, split_pc, 0});
        int32_t end_pc = (int32_t)prog->inst_len;
        prog->insts[split_pc].arg = greedy ? body_pc : end_pc;
        prog->insts[split_pc].out2 = greedy ? end_pc : body_pc;
      } else {
        int32_t opt = n->val2 - n->val;
        for (int32_t i = 0; i < opt; i++) {
          int32_t split_pc = prog_emit(prog, (ReInst){(uint8_t)INST_SPLIT, 0, 0});
          int32_t body_pc = (int32_t)prog->inst_len;
          compile_node(a, n->child, prog);
          int32_t end_pc = (int32_t)prog->inst_len;
          prog->insts[split_pc].arg = greedy ? body_pc : end_pc;
          prog->insts[split_pc].out2 = greedy ? end_pc : body_pc;
        }
      }
      break;
    }
  }
}

static void refine_ascii_classes(uint8_t cls[128], int32_t* num_classes, uint64_t mask_lo, uint64_t mask_hi) {
  if ((mask_lo == 0 && mask_hi == 0) || (mask_lo == UINT64_MAX && mask_hi == UINT64_MAX)) {
    return;
  }
  uint8_t flags[128];
  memset(flags, 0, sizeof(flags));
  for (int b = 0; b < 128; b++) {
    bool m = b < 64 ? (((mask_lo >> (unsigned)b) & 1ULL) != 0) : (((mask_hi >> (unsigned)(b - 64)) & 1ULL) != 0);
    flags[cls[b]] |= m ? 1u : 2u;
  }
  int32_t split_map[128];
  int32_t cur_n = *num_classes;
  for (int32_t c = 0; c < cur_n; c++) {
    if (flags[c] == 3u) {
      split_map[c] = (*num_classes)++;
    } else {
      split_map[c] = -1;
    }
  }
  for (int b = 0; b < 128; b++) {
    int32_t nc = split_map[cls[b]];
    if (nc >= 0) {
      bool m = b < 64 ? (((mask_lo >> (unsigned)b) & 1ULL) != 0) : (((mask_hi >> (unsigned)(b - 64)) & 1ULL) != 0);
      if (m) {
        cls[b] = (uint8_t)nc;
      }
    }
  }
}

static void compute_ascii_byte_classes(PinoReProg* prog) {
  memset(prog->ascii_byte_class, 0, 128);
  int32_t num_classes = 1;
  for (size_t i = 0; i < prog->inst_len; i++) {
    const ReInst* ins = &prog->insts[i];
    if (ins->op == INST_RUNE && ins->arg >= 0 && ins->arg < 128) {
      if (ins->arg < 64) {
        refine_ascii_classes(prog->ascii_byte_class, &num_classes, 1ULL << (unsigned)ins->arg, 0);
      } else {
        refine_ascii_classes(prog->ascii_byte_class, &num_classes, 0, 1ULL << (unsigned)(ins->arg - 64));
      }
    } else if (ins->op == INST_RUNE_FOLD && ins->arg >= 0 && ins->arg < 128) {
      uint64_t lo = 0, hi = 0;
      if (ins->arg < 64) {
        lo |= 1ULL << (unsigned)ins->arg;
      } else {
        hi |= 1ULL << (unsigned)(ins->arg - 64);
      }
      if (ins->arg >= 'a' && ins->arg <= 'z') {
        hi |= 1ULL << (unsigned)(ins->arg - 32 - 64);
      }
      refine_ascii_classes(prog->ascii_byte_class, &num_classes, lo, hi);
    } else if (ins->op == INST_ANY) {
      refine_ascii_classes(prog->ascii_byte_class, &num_classes, 1ULL << (unsigned)'\n', 0);
    } else if (ins->op == INST_CLASS) {
      const ReCharClass* cc = &prog->classes[ins->arg];
      refine_ascii_classes(prog->ascii_byte_class, &num_classes, cc->ascii_lo, cc->ascii_hi);
    }
  }
  prog->num_ascii_classes = num_classes;
  for (int32_t c = 0; c < num_classes; c++) {
    prog->ascii_class_rep[c] = -1;
  }
  for (int b = 0; b < 128; b++) {
    uint8_t c = prog->ascii_byte_class[b];
    if (prog->ascii_class_rep[c] < 0) {
      prog->ascii_class_rep[c] = b;
    }
  }
}

/* ------------------------------------------------------------------------- */
/* ASCII Byte-Class Lazy DFA                                                  */
/* ------------------------------------------------------------------------- */

static uint32_t hash_pcs(const int32_t* pcs, int32_t count) {
  uint32_t h = 2166136261u;
  for (int32_t i = 0; i < count; i++) {
    h ^= (uint32_t)pcs[i];
    h *= 16777619u;
  }
  return h;
}

static int int32_cmp(const void* a, const void* b) {
  int32_t x = *(const int32_t*)a;
  int32_t y = *(const int32_t*)b;
  return (x > y) - (x < y);
}

static int32_t dfa_epsilon_closure(const PinoReProg* prog, const int32_t* seeds, int32_t seed_count,
                                   int32_t* out_pcs, bool* is_match_out) {
  size_t ninst = prog->inst_len;
  uint8_t* seen = (uint8_t*)PINO_ALLOC(pino_cur_alloc, ninst, "re dfa seen");
  int32_t* stack = (int32_t*)PINO_ALLOC(pino_cur_alloc, ninst * sizeof(int32_t), "re dfa stack");
  memset(seen, 0, ninst);

  int32_t sp = 0;
  for (int32_t i = 0; i < seed_count; i++) {
    stack[sp++] = seeds[i];
  }
  int32_t count = 0;
  bool is_match = false;
  while (sp > 0) {
    int32_t pc = stack[--sp];
    if (seen[pc]) {
      continue;
    }
    seen[pc] = 1u;
    const ReInst* ins = &prog->insts[pc];
    if (ins->op == INST_JUMP) {
      stack[sp++] = ins->arg;
    } else if (ins->op == INST_SPLIT) {
      stack[sp++] = ins->out2;
      stack[sp++] = ins->arg;
    } else if (ins->op == INST_MATCH) {
      is_match = true;
    } else {
      out_pcs[count++] = pc;
    }
  }
  pino_dealloc(pino_cur_alloc, stack, ninst * sizeof(int32_t));
  pino_dealloc(pino_cur_alloc, seen, ninst);

  if (count > 1) {
    qsort(out_pcs, (size_t)count, sizeof(int32_t), int32_cmp);
  }
  *is_match_out = is_match;
  return count;
}

static int32_t dfa_get_or_add_state(ReDfa* dfa, const int32_t* sorted_pcs, int32_t count, bool is_match) {
  uint32_t h = hash_pcs(sorted_pcs, count);
  uint32_t mask = PINO_DFA_HASH_CAP - 1u;
  uint32_t slot = h & mask;
  while (dfa->hash_slots[slot] >= 0) {
    int32_t sid = dfa->hash_slots[slot];
    const ReDfaState* st = &dfa->states[sid];
    if (st->hash == h && st->nfa_count == count &&
        (count == 0 || memcmp(st->nfa_pcs, sorted_pcs, (size_t)count * sizeof(int32_t)) == 0) &&
        (dfa->is_match[sid] != 0) == is_match) {
      return sid;
    }
    slot = (slot + 1u) & mask;
  }

  if (dfa->state_count >= PINO_DFA_MAX_STATES) {
    return -1;
  }
  if (dfa->state_count == dfa->state_cap) {
    int32_t old_cap = dfa->state_cap;
    int32_t new_cap = old_cap * 2;
    if (new_cap > PINO_DFA_MAX_STATES) {
      new_cap = PINO_DFA_MAX_STATES;
    }
    dfa->states = (ReDfaState*)PINO_REALLOC(pino_cur_alloc, dfa->states, (size_t)old_cap * sizeof(ReDfaState),
                                            (size_t)new_cap * sizeof(ReDfaState), "re dfa states");
    dfa->is_match = (uint8_t*)PINO_REALLOC(pino_cur_alloc, dfa->is_match, (size_t)old_cap, (size_t)new_cap,
                                           "re dfa match");
    size_t old_t = (size_t)old_cap * (size_t)dfa->num_classes;
    size_t new_t = (size_t)new_cap * (size_t)dfa->num_classes;
    dfa->trans = (int32_t*)PINO_REALLOC(pino_cur_alloc, dfa->trans, old_t * sizeof(int32_t),
                                        new_t * sizeof(int32_t), "re dfa trans");
    for (size_t i = old_t; i < new_t; i++) {
      dfa->trans[i] = PINO_DFA_UNKNOWN;
    }
    dfa->state_cap = new_cap;
  }

  int32_t id = dfa->state_count++;
  int32_t* pcs_copy = NULL;
  if (count > 0) {
    pcs_copy = (int32_t*)PINO_ALLOC(pino_cur_alloc, (size_t)count * sizeof(int32_t), "re dfa pcs");
    memcpy(pcs_copy, sorted_pcs, (size_t)count * sizeof(int32_t));
  }
  dfa->states[id].nfa_pcs = pcs_copy;
  dfa->states[id].nfa_count = count;
  dfa->states[id].hash = h;
  dfa->is_match[id] = is_match ? 1u : 0u;
  dfa->hash_slots[slot] = id;
  return id;
}

static ReDfa* dfa_create(const PinoReProg* prog, bool anchored) {
  ReDfa* dfa = (ReDfa*)PINO_ALLOC(pino_cur_alloc, sizeof(ReDfa), "re dfa");
  dfa->anchored = anchored;
  dfa->num_classes = prog->num_ascii_classes;
  dfa->state_count = 0;
  dfa->state_cap = 32;
  dfa->states = (ReDfaState*)PINO_ALLOC(pino_cur_alloc, (size_t)dfa->state_cap * sizeof(ReDfaState), "re dfa states");
  dfa->is_match = (uint8_t*)PINO_ALLOC(pino_cur_alloc, (size_t)dfa->state_cap, "re dfa match");
  size_t tlen = (size_t)dfa->state_cap * (size_t)dfa->num_classes;
  dfa->trans = (int32_t*)PINO_ALLOC(pino_cur_alloc, tlen * sizeof(int32_t), "re dfa trans");
  for (size_t i = 0; i < tlen; i++) {
    dfa->trans[i] = PINO_DFA_UNKNOWN;
  }
  for (size_t i = 0; i < PINO_DFA_HASH_CAP; i++) {
    dfa->hash_slots[i] = -1;
  }

  int32_t seed = 0;
  int32_t* pcs = (int32_t*)PINO_ALLOC(pino_cur_alloc, prog->inst_len * sizeof(int32_t), "re dfa init pcs");
  bool is_match = false;
  int32_t n = dfa_epsilon_closure(prog, &seed, 1, pcs, &is_match);
  (void)dfa_get_or_add_state(dfa, pcs, n, is_match);
  pino_dealloc(pino_cur_alloc, pcs, prog->inst_len * sizeof(int32_t));
  return dfa;
}

static void dfa_free(ReDfa* dfa) {
  if (dfa == NULL) {
    return;
  }
  for (int32_t i = 0; i < dfa->state_count; i++) {
    if (dfa->states[i].nfa_pcs != NULL) {
      pino_dealloc(pino_cur_alloc, dfa->states[i].nfa_pcs, (size_t)dfa->states[i].nfa_count * sizeof(int32_t));
    }
  }
  pino_dealloc(pino_cur_alloc, dfa->states, (size_t)dfa->state_cap * sizeof(ReDfaState));
  pino_dealloc(pino_cur_alloc, dfa->is_match, (size_t)dfa->state_cap);
  pino_dealloc(pino_cur_alloc, dfa->trans, (size_t)dfa->state_cap * (size_t)dfa->num_classes * sizeof(int32_t));
  pino_dealloc(pino_cur_alloc, dfa, sizeof(ReDfa));
}

static int32_t dfa_compute_trans(const PinoReProg* prog, ReDfa* dfa, int32_t state_id, int32_t byte_cls) {
  if (dfa->state_count >= PINO_DFA_MAX_STATES) {
    return PINO_DFA_UNKNOWN;
  }
  const ReDfaState* st = &dfa->states[state_id];
  int32_t rep = prog->ascii_class_rep[byte_cls];
  int32_t rep_fold = (rep >= 'A' && rep <= 'Z') ? (rep + 32) : rep;

  size_t max_seeds = (size_t)st->nfa_count + 1;
  int32_t* seeds = (int32_t*)PINO_ALLOC(pino_cur_alloc, max_seeds * sizeof(int32_t), "re dfa seeds");
  int32_t sc = 0;

  for (int32_t i = 0; i < st->nfa_count; i++) {
    int32_t pc = st->nfa_pcs[i];
    const ReInst* ins = &prog->insts[pc];
    bool ok = false;
    switch (ins->op) {
      case INST_RUNE:
        ok = (rep == ins->arg);
        break;
      case INST_RUNE_FOLD:
        ok = (rep_fold == ins->arg);
        break;
      case INST_ANY:
        ok = (rep != '\n');
        break;
      case INST_ANY_NL:
        ok = true;
        break;
      case INST_CLASS:
        ok = cc_matches_ascii(&prog->classes[ins->arg], rep);
        break;
      default:
        break;
    }
    if (ok) {
      seeds[sc++] = pc + 1;
    }
  }

  size_t t_idx = (size_t)state_id * (size_t)dfa->num_classes + (size_t)byte_cls;
  if (sc == 0) {
    pino_dealloc(pino_cur_alloc, seeds, max_seeds * sizeof(int32_t));
    int32_t res = dfa->anchored ? PINO_DFA_DEAD : PINO_DFA_RESET_BIT;
    dfa->trans[t_idx] = res;
    return res;
  }
  if (!dfa->anchored) {
    seeds[sc++] = 0;
  }

  int32_t* pcs = (int32_t*)PINO_ALLOC(pino_cur_alloc, prog->inst_len * sizeof(int32_t), "re dfa next pcs");
  bool is_match = false;
  int32_t n = dfa_epsilon_closure(prog, seeds, sc, pcs, &is_match);
  pino_dealloc(pino_cur_alloc, seeds, max_seeds * sizeof(int32_t));

  int32_t next_id = dfa_get_or_add_state(dfa, pcs, n, is_match);
  pino_dealloc(pino_cur_alloc, pcs, prog->inst_len * sizeof(int32_t));
  if (next_id < 0) {
    return PINO_DFA_UNKNOWN;
  }
  dfa->trans[t_idx] = next_id;
  return next_id;
}

static int dfa_has_match_ascii(const PinoReProg* prog, ReDfa* dfa, const char* text, size_t text_len, size_t from_pos) {
  if (dfa->is_match[0]) {
    return 1;
  }
  const uint8_t* bc = prog->ascii_byte_class;
  int32_t* trans = dfa->trans;
  uint8_t* is_match = dfa->is_match;
  size_t stride = (size_t)dfa->num_classes;
  int32_t state = 0;

  for (size_t i = from_pos; i < text_len; i++) {
    unsigned char b = (unsigned char)text[i];
    if (b >= 0x80u) {
      return -1;
    }
    int32_t cls = (int32_t)bc[b];
    int32_t next = trans[(size_t)state * stride + (size_t)cls];
    if (next == PINO_DFA_UNKNOWN) {
      next = dfa_compute_trans(prog, dfa, state, cls);
      if (next == PINO_DFA_UNKNOWN) {
        return -1;
      }
      trans = dfa->trans;
      is_match = dfa->is_match;
    }
    state = next & PINO_DFA_STATE_MASK;
    if (is_match[state]) {
      return 1;
    }
  }
  return 0;
}

static int dfa_scan_for_match_ascii(const PinoReProg* prog, ReDfa* dfa, const char* text, size_t text_len,
                                    size_t from_pos, size_t* window_start, size_t* first_match_end) {
  *window_start = from_pos;
  *first_match_end = from_pos;
  if (dfa->is_match[0]) {
    return 1;
  }
  const uint8_t* bc = prog->ascii_byte_class;
  int32_t* trans = dfa->trans;
  uint8_t* is_match = dfa->is_match;
  size_t stride = (size_t)dfa->num_classes;
  int32_t state = 0;

  for (size_t i = from_pos; i < text_len; i++) {
    unsigned char b = (unsigned char)text[i];
    if (b >= 0x80u) {
      return -1;
    }
    int32_t cls = (int32_t)bc[b];
    int32_t next = trans[(size_t)state * stride + (size_t)cls];
    if (next == PINO_DFA_UNKNOWN) {
      next = dfa_compute_trans(prog, dfa, state, cls);
      if (next == PINO_DFA_UNKNOWN) {
        return -1;
      }
      trans = dfa->trans;
      is_match = dfa->is_match;
    }
    if ((next & PINO_DFA_RESET_BIT) != 0) {
      *window_start = i + 1;
    }
    state = next & PINO_DFA_STATE_MASK;
    if (is_match[state]) {
      *first_match_end = i + 1;
      return 1;
    }
  }
  return 0;
}

/* Returns 1 on match (*end_out set), 0 on definitive no match, -1 to fall back to Pike VM. */
static int dfa_anchored_longest_ascii(const PinoReProg* prog, ReDfa* dfa, const char* text, size_t text_len,
                                      size_t start_pos, size_t* end_out) {
  const uint8_t* bc = prog->ascii_byte_class;
  int32_t* trans = dfa->trans;
  uint8_t* is_match = dfa->is_match;
  size_t stride = (size_t)dfa->num_classes;
  int32_t state = 0;
  bool found = is_match[0] != 0;
  size_t best_end = start_pos;

  for (size_t i = start_pos; i < text_len; i++) {
    unsigned char b = (unsigned char)text[i];
    if (b >= 0x80u) {
      return -1;
    }
    int32_t cls = (int32_t)bc[b];
    int32_t next = trans[(size_t)state * stride + (size_t)cls];
    if (next == PINO_DFA_UNKNOWN) {
      next = dfa_compute_trans(prog, dfa, state, cls);
      if (next == PINO_DFA_UNKNOWN) {
        return -1;
      }
      trans = dfa->trans;
      is_match = dfa->is_match;
    }
    if (next == PINO_DFA_DEAD) {
      if (found) {
        *end_out = best_end;
        return 1;
      }
      return 0;
    }
    state = next & PINO_DFA_STATE_MASK;
    if (is_match[state]) {
      found = true;
      best_end = i + 1;
    }
  }

  if (found) {
    *end_out = best_end;
    return 1;
  }
  return 0;
}

/* ------------------------------------------------------------------------- */
/* Pike VM (Russ Cox 2009) — guaranteed O(M * N), exact leftmost-first       */
/* ------------------------------------------------------------------------- */

typedef struct PikeThread {
  int32_t pc;
  size_t start_pos;
} PikeThread;

static inline bool check_assert(uint32_t kind, size_t byte_pos, size_t text_len, int32_t prev_rune, int32_t cur_rune) {
  switch (kind) {
    case RE_ASSERT_TEXT_START:
      return byte_pos == 0;
    case RE_ASSERT_TEXT_END:
      return byte_pos == text_len;
    case RE_ASSERT_LINE_START:
      return byte_pos == 0 || prev_rune == '\n';
    case RE_ASSERT_LINE_END:
      return byte_pos == text_len || cur_rune == '\n';
    case RE_ASSERT_WORD_BOUNDARY:
      return re_is_ascii_word(prev_rune) != re_is_ascii_word(cur_rune);
    case RE_ASSERT_NOT_WORD_BOUNDARY:
      return re_is_ascii_word(prev_rune) == re_is_ascii_word(cur_rune);
    default:
      return false;
  }
}

static inline bool inst_matches_rune(const PinoReProg* prog, const ReInst* ins, int32_t rune) {
  switch (ins->op) {
    case INST_RUNE:
      return rune == ins->arg;
    case INST_RUNE_FOLD:
      return pino_casefold_canonical(rune) == ins->arg;
    case INST_ANY:
      return rune != '\n';
    case INST_ANY_NL:
      return true;
    case INST_CLASS:
      return cc_matches(&prog->classes[ins->arg], rune);
    default:
      return false;
  }
}

static void pike_add_thread(const PinoReProg* prog, PikeThread* list, size_t* count, uint32_t* visited,
                            int32_t* stack, uint32_t gen, int32_t start_pc, size_t start_pos, size_t byte_pos,
                            size_t text_len, int32_t prev_rune, int32_t cur_rune) {
  size_t sp = 0;
  stack[sp++] = start_pc;
  while (sp > 0) {
    int32_t pc = stack[--sp];
    if (visited[pc] == gen) {
      continue;
    }
    visited[pc] = gen;
    const ReInst* ins = &prog->insts[pc];
    if (ins->op == INST_JUMP) {
      stack[sp++] = ins->arg;
    } else if (ins->op == INST_SPLIT) {
      stack[sp++] = ins->out2;
      stack[sp++] = ins->arg;
    } else if (ins->op == INST_ASSERT) {
      if (check_assert((uint32_t)ins->arg, byte_pos, text_len, prev_rune, cur_rune)) {
        stack[sp++] = pc + 1;
      }
    } else {
      list[*count].pc = pc;
      list[*count].start_pos = start_pos;
      (*count)++;
    }
  }
}

static bool run_pike_vm(const PinoReProg* prog, const char* text, size_t text_len, size_t from_pos, bool anchored,
                        size_t* start_out, size_t* end_out) {
  size_t ninst = prog->inst_len;
  PikeThread* clist = (PikeThread*)PINO_ALLOC(pino_cur_alloc, ninst * sizeof(PikeThread), "re clist");
  PikeThread* nlist = (PikeThread*)PINO_ALLOC(pino_cur_alloc, ninst * sizeof(PikeThread), "re nlist");
  uint32_t* visited = (uint32_t*)PINO_ALLOC(pino_cur_alloc, ninst * sizeof(uint32_t), "re visited");
  int32_t* stack = (int32_t*)PINO_ALLOC(pino_cur_alloc, ninst * sizeof(int32_t), "re stack");
  memset(visited, 0, ninst * sizeof(uint32_t));

  size_t ccount = 0;
  uint32_t gen = 0;
  bool matched = false;
  size_t best_start = 0;
  size_t best_end = 0;

  size_t byte_pos = from_pos;
  int32_t prev_rune = re_decode_before(text, byte_pos);

  for (;;) {
    int32_t cur_rune = -1;
    int width = 0;
    if (byte_pos < text_len) {
      cur_rune = pino_utf8_decode(text + byte_pos, text + text_len, &width);
    }

    if (!matched && (!anchored || byte_pos == from_pos)) {
      gen++;
      if (gen == 0) {
        memset(visited, 0, ninst * sizeof(uint32_t));
        gen = 1;
      }
      for (size_t i = 0; i < ccount; i++) {
        visited[clist[i].pc] = gen;
      }
      pike_add_thread(prog, clist, &ccount, visited, stack, gen, 0, byte_pos, byte_pos, text_len, prev_rune, cur_rune);
    }

    if (ccount == 0 && (matched || anchored || byte_pos >= text_len)) {
      break;
    }

    size_t next_byte_pos = byte_pos + (size_t)width;
    int32_t next_rune = -1;
    if (next_byte_pos < text_len) {
      int dummy_w = 0;
      next_rune = pino_utf8_decode(text + next_byte_pos, text + text_len, &dummy_w);
    }

    gen++;
    if (gen == 0) {
      memset(visited, 0, ninst * sizeof(uint32_t));
      gen = 1;
    }
    size_t ncount = 0;

    for (size_t i = 0; i < ccount; i++) {
      PikeThread t = clist[i];
      const ReInst* ins = &prog->insts[t.pc];
      if (ins->op == INST_MATCH) {
        matched = true;
        best_start = t.start_pos;
        best_end = byte_pos;
        break;
      }
      if (cur_rune < 0) {
        continue;
      }
      if (inst_matches_rune(prog, ins, cur_rune)) {
        pike_add_thread(prog, nlist, &ncount, visited, stack, gen, t.pc + 1, t.start_pos, next_byte_pos, text_len,
                        cur_rune, next_rune);
      }
    }

    PikeThread* tmp = clist;
    clist = nlist;
    nlist = tmp;
    ccount = ncount;

    if (byte_pos >= text_len) {
      break;
    }
    if (ccount == 0 && (matched || anchored)) {
      break;
    }
    prev_rune = cur_rune;
    byte_pos = next_byte_pos;
  }

  pino_dealloc(pino_cur_alloc, stack, ninst * sizeof(int32_t));
  pino_dealloc(pino_cur_alloc, visited, ninst * sizeof(uint32_t));
  pino_dealloc(pino_cur_alloc, nlist, ninst * sizeof(PikeThread));
  pino_dealloc(pino_cur_alloc, clist, ninst * sizeof(PikeThread));

  if (matched) {
    if (start_out != NULL) {
      *start_out = best_start;
    }
    if (end_out != NULL) {
      *end_out = best_end;
    }
    return true;
  }
  return false;
}

/* ------------------------------------------------------------------------- */
/* Literal substring search (memchr + memcmp)                                 */
/* ------------------------------------------------------------------------- */

static bool find_literal_bytes(const char* text, size_t text_len, size_t from_pos,
                               const char* needle, size_t needle_len, size_t* pos_out) {
  if (needle_len == 0) {
    *pos_out = from_pos;
    return true;
  }
  if (from_pos > text_len || needle_len > text_len - from_pos) {
    return false;
  }
  unsigned char first = (unsigned char)needle[0];
  const char* p = text + from_pos;
  const char* end = text + text_len - needle_len + 1;
  while (p < end) {
    const char* hit = (const char*)memchr(p, (int)first, (size_t)(end - p));
    if (hit == NULL) {
      return false;
    }
    if (needle_len == 1 || memcmp(hit + 1, needle + 1, needle_len - 1) == 0) {
      *pos_out = (size_t)(hit - text);
      return true;
    }
    p = hit + 1;
  }
  return false;
}

/* ------------------------------------------------------------------------- */
/* Public low-level C API (pino_re_*)                                         */
/* ------------------------------------------------------------------------- */

PinoReProg* pino_re_compile(const char* pat, size_t pat_len, char* err_buf, size_t err_cap) {
  AstPool ast;
  ast_init(&ast);

  ReParser parser;
  parser.src = pat != NULL ? pat : "";
  parser.len = pat_len;
  parser.pos = 0;
  parser.ast = &ast;
  parser.has_err = false;
  parser.err_buf = err_buf;
  parser.err_cap = err_cap;

  uint32_t flags = 0;
  int32_t root = parse_alt(&parser, &flags, false);
  if (!parser.has_err && parser.pos < parser.len) {
    set_error_reason(&parser, "unexpected )");
  }
  if (parser.has_err) {
    ast_free(&ast);
    return NULL;
  }

  root = ast_simplify(&ast, root);

  PinoReProg* prog = (PinoReProg*)PINO_ALLOC(pino_cur_alloc, sizeof(PinoReProg), "re prog");
  memset(prog, 0, sizeof(*prog));

  extract_literal_prefix(&ast, root, prog);
  prog->is_anchored_start = ast_is_anchored_start(&ast, root);
  bool unambiguous = ast_is_unambiguous_greedy(&ast, root);

  compile_node(&ast, root, prog);
  prog_emit(prog, (ReInst){(uint8_t)INST_MATCH, 0, 0});
  ast_free(&ast);

  prog->can_use_dfa = !prog->has_asserts && prog->inst_len <= 4096;
  prog->is_unambiguous_greedy = unambiguous && !prog->has_asserts;

  if (prog->can_use_dfa && !prog->is_exact_literal) {
    compute_ascii_byte_classes(prog);
    prog->unanchored_dfa = dfa_create(prog, false);
    if (prog->is_unambiguous_greedy) {
      prog->anchored_dfa = dfa_create(prog, true);
    }
  }

  return prog;
}

void pino_re_free(PinoReProg* prog) {
  if (prog == NULL) {
    return;
  }
  dfa_free(prog->unanchored_dfa);
  dfa_free(prog->anchored_dfa);
  if (prog->literal_prefix != NULL) {
    pino_dealloc(pino_cur_alloc, prog->literal_prefix, prog->literal_prefix_len);
  }
  for (size_t i = 0; i < prog->class_len; i++) {
    cc_free(&prog->classes[i]);
  }
  if (prog->classes != NULL) {
    pino_dealloc(pino_cur_alloc, prog->classes, prog->class_cap * sizeof(ReCharClass));
  }
  if (prog->insts != NULL) {
    pino_dealloc(pino_cur_alloc, prog->insts, prog->inst_cap * sizeof(ReInst));
  }
  pino_dealloc(pino_cur_alloc, prog, sizeof(PinoReProg));
}

bool pino_re_is_anchored_start(const PinoReProg* prog) { return prog->is_anchored_start; }

bool pino_re_has_match(PinoReProg* prog, const char* text, size_t text_len) {
  if (prog->is_exact_literal) {
    size_t pos = 0;
    return find_literal_bytes(text, text_len, 0, prog->literal_prefix, prog->literal_prefix_len, &pos);
  }
  if (prog->is_anchored_start) {
    return run_pike_vm(prog, text, text_len, 0, true, NULL, NULL);
  }
  if (prog->unanchored_dfa != NULL) {
    int dfa_res = dfa_has_match_ascii(prog, prog->unanchored_dfa, text, text_len, 0);
    if (dfa_res >= 0) {
      return dfa_res != 0;
    }
  }
  return pino_re_find_from(prog, text, text_len, 0, NULL, NULL);
}

bool pino_re_match_prefix(PinoReProg* prog, const char* text, size_t text_len, size_t* end_out) {
  if (prog->is_exact_literal) {
    if (text_len >= prog->literal_prefix_len &&
        memcmp(text, prog->literal_prefix, prog->literal_prefix_len) == 0) {
      if (end_out != NULL) {
        *end_out = prog->literal_prefix_len;
      }
      return true;
    }
    return false;
  }
  if (prog->literal_prefix_len > 0 &&
      (text_len < prog->literal_prefix_len || memcmp(text, prog->literal_prefix, prog->literal_prefix_len) != 0)) {
    return false;
  }
  if (prog->anchored_dfa != NULL) {
    size_t end = 0;
    int r = dfa_anchored_longest_ascii(prog, prog->anchored_dfa, text, text_len, 0, &end);
    if (r >= 0) {
      if (r == 1 && end_out != NULL) {
        *end_out = end;
      }
      return r == 1;
    }
  }
  return run_pike_vm(prog, text, text_len, 0, true, NULL, end_out);
}

bool pino_re_find(PinoReProg* prog, const char* text, size_t text_len, size_t* start_out, size_t* end_out) {
  return pino_re_find_from(prog, text, text_len, 0, start_out, end_out);
}

bool pino_re_find_from(PinoReProg* prog, const char* text, size_t text_len, size_t from_pos,
                       size_t* start_out, size_t* end_out) {
  if (prog->is_exact_literal) {
    size_t pos = 0;
    if (find_literal_bytes(text, text_len, from_pos, prog->literal_prefix, prog->literal_prefix_len, &pos)) {
      if (start_out != NULL) {
        *start_out = pos;
      }
      if (end_out != NULL) {
        *end_out = pos + prog->literal_prefix_len;
      }
      return true;
    }
    return false;
  }

  if (prog->is_anchored_start) {
    if (from_pos > 0) {
      return false;
    }
    return run_pike_vm(prog, text, text_len, 0, true, start_out, end_out);
  }

  if (prog->literal_prefix_len > 0) {
    size_t search_pos = from_pos;
    while (search_pos <= text_len && prog->literal_prefix_len <= text_len - search_pos) {
      size_t cand = 0;
      if (!find_literal_bytes(text, text_len, search_pos, prog->literal_prefix, prog->literal_prefix_len, &cand)) {
        return false;
      }
      if (prog->anchored_dfa != NULL) {
        size_t end = 0;
        int r = dfa_anchored_longest_ascii(prog, prog->anchored_dfa, text, text_len, cand, &end);
        if (r == 1) {
          if (start_out != NULL) {
            *start_out = cand;
          }
          if (end_out != NULL) {
            *end_out = end;
          }
          return true;
        }
        if (r == 0) {
          search_pos = pino_re_next_rune_pos(text, text_len, cand);
          continue;
        }
      }
      if (run_pike_vm(prog, text, text_len, cand, true, start_out, end_out)) {
        return true;
      }
      search_pos = pino_re_next_rune_pos(text, text_len, cand);
    }
    return false;
  }

  if (prog->unanchored_dfa != NULL) {
    size_t window_start = from_pos;
    size_t first_match_end = from_pos;
    int status = dfa_scan_for_match_ascii(prog, prog->unanchored_dfa, text, text_len, from_pos,
                                          &window_start, &first_match_end);
    if (status == 0) {
      return false;
    }
    if (status == 1) {
      if (prog->anchored_dfa != NULL && first_match_end > 0 && window_start == first_match_end - 1) {
        size_t end = 0;
        int r = dfa_anchored_longest_ascii(prog, prog->anchored_dfa, text, text_len, window_start, &end);
        if (r == 1) {
          if (start_out != NULL) {
            *start_out = window_start;
          }
          if (end_out != NULL) {
            *end_out = end;
          }
          return true;
        }
      }
      return run_pike_vm(prog, text, text_len, window_start, false, start_out, end_out);
    }
    return run_pike_vm(prog, text, text_len, window_start, false, start_out, end_out);
  }

  return run_pike_vm(prog, text, text_len, from_pos, false, start_out, end_out);
}

/* ------------------------------------------------------------------------- */
/* Pino runtime entry points (pino_rt.h)                                      */
/* ------------------------------------------------------------------------- */

static size_t re_strbuf_size(int64_t cap) { return sizeof(PinoStrBuf) + (size_t)cap + 1; }

PinoRegex* pino_regex_compile(PinoString pattern, PinoLoc loc) {
  size_t err_cap = (size_t)pattern.byte_len + 256;
  char* err_buf = (char*)PINO_ALLOC(pino_cur_alloc, err_cap, "regex error msg");
  err_buf[0] = '\0';

  PinoReProg* prog = pino_re_compile(pattern.ptr, (size_t)pattern.byte_len, err_buf, err_cap);
  if (prog == NULL) {
    PinoString msg = pino_str_from_bytes(err_buf, (int64_t)strlen(err_buf));
    pino_dealloc(pino_cur_alloc, err_buf, err_cap);
    pino_panic_at(msg, loc);
  }
  pino_dealloc(pino_cur_alloc, err_buf, err_cap);

  PinoRegex* r = (PinoRegex*)PINO_RC_ALLOC(pino_cur_alloc, sizeof(PinoRegex), 0, 0, "regex");
  r->pattern = pino_str_to_owned(pattern);
  r->prog = prog;
  return r;
}

void pino_regex_release(PinoRegex* r) {
  if (r == NULL || !pino_rc_dec(&r->hdr)) {
    return;
  }
  pino_str_release(r->pattern);
  pino_re_free(r->prog);
  pino_dealloc(pino_cur_alloc, r, sizeof(PinoRegex));
}

PinoString pino_regex_pattern(const PinoRegex* r) { return pino_str_retain(r->pattern); }

bool pino_regex_has_match(const PinoRegex* r, PinoString text) {
  return pino_re_has_match(r->prog, text.ptr, (size_t)text.byte_len);
}

PinoString pino_regex_match_prefix(const PinoRegex* r, PinoString text) {
  size_t end = 0;
  if (pino_re_match_prefix(r->prog, text.ptr, (size_t)text.byte_len, &end)) {
    PinoString m = {text.owner, text.ptr, (int64_t)end};
    return pino_str_retain(m);
  }
  return PINO_STR_EMPTY;
}

PinoString pino_regex_find(const PinoRegex* r, PinoString text) {
  size_t start = 0;
  size_t end = 0;
  if (pino_re_find(r->prog, text.ptr, (size_t)text.byte_len, &start, &end)) {
    PinoString m = {text.owner, text.ptr + start, (int64_t)(end - start)};
    return pino_str_retain(m);
  }
  return PINO_STR_EMPTY;
}

PinoVec_string* pino_regex_find_all(const PinoRegex* r, PinoString text) {
  PinoVec_string* v = &pino_vec_string_empty;
  size_t text_len = (size_t)text.byte_len;
  size_t pos = 0;
  bool has_prev_end = false;
  size_t prev_match_end = 0;

  while (pos <= text_len) {
    size_t m_start = 0;
    size_t m_end = 0;
    if (!pino_re_find_from(r->prog, text.ptr, text_len, pos, &m_start, &m_end)) {
      break;
    }
    if (m_start == m_end && has_prev_end && m_start == prev_match_end) {
      if (pos >= text_len) {
        break;
      }
      pos = pino_re_next_rune_pos(text.ptr, text_len, pos);
      continue;
    }

    PinoString m = {text.owner, text.ptr + m_start, (int64_t)(m_end - m_start)};
    pino_vec_string_push(&v, pino_str_retain(m));
    has_prev_end = true;
    prev_match_end = m_end;

    if (m_end > m_start) {
      pos = m_end;
    } else {
      if (m_end >= text_len) {
        break;
      }
      pos = pino_re_next_rune_pos(text.ptr, text_len, m_end);
    }

    if (pino_re_is_anchored_start(r->prog)) {
      break;
    }
  }
  return v;
}

typedef struct ReOutBuf {
  PinoStrBuf* buf;
  int64_t len;
} ReOutBuf;

static void re_out_append(ReOutBuf* b, const char* p, int64_t n) {
  if (n <= 0) {
    return;
  }
  if (b->len + n > b->buf->cap) {
    int64_t cap = b->buf->cap;
    int64_t new_cap = cap * 2;
    if (new_cap < b->len + n) {
      new_cap = b->len + n;
    }
    b->buf = (PinoStrBuf*)PINO_REALLOC(pino_cur_alloc, b->buf, re_strbuf_size(cap),
                                       re_strbuf_size(new_cap), "string buffer");
    b->buf->cap = new_cap;
  }
  memcpy(b->buf->data + b->len, p, (size_t)n);
  b->len += n;
}

PinoString pino_regex_replace(const PinoRegex* r, PinoString text, PinoString repl) {
  size_t text_len = (size_t)text.byte_len;
  size_t pos = 0;
  size_t copy_from = 0;
  bool has_prev_end = false;
  size_t prev_match_end = 0;
  bool any_match = false;
  ReOutBuf out = {NULL, 0};

  while (pos <= text_len) {
    size_t m_start = 0;
    size_t m_end = 0;
    if (!pino_re_find_from(r->prog, text.ptr, text_len, pos, &m_start, &m_end)) {
      break;
    }
    if (m_start == m_end && has_prev_end && m_start == prev_match_end) {
      if (pos >= text_len) {
        break;
      }
      pos = pino_re_next_rune_pos(text.ptr, text_len, pos);
      continue;
    }

    if (!any_match) {
      out.buf = pino_strbuf_alloc(text.byte_len + repl.byte_len + 16);
      out.len = 0;
      any_match = true;
    }
    re_out_append(&out, text.ptr + copy_from, (int64_t)(m_start - copy_from));
    re_out_append(&out, repl.ptr, repl.byte_len);
    copy_from = m_end;
    has_prev_end = true;
    prev_match_end = m_end;

    if (m_end > m_start) {
      pos = m_end;
    } else {
      if (m_end >= text_len) {
        break;
      }
      pos = pino_re_next_rune_pos(text.ptr, text_len, m_end);
    }

    if (pino_re_is_anchored_start(r->prog)) {
      break;
    }
  }

  if (!any_match) {
    return pino_str_retain(text);
  }
  re_out_append(&out, text.ptr + copy_from, (int64_t)(text_len - copy_from));
  if (out.len == 0) {
    pino_str_release(pino_strbuf_seal(out.buf, 0));
    return PINO_STR_EMPTY;
  }
  return pino_strbuf_seal(out.buf, out.len);
}
