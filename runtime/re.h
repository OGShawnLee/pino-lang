#ifndef _TINY_REGEX_C
#define _TINY_REGEX_C

#ifdef __cplusplus
extern "C"{
#endif

enum slre_option {SLRE_CASE_INSENSITIVE = 1};

struct cap {
  const char *ptr;
  int len;
};

struct slre {
  unsigned char code[512];
  unsigned char data[512];
  int code_size;
  int data_size;
  int num_caps;   // Number of bracket pairs
  int anchored;   // Must match from string start
  enum slre_option options;
  const char *error_string;   // Error string
};

const char *slre_compile(struct slre *r, const char *re);
const char *slre_match(const struct slre *r, const char *buf, int len,
                           struct cap *caps, int caps_size);

#ifdef __cplusplus
}
#endif

#endif
