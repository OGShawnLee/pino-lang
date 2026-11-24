/*
 * pino_regex.h - Pino's linear-time regular expression engine (D-190).
 *
 * Implements RE2/Go-style syntax with guaranteed linear time:
 *   - Thompson NFA + Pike VM (Russ Cox 2009) for exact leftmost-first matching
 *   - Exact-literal and literal-prefix fast paths (memchr + memcmp)
 *   - ASCII byte-class lazy DFA (Russ Cox 2007/2010) with sound TRANS_RESET tracking
 *
 * Written from scratch for Pino Lang (MIT).
 * UTF-8 without BOM, LF line endings, plain C99.
 */
#ifndef PINO_REGEX_H
#define PINO_REGEX_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct PinoReProg PinoReProg;

/*
 * Compiles a UTF-8 regex pattern of `pat_len` bytes.
 * Returns the compiled program on success.
 * On syntax error returns NULL and writes the full error sentence
 * ("Invalid pattern '<pattern>': <reason>.") into `err_buf` (if non-NULL and err_cap > 0).
 */
PinoReProg* pino_re_compile(const char* pat, size_t pat_len, char* err_buf, size_t err_cap);

/* Frees a compiled regex program and its lazy DFA caches. */
void pino_re_free(PinoReProg* prog);

/* Returns true if the pattern is anchored at the start of text (\A or non-multiline ^). */
bool pino_re_is_anchored_start(const PinoReProg* prog);

/* Returns true if `prog` matches anywhere in `text[0..text_len)`. */
bool pino_re_has_match(PinoReProg* prog, const char* text, size_t text_len);

/* Matches anchored at byte 0. On match returns true and sets *end_out to the match end byte. */
bool pino_re_match_prefix(PinoReProg* prog, const char* text, size_t text_len, size_t* end_out);

/* Finds the leftmost-first match anywhere in `text[0..text_len)`. */
bool pino_re_find(PinoReProg* prog, const char* text, size_t text_len, size_t* start_out, size_t* end_out);

/* Finds the leftmost-first match starting at or after `from_pos` (preserving full text context for anchors). */
bool pino_re_find_from(PinoReProg* prog, const char* text, size_t text_len, size_t from_pos,
                       size_t* start_out, size_t* end_out);

/* Advances `pos` by one UTF-8 rune (matching pino_utf8_decode). */
size_t pino_re_next_rune_pos(const char* text, size_t text_len, size_t pos);

#ifdef __cplusplus
}
#endif

#endif /* PINO_REGEX_H */
