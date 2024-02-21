#include "transpiler.h"

int main() {
  Transpiler::transpile("main.pino", "index.js");
  return 0;
}