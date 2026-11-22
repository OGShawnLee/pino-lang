/*
 * pino_alloc.c - default allocator and the -DPINO_DEBUG_RC debug mode
 * (design 056 sections 4.3 and 4.5).
 *
 * Debug mode layout of every allocation (64-byte prefix keeps the payload
 * 16-byte aligned):
 *
 *   [ PinoDebugNode ........ padding ][ payload ... ]
 *     ^ block returned by a->alloc      ^ pointer handed to the caller
 *
 * Freed blocks are poisoned with 0xDD, get magic 0xDEADFACE, and wait in a
 * small quarantine ring before the memory is really released, so a use after
 * free or a double free is reported instead of silently reading reused memory.
 */
#include "pino_rt.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <process.h>
#else
#include <unistd.h>
#endif

PinoFrame* pino_frame_top = NULL;

uint64_t pino_stat_alloc_count = 0;
uint64_t pino_stat_dealloc_count = 0;
uint64_t pino_stat_alloc_bytes = 0;

static void pino_out_of_memory(size_t size) {
  fflush(stdout);
  fprintf(stderr, "Out of memory: the program could not allocate %llu bytes.\n", (unsigned long long)size);
  exit(101);
}

static void* sys_alloc(void* ctx, size_t size) {
  (void)ctx;
  void* p = malloc(size == 0 ? 1 : size);
  if (p == NULL) {
    pino_out_of_memory(size);
  }
  return p;
}

static void* sys_realloc(void* ctx, void* ptr, size_t old_size, size_t new_size) {
  (void)ctx;
  (void)old_size;
  void* p = realloc(ptr, new_size == 0 ? 1 : new_size);
  if (p == NULL) {
    pino_out_of_memory(new_size);
  }
  return p;
}

static void sys_dealloc(void* ctx, void* ptr, size_t size) {
  (void)ctx;
  (void)size;
  free(ptr);
}

PinoAlloc pino_sys_alloc = {sys_alloc, sys_realloc, sys_dealloc, NULL};
PinoAlloc* pino_cur_alloc = &pino_sys_alloc;

#ifndef PINO_DEBUG_RC
/* ------------------------------------------------------------------------- */
/* Release mode: thin wrappers                                                */
/* ------------------------------------------------------------------------- */

void* pino_alloc_at(PinoAlloc* a, size_t size, const char* type_name, const char* file, int line) {
  (void)type_name;
  (void)file;
  (void)line;
  pino_stat_alloc_count++;
  pino_stat_alloc_bytes += size;
  return a->alloc(a->ctx, size);
}

void* pino_realloc_at(PinoAlloc* a, void* p, size_t old_size, size_t new_size, const char* type_name,
                      const char* file, int line) {
  (void)type_name;
  (void)file;
  (void)line;
  if (new_size > old_size) {
    pino_stat_alloc_bytes += new_size - old_size;
  }
  return a->realloc(a->ctx, p, old_size, new_size);
}

void pino_dealloc(PinoAlloc* a, void* p, size_t size) {
  if (p == NULL) {
    return;
  }
  pino_stat_dealloc_count++;
  a->dealloc(a->ctx, p, size);
}

void* pino_rc_alloc_at(PinoAlloc* a, size_t size, uint16_t type_id, uint16_t flags, const char* type_name,
                       const char* file, int line) {
  PinoRcHeader* h = (PinoRcHeader*)pino_alloc_at(a, size, type_name, file, line);
  h->rc = 1;
  h->flags = flags;
  h->type_id = type_id;
  return h;
}

void pino_rc_mark_cycle_candidate(PinoRcHeader* h) { h->flags |= PINO_FLAG_CYCLE_CANDIDATE; }

#else
/* ------------------------------------------------------------------------- */
/* -DPINO_DEBUG_RC: live list, magic checks, poisoning, leak report           */
/* ------------------------------------------------------------------------- */

#define PINO_DEBUG_MAGIC 0x50494E4Fu
#define PINO_DEBUG_FREED_MAGIC 0xDEADFACEu
#define PINO_DEBUG_PREFIX 64
#define PINO_DEBUG_QUARANTINE 256
#define PINO_DEBUG_ALLOC_FILL 0xCD
#define PINO_DEBUG_POISON 0xDD

typedef struct PinoDebugNode {
  uint32_t magic;
  uint32_t is_rc_object;
  struct PinoDebugNode* prev;
  struct PinoDebugNode* next;
  const char* type_name;
  const char* file;
  PinoAlloc* alloc;
  size_t size;
  int32_t line;
  bool cycle_candidate;
} PinoDebugNode;

typedef char pino_assert_debug_node_fits[(sizeof(PinoDebugNode) <= PINO_DEBUG_PREFIX) ? 1 : -1];

static PinoDebugNode* g_live_head = NULL;
static int64_t g_live_count = 0;
static bool g_report_registered = false;
static bool g_aborting = false;
static PinoDebugNode* g_quarantine[PINO_DEBUG_QUARANTINE];
static int g_quarantine_next = 0;

static PinoDebugNode* node_of(const void* payload) {
  return (PinoDebugNode*)((char*)payload - PINO_DEBUG_PREFIX);
}

static void* payload_of(PinoDebugNode* n) { return (char*)n + PINO_DEBUG_PREFIX; }

static void debug_fatal(const char* what, const void* payload, const char* op) {
  g_aborting = true;
  fflush(stdout);
  fprintf(stderr, "Memory error in %s: %s (object at %p).\n", op, what, payload);
  if (pino_frame_top != NULL) {
    fprintf(stderr, "  while running '%s'.\n", pino_frame_top->name);
  }
  fflush(stderr);
  exit(103);
}

void pino_debug_check_rc(const PinoRcHeader* h, const char* op) {
  /* Immortal singletons and arena objects are not tracked here. */
  if (h->rc == PINO_RC_IMMORTAL || h->rc == PINO_RC_ARENA) {
    return;
  }
  const PinoDebugNode* n = node_of(h);
  if (n->magic == PINO_DEBUG_FREED_MAGIC) {
    debug_fatal("this object was already freed (use after free or double release)", h, op);
  }
  if (n->magic != PINO_DEBUG_MAGIC) {
    debug_fatal("this is not a live runtime object (bad pointer or corrupted header)", h, op);
  }
}

static void report_registered_once(void);

void* pino_alloc_at(PinoAlloc* a, size_t size, const char* type_name, const char* file, int line) {
  report_registered_once();
  pino_stat_alloc_count++;
  pino_stat_alloc_bytes += size;
  PinoDebugNode* n = (PinoDebugNode*)a->alloc(a->ctx, PINO_DEBUG_PREFIX + size);
  memset(n, 0, PINO_DEBUG_PREFIX);
  n->magic = PINO_DEBUG_MAGIC;
  n->type_name = type_name != NULL ? type_name : "memory block";
  n->file = file != NULL ? file : "?";
  n->line = line;
  n->size = size;
  n->alloc = a;
  n->prev = NULL;
  n->next = g_live_head;
  if (g_live_head != NULL) {
    g_live_head->prev = n;
  }
  g_live_head = n;
  g_live_count++;
  void* p = payload_of(n);
  memset(p, PINO_DEBUG_ALLOC_FILL, size);
  return p;
}

static void unlink_node(PinoDebugNode* n) {
  if (n->prev != NULL) {
    n->prev->next = n->next;
  } else {
    g_live_head = n->next;
  }
  if (n->next != NULL) {
    n->next->prev = n->prev;
  }
  n->prev = NULL;
  n->next = NULL;
  g_live_count--;
}

void pino_dealloc(PinoAlloc* a, void* p, size_t size) {
  (void)a;
  if (p == NULL) {
    return;
  }
  PinoDebugNode* n = node_of(p);
  if (n->magic == PINO_DEBUG_FREED_MAGIC) {
    debug_fatal("this memory was already freed (double free)", p, "pino_dealloc");
  }
  if (n->magic != PINO_DEBUG_MAGIC) {
    debug_fatal("this memory was not allocated by the runtime allocator", p, "pino_dealloc");
  }
  if (n->size != size) {
    debug_fatal("the size given when freeing does not match the size allocated", p, "pino_dealloc");
  }
  pino_stat_dealloc_count++;
  unlink_node(n);
  n->magic = PINO_DEBUG_FREED_MAGIC;
  memset(p, PINO_DEBUG_POISON, n->size);
  /* Quarantine: really release the oldest block once the ring is full. */
  PinoDebugNode* old = g_quarantine[g_quarantine_next];
  if (old != NULL) {
    old->alloc->dealloc(old->alloc->ctx, old, PINO_DEBUG_PREFIX + old->size);
  }
  g_quarantine[g_quarantine_next] = n;
  g_quarantine_next = (g_quarantine_next + 1) % PINO_DEBUG_QUARANTINE;
}

void* pino_realloc_at(PinoAlloc* a, void* p, size_t old_size, size_t new_size, const char* type_name,
                      const char* file, int line) {
  if (p == NULL) {
    return pino_alloc_at(a, new_size, type_name, file, line);
  }
  PinoDebugNode* n = node_of(p);
  void* q = pino_alloc_at(a, new_size, n->type_name, n->file, n->line);
  PinoDebugNode* qn = node_of(q);
  qn->is_rc_object = n->is_rc_object;
  qn->cycle_candidate = n->cycle_candidate;
  (void)type_name;
  (void)file;
  (void)line;
  memcpy(q, p, old_size < new_size ? old_size : new_size);
  pino_dealloc(a, p, old_size);
  return q;
}

void* pino_rc_alloc_at(PinoAlloc* a, size_t size, uint16_t type_id, uint16_t flags, const char* type_name,
                       const char* file, int line) {
  PinoRcHeader* h = (PinoRcHeader*)pino_alloc_at(a, size, type_name, file, line);
  h->rc = 1;
  h->flags = flags;
  h->type_id = type_id;
  PinoDebugNode* n = node_of(h);
  n->is_rc_object = 1;
  n->cycle_candidate = (flags & PINO_FLAG_CYCLE_CANDIDATE) != 0;
  return h;
}

void pino_rc_mark_cycle_candidate(PinoRcHeader* h) {
  h->flags |= PINO_FLAG_CYCLE_CANDIDATE;
  if (h->rc != PINO_RC_IMMORTAL && h->rc != PINO_RC_ARENA) {
    node_of(h)->cycle_candidate = true;
  }
}

int64_t pino_debug_live_count(void) { return g_live_count; }

int64_t pino_debug_leak_count(int64_t* cycle_leaks_out) {
  int64_t leaks = 0;
  int64_t cycles = 0;
  for (PinoDebugNode* n = g_live_head; n != NULL; n = n->next) {
    if (n->cycle_candidate) {
      cycles++;
    } else {
      leaks++;
    }
  }
  if (cycle_leaks_out != NULL) {
    *cycle_leaks_out = cycles;
  }
  return leaks;
}

void pino_debug_leak_report(void) {
  if (g_aborting) {
    return;
  }
  int64_t cycles = 0;
  int64_t leaks = pino_debug_leak_count(&cycles);
  if (leaks == 0 && cycles == 0) {
    return;
  }
  fflush(stdout);
  for (PinoDebugNode* n = g_live_head; n != NULL; n = n->next) {
    const char* tag = n->cycle_candidate ? "CYCLE-LEAK" : "LEAK";
    if (n->is_rc_object) {
      const PinoRcHeader* h = (const PinoRcHeader*)payload_of(n);
      fprintf(stderr, "%s: %s rc=%d allocated at %s:%d (%llu bytes)\n", tag, n->type_name, (int)h->rc, n->file,
              (int)n->line, (unsigned long long)n->size);
    } else {
      fprintf(stderr, "%s: %s allocated at %s:%d (%llu bytes)\n", tag, n->type_name, n->file, (int)n->line,
              (unsigned long long)n->size);
    }
  }
  fprintf(stderr, "%lld memory leak(s) and %lld closure-cycle leak(s) were found at exit.\n", (long long)leaks,
          (long long)cycles);
  fflush(stderr);
  const char* fail = getenv("PINO_FAIL_ON_LEAK");
  if (leaks > 0 && fail != NULL && strcmp(fail, "1") == 0) {
    fflush(stdout);
    _exit(102);
  }
}

static void report_registered_once(void) {
  if (!g_report_registered) {
    g_report_registered = true;
    atexit(pino_debug_leak_report);
  }
}

#endif /* PINO_DEBUG_RC */
