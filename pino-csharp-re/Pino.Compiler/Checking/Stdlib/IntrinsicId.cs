namespace Pino.Compiler.Checking;

/// <summary>
/// Exhaustive identifier for every standard-library global built-in and built-in type
/// property or method, distinguishing arity and overload variants explicitly.
/// </summary>
public enum IntrinsicId : byte {
  // Global built-ins (spec/stdlib/builtins.md)
  Println,
  ReadlineNoPrompt,
  ReadlineWithPrompt,
  IntFromNumber,
  IntFromString,
  FloatFromNumber,
  FloatFromString,
  RuneFromInt,
  RuneFromString,
  Str,
  Type,
  RandFloat,
  RandInt,
  Time,
  Sleep,
  Clear,
  Panic,
  RegexCompile,
  ReadFile,
  WriteFile,
  FileExists,

  // string properties & methods (STRLIB-1..19)
  StrLen,
  StrLower,
  StrUpper,
  StrTrim,
  StrTrimStart,
  StrTrimEnd,
  StrClone,
  StrToCString,
  StrContains,
  StrStartsWith,
  StrEndsWith,
  StrIndexOf,
  StrLastIndexOf,
  StrSplit,
  StrReplace,
  StrSubstring,

  // []T properties & methods (VECLIB-1..25)
  VecLen,
  VecCap,
  VecPush,
  VecPop,
  VecEach1,
  VecEach2,
  VecMap1,
  VecMap2,
  VecFilter1,
  VecFilter2,
  VecFind1,
  VecFind2,
  VecFindIndex1,
  VecFindIndex2,
  VecAny1,
  VecAny2,
  VecAll1,
  VecAll2,
  VecCount1,
  VecCount2,
  VecJoin,

  // map[K, V] properties & methods (MAPLIB-1..7)
  MapLen,
  MapKeys,
  MapValues,
  MapRemove,

  // set[T] properties & methods (SETLIB-1..8, D-175)
  SetLen,
  SetAdd,
  SetRemove,
  SetAny,
  SetAll,
  SetEach,

  // regex properties & methods (RGX-1..7)
  RegexPattern,
  RegexHasMatch,
  RegexMatchPrefix,
  RegexFind,
  RegexFindAll,
  RegexReplace,
}
