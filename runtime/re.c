#include <stdio.h>
#include <assert.h>
#include <ctype.h>
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>

#include "re.h"

#ifdef _WIN32
#define snprintf _snprintf
#endif

enum {
  END, BRANCH, ANY, EXACT, ANYOF, ANYBUT, OPEN, CLOSE, BOL, EOL, STAR, PLUS,
  STARQ, PLUSQ, QUEST, SPACE, NONSPACE, DIGIT
};

static const char *meta_characters = "|.^$*+?()[\\";
static const char *error_no_match = "No match";

static void set_jump_offset(struct slre *r, int pc, int offset) {
  assert(offset < r->code_size);
  if (r->code_size - offset > 0xff) {
    r->error_string = "Jump offset is too big";
  } else {
    r->code[pc] = (unsigned char) (r->code_size - offset);
  }
}

static void emit(struct slre *r, int code) {
  if (r->code_size >= (int) (sizeof(r->code) / sizeof(r->code[0]))) {
    r->error_string = "RE is too long (code overflow)";
  } else {
    r->code[r->code_size++] = (unsigned char) code;
  }
}

static void store_char_in_data(struct slre *r, int ch) {
  if (r->data_size >= (int) sizeof(r->data)) {
    r->error_string = "RE is too long (data overflow)";
  } else {
    r->data[r->data_size++] = ch;
  }
}

static void exact(struct slre *r, const char **re) {
  int  old_data_size = r->data_size;

  while (**re != '\0' && (strchr(meta_characters, **re)) == NULL) {
    store_char_in_data(r, *(*re)++);
  }

  emit(r, EXACT);
  emit(r, old_data_size);
  emit(r, r->data_size - old_data_size);
}

static int get_escape_char(const char **re) {
  int  res;

  switch (*(*re)++) {
    case 'n':  res = '\n';    break;
    case 'r':  res = '\r';    break;
    case 't':  res = '\t';    break;
    case '0':  res = 0;    break;
    case 'S':  res = NONSPACE << 8;  break;
    case 's':  res = SPACE << 8;  break;
    case 'd':  res = DIGIT << 8;  break;
    default:  res = (*re)[-1];  break;
  }

  return res;
}

static void anyof(struct slre *r, const char **re) {
  int  esc, old_data_size = r->data_size, op = ANYOF;

  if (**re == '^') {
    op = ANYBUT;
    (*re)++;
  }

  while (**re != '\0')

    switch (*(*re)++) {
      case ']':
        emit(r, op);
        emit(r, old_data_size);
        emit(r, r->data_size - old_data_size);
        return;
        // NOTREACHED
        break;
      case '\\':
        esc = get_escape_char(re);
        if ((esc & 0xff) == 0) {
          int digits;
          store_char_in_data(r, 0);
          store_char_in_data(r, esc >> 8);
          if ((*re)[-1] == 'd') { //decimal digits
            for (digits = '0'; digits <= '9'; ++digits)
              store_char_in_data(r, digits);
          }
        } else {
          store_char_in_data(r, esc);
        }
        break;
      default:
        if (**re == '-') { //range e.g. a-z
          int digits, ch;
          if ((*re)[-1] < (*re)[1]) {
            ch = (*re)[-1];
            while (ch <= (*re)[1]) {
              store_char_in_data(r, ch);
              ch++;
            }
            *re += 2;
          } else {
            r->error_string = "Invalid character range";
          }
        } else {
          store_char_in_data(r, (*re)[-1]);
        }
        break;
    }

  r->error_string = "No closing ']' bracket";
}

static void relocate(struct slre *r, int begin, int shift) {
  emit(r, END);
  memmove(r->code + begin + shift, r->code + begin, r->code_size - begin);
  r->code_size += shift;
}

static void quantifier(struct slre *r, int prev, int op) {
  if (r->code[prev] == EXACT && r->code[prev + 2] > 1) {
    r->code[prev + 2]--;
    emit(r, EXACT);
    emit(r, r->code[prev + 1] + r->code[prev + 2]);
    emit(r, 1);
    prev = r->code_size - 3;
  }
  relocate(r, prev, 2);
  r->code[prev] = op;
  set_jump_offset(r, prev + 1, prev);
}

static void exact_one_char(struct slre *r, int ch) {
  emit(r, EXACT);
  emit(r, r->data_size);
  emit(r, 1);
  store_char_in_data(r, ch);
}

static void fixup_branch(struct slre *r, int fixup) {
  if (fixup > 0) {
    emit(r, END);
    set_jump_offset(r, fixup, fixup - 2);
  }
}

static void compile(struct slre *r, const char **re) {
  int  op, esc, branch_start, last_op, fixup, cap_no, level;

  fixup = 0;
  level = r->num_caps;
  branch_start = last_op = r->code_size;

  for (;;)
    switch (*(*re)++) {

      case '\0':
        (*re)--;
        return;
        // NOTREACHED
        break;

      case '^':
        emit(r, BOL);
        break;

      case '$':
        emit(r, EOL);
        break;

      case '.':
        last_op = r->code_size;
        emit(r, ANY);
        break;

      case '[':
        last_op = r->code_size;
        anyof(r, re);
        break;

      case '\\':
        last_op = r->code_size;
        esc = get_escape_char(re);
        if (esc & 0xff00) {
          emit(r, esc >> 8);
        } else {
          exact_one_char(r, esc);
        }
        break;

      case '(':
        last_op = r->code_size;
        cap_no = ++r->num_caps;
        emit(r, OPEN);
        emit(r, cap_no);

        compile(r, re);
        if (*(*re)++ != ')') {
          r->error_string = "No closing bracket";
          return;
        }

        emit(r, CLOSE);
        emit(r, cap_no);
        break;

      case ')':
        (*re)--;
        fixup_branch(r, fixup);
        if (level == 0) {
          r->error_string = "Unbalanced brackets";
          return;
        }
        return;
        // NOTREACHED
        break;

      case '+':
      case '*':
        op = (*re)[-1] == '*' ? STAR: PLUS;
        if (**re == '?') {
          (*re)++;
          op = op == STAR ? STARQ : PLUSQ;
        }
        quantifier(r, last_op, op);
        break;

      case '?':
        quantifier(r, last_op, QUEST);
        break;

      case '|':
        fixup_branch(r, fixup);
        relocate(r, branch_start, 3);
        r->code[branch_start] = BRANCH;
        set_jump_offset(r, branch_start + 1, branch_start);
        fixup = branch_start + 2;
        r->code[fixup] = 0xff;
        break;

      default:
        (*re)--;
        last_op = r->code_size;
        exact(r, re);
        break;
    }
}

const char *slre_compile(struct slre *r, const char *re) {
  r->error_string = NULL;
  r->code_size = r->data_size = r->num_caps = r->anchored = 0;

  if (*re == '^') {
    r->anchored++;
  }

  emit(r, OPEN);  // This will capture what matches full RE
  emit(r, 0);

  while (*re != '\0') {
    compile(r, &re);
  }

  if (r->code[2] == BRANCH) {
    fixup_branch(r, 4);
  }

  emit(r, CLOSE);
  emit(r, 0);
  emit(r, END);

  return r->error_string;
}

static const char *match(const struct slre *, int, const char *, int, int *,
                         struct cap *, int caps_size);

static void loop_greedy(const struct slre *r, int pc, const char *s, int len,
                        int *ofs) {
  int  saved_offset, matched_offset;

  saved_offset = matched_offset = *ofs;

  while (!match(r, pc + 2, s, len, ofs, NULL, 0)) {
    saved_offset = *ofs;
    if (!match(r, pc + pc + 1, s, len, ofs, NULL, 0)) {
      matched_offset = saved_offset;
    }
    *ofs = saved_offset;
  }

  *ofs = matched_offset;
}

static void loop_non_greedy(const struct slre *r, int pc, const char *s,
                            int len, int *ofs) {
  int  saved_offset = *ofs;

  while (!match(r, pc + 2, s, len, ofs, NULL, 0)) {
    saved_offset = *ofs;
    if (!match(r, pc + pc + 1, s, len, ofs, NULL, 0))
      break;
  }

  *ofs = saved_offset;
}

static int is_any_of(const unsigned char *p, int len, const char *s, int *ofs) {
  int  i, ch;

  ch = s[*ofs];

  for (i = 0; i < len; i++)
    if (p[i] == ch) {
      (*ofs)++;
      return 1;
    }

  return 0;
}

static int is_any_but(const unsigned char *p, int len, const char *s,
                      int *ofs) {
  int  i, ch;

  ch = s[*ofs];

  for (i = 0; i < len; i++)
    if (p[i] == ch) {
      return 0;
    }

  (*ofs)++;
  return 1;
}

static int lowercase(const char *s) {
  return tolower(* (const unsigned char *) s);
}

static int casecmp(const void *p1, const void *p2, size_t len) {
  const char *s1 = p1, *s2 = p2;
  int diff = 0;

  if (len > 0)
    do {
      diff = lowercase(s1++) - lowercase(s2++);
    } while (diff == 0 && s1[-1] != '\0' && --len > 0);

  return diff;
}

static const char *match(const struct slre *r, int pc, const char *s, int len,
                         int *ofs, struct cap *caps, int caps_size) {
  int n, saved_offset;
  const char *error_string = NULL;
  int (*cmp)(const void *string1, const void *string2, size_t len);

  while (error_string == NULL && r->code[pc] != END) {

    assert(pc < r->code_size);
    assert(pc < (int) (sizeof(r->code) / sizeof(r->code[0])));

    switch (r->code[pc]) {
      case BRANCH:
        saved_offset = *ofs;
        error_string = match(r, pc + 3, s, len, ofs, caps, caps_size);
        if (error_string != NULL) {
          *ofs = saved_offset;
          error_string = match(r, pc + r->code[pc + 1], s, len, ofs, caps,
                               caps_size);
        }
        pc += r->code[pc + 2];
        break;

      case EXACT:
        error_string = error_no_match;
        n = r->code[pc + 2];  // String length
        cmp = r->options & SLRE_CASE_INSENSITIVE ? casecmp : memcmp;
        if (n <= len - *ofs && !cmp(s + *ofs, r->data + r->code[pc + 1], n)) {
          (*ofs) += n;
          error_string = NULL;
        }
        pc += 3;
        break;

      case QUEST:
        error_string = NULL;
        saved_offset = *ofs;
        if (match(r, pc + 2, s, len, ofs, caps, caps_size) != NULL) {
          *ofs = saved_offset;
        }
        pc += r->code[pc + 1];
        break;

      case STAR:
        error_string = NULL;
        loop_greedy(r, pc, s, len, ofs);
        pc += r->code[pc + 1];
        break;

      case STARQ:
        error_string = NULL;
        loop_non_greedy(r, pc, s, len, ofs);
        pc += r->code[pc + 1];
        break;

      case PLUS:
        if ((error_string = match(r, pc + 2, s, len, ofs,
                                  caps, caps_size)) != NULL) {
          break;
        }
        loop_greedy(r, pc, s, len, ofs);
        pc += r->code[pc + 1];
        break;

      case PLUSQ:
        if ((error_string = match(r, pc + 2, s, len, ofs,
                                  caps, caps_size)) != NULL) {
          break;
        }
        loop_non_greedy(r, pc, s, len, ofs);
        pc += r->code[pc + 1];
        break;

      case SPACE:
        error_string = error_no_match;
        if (*ofs < len && isspace(((unsigned char *)s)[*ofs])) {
          (*ofs)++;
          error_string = NULL;
        }
        pc++;
        break;

      case NONSPACE:
        error_string = error_no_match;
        if (*ofs < len && !isspace(((unsigned char *)s)[*ofs])) {
          (*ofs)++;
          error_string = NULL;
        }
        pc++;
        break;

      case DIGIT:
        error_string = error_no_match;
        if (*ofs < len && isdigit(((unsigned char *)s)[*ofs])) {
          (*ofs)++;
          error_string = NULL;
        }
        pc++;
        break;

      case ANY:
        error_string = error_no_match;
        if (*ofs < len) {
          (*ofs)++;
          error_string = NULL;
        }
        pc++;
        break;

      case ANYOF:
        error_string = error_no_match;
        if (*ofs < len)
          error_string = is_any_of(r->data + r->code[pc + 1], r->code[pc + 2],
                                   s, ofs) ? NULL : error_no_match;
        pc += 3;
        break;

      case ANYBUT:
        error_string = error_no_match;
        if (*ofs < len)
          error_string = is_any_but(r->data + r->code[pc + 1], r->code[pc + 2],
                                    s, ofs) ? NULL : error_no_match;
        pc += 3;
        break;

      case BOL:
        error_string = *ofs == 0 ? NULL : error_no_match;
        pc++;
        break;

      case EOL:
        error_string = *ofs == len ? NULL : error_no_match;
        pc++;
        break;

      case OPEN:
        if (caps != NULL) {
          if (caps_size - 2 < r->code[pc + 1]) {
            error_string = "Too many brackets";
          } else {
            caps[r->code[pc + 1]].ptr = s + *ofs;
          }
        }
        pc += 2;
        break;

      case CLOSE:
        if (caps != NULL) {
          assert(r->code[pc + 1] >= 0);
          assert(r->code[pc + 1] < caps_size);
          caps[r->code[pc + 1]].len = (s + *ofs) -
            caps[r->code[pc + 1]].ptr;
        }
        pc += 2;
        break;

      case END:
        pc++;
        break;

      default:
        printf("unknown cmd (%d) at %d\n", r->code[pc], pc);
        assert(0);
        break;
    }
  }

  return error_string;
}

const char *slre_match(const struct slre *r, const char *buf, int len,
                          struct cap *caps, int caps_size) {
  int  i, ofs = 0;
  const char *error_string = error_no_match;

  if (caps != NULL) {
    memset(caps, 0, caps_size * sizeof(caps[0]));
  }

  if (r->anchored) {
    error_string = match(r, 0, buf, len, &ofs, caps, caps_size);
  } else {
    for (i = 0; i < len && error_string != NULL; i++) {
      ofs = i;
      error_string = match(r, 0, buf, len, &ofs, caps, caps_size);
    }
  }

  return error_string;
}
