using System;
using System.Collections.Generic;
using System.IO;

namespace PinoBenchmarks;

public static class Lexer {
  private enum AtomType {
    Keyword,
    Identifier,
    LiteralInt,
    LiteralBool,
    Marker,
    EOF,
  }

  private readonly struct Atom {
    public readonly AtomType Type;
    public readonly string? Text;
    public readonly long IntVal;
    public readonly bool BoolVal;

    public Atom(AtomType type, string? text = null, long intVal = 0, bool boolVal = false) {
      Type = type;
      Text = text;
      IntVal = intVal;
      BoolVal = boolVal;
    }
  }

  private readonly struct Particle {
    public readonly int Line;
    public readonly int Column;
    public readonly int Len;
    public readonly Atom Atom;

    public Particle(int line, int column, int len, Atom atom) {
      Line = line;
      Column = column;
      Len = len;
      Atom = atom;
    }
  }

  private static readonly HashSet<string> Keywords = new() {
    "val", "var", "fn", "return", "if", "then", "else", "match", "when",
    "yield", "for", "in", "break", "continue", "struct", "enum", "union",
    "interface", "pub", "is", "static", "module", "from", "import",
  };

  private static readonly Dictionary<char, string> MarkerMap = new() {
    ['('] = "LPAREN", [')'] = "RPAREN",
    ['['] = "LBRACKET", [']'] = "RBRACKET",
    ['{'] = "LBRACE", ['}'] = "RBRACE",
    [':'] = "COLON", [','] = "COMMA",
  };

  private static bool IsIdentChar(char c) {
    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
  }

  private static bool IsNumChar(char c) {
    return c >= '0' && c <= '9';
  }

  private static List<Particle> LexProgram(string program) {
    var atomList = new List<Particle>(program.Length / 6);
    int lineNum = 1;
    int column = 1;
    int identifierLen = 0;
    int numericalLen = 0;
    int totalLen = program.Length;

    void PushIdentifier(int i, int length) {
      int begin = i - length;
      string sub = program.Substring(begin, length);
      Atom atom;
      if (sub.Length <= 10 && Keywords.Contains(sub)) {
        atom = new Atom(AtomType.Keyword, text: sub);
      } else if (sub == "true" || sub == "false") {
        atom = new Atom(AtomType.LiteralBool, boolVal: sub == "true");
      } else {
        atom = new Atom(AtomType.Identifier, text: sub);
      }
      atomList.Add(new Particle(lineNum, column - length, length, atom));
    }

    void PushNumerical(int i, int length) {
      int begin = i - length;
      string sub = program.Substring(begin, length);
      long.TryParse(sub, out long n);
      atomList.Add(new Particle(lineNum, column - length, length, new Atom(AtomType.LiteralInt, intVal: n)));
    }

    for (int i = 0; i < totalLen; i++) {
      char c = program[i];

      if (IsIdentChar(c)) {
        identifierLen++;
        column++;
        continue;
      } else if (identifierLen > 0) {
        PushIdentifier(i, identifierLen);
        identifierLen = 0;
      }

      if (IsNumChar(c)) {
        numericalLen++;
        column++;
        continue;
      } else if (numericalLen > 0) {
        PushNumerical(i, numericalLen);
        numericalLen = 0;
      }

      if (c == '\n') {
        lineNum++;
        column = 1;
        continue;
      }

      if (c == ' ' || c == '\t') {
        column++;
        continue;
      }

      if (MarkerMap.TryGetValue(c, out string? markerName)) {
        atomList.Add(new Particle(lineNum, column, 1, new Atom(AtomType.Marker, text: markerName)));
        column++;
        continue;
      }

      column++;
    }

    if (identifierLen > 0) {
      PushIdentifier(totalLen, identifierLen);
    } else if (numericalLen > 0) {
      PushNumerical(totalLen, numericalLen);
    }

    atomList.Add(new Particle(lineNum, column, 1, new Atom(AtomType.EOF)));
    return atomList;
  }

  public static void Run(bool quick) {
    string content = File.ReadAllText("benchmarks/data/lexer.txt");
    List<Particle> particles = LexProgram(content);

    long kwCount = 0;
    long identCount = 0;
    long intSum = 0;
    long boolTrue = 0;
    long markerCount = 0;

    foreach (Particle p in particles) {
      switch (p.Atom.Type) {
        case AtomType.Keyword:
          kwCount++;
          break;
        case AtomType.Identifier:
          identCount++;
          break;
        case AtomType.LiteralInt:
          intSum = (intSum + p.Atom.IntVal) % 1000000007;
          break;
        case AtomType.LiteralBool:
          if (p.Atom.BoolVal) {
            boolTrue++;
          }
          break;
        case AtomType.Marker:
          markerCount++;
          break;
      }
    }

    Console.WriteLine(particles.Count);
    Console.WriteLine(kwCount);
    Console.WriteLine(identCount);
    Console.WriteLine(intSum);
    Console.WriteLine(boolTrue);
    Console.WriteLine(markerCount);
  }
}
