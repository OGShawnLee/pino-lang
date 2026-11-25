using System;

namespace PinoBenchmarks;

public static class BinaryTrees {
  private sealed class Tree {
    public readonly Tree? Left;
    public readonly Tree? Right;

    public Tree(Tree? left, Tree? right) {
      Left = left;
      Right = right;
    }
  }

  private static Tree MakeTree(int depth) {
    if (depth <= 0) {
      return new Tree(null, null);
    }
    return new Tree(MakeTree(depth - 1), MakeTree(depth - 1));
  }

  private static long CheckTree(Tree t) {
    if (t.Left is null) {
      return 1;
    }
    return 1 + CheckTree(t.Left) + CheckTree(t.Right!);
  }

  private static int Pow2(int exp) {
    int r = 1;
    for (int i = 0; i < exp; i++) {
      r *= 2;
    }
    return r;
  }

  public static void Run(bool quick) {
    int minDepth = 4;
    int maxDepth = quick ? 9 : 12;
    int stretchDepth = maxDepth + 1;

    long stretchCheck = CheckTree(MakeTree(stretchDepth));
    Tree longLived = MakeTree(maxDepth);

    long totalCheck = stretchCheck;
    for (int d = minDepth; d <= maxDepth; d += 2) {
      int iterations = Pow2(maxDepth - d + minDepth);
      long check = 0;
      for (int i = 0; i < iterations; i++) {
        check += CheckTree(MakeTree(d));
      }
      totalCheck += check;
    }

    long longCheck = CheckTree(longLived);
    totalCheck += longCheck;

    Console.WriteLine(stretchCheck);
    Console.WriteLine(totalCheck);
    Console.WriteLine(longCheck);
  }
}
