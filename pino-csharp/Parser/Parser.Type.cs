using System;
using System.Collections.Generic;

namespace Pino;

public partial class Parser {
  private static string ConsumeTyping(TokenStream stream) {
    if (stream.Current.Type == TokenType.Identifier && stream.Current.Data == "map") {
      stream.Consume(); // consume 'map'
      if (!stream.Consume().IsMarker(MarkerType.BracketBegin)) {
        throw Error(stream, $"Expected '[' after 'map' in type signature, got {stream.Current}");
      }
      string keyType = ConsumeTyping(stream);
      if (!stream.Consume().IsMarker(MarkerType.Comma)) {
        throw Error(stream, $"Expected ',' between map types in type signature, got {stream.Current}");
      }
      string valType = ConsumeTyping(stream);
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw Error(stream, $"Expected ']' after map types in type signature, got {stream.Current}");
      }
      return $"map[{keyType}, {valType}]";
    }

    if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
      stream.Consume(); // consume '['
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw Error(stream, $"Expected ']' for array type, got {stream.Current}");
      }
      string elemType = ConsumeTyping(stream);
      return "[]" + elemType;
    }

    if (stream.Current.IsKeyword(KeywordType.Function)) {
      stream.Consume(); // consume 'fn'
      if (!stream.Consume().IsMarker(MarkerType.ParenthesisBegin)) {
        throw Error(stream, $"Expected '(' for function type, got {stream.Current}");
      }

      var paramTypes = new List<string>();
      while (stream.HasNext && !stream.Current.IsMarker(MarkerType.ParenthesisEnd)) {
        paramTypes.Add(ConsumeTyping(stream));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }

      if (!stream.Consume().IsMarker(MarkerType.ParenthesisEnd)) {
        throw Error(stream, $"Expected ')' for function type, got {stream.Current}");
      }

      string returnType = " any";
      // Optional return type: can start with identifier, bracket '[', or keyword 'fn'
      if (stream.Current.Type == TokenType.Identifier ||
          stream.Current.IsMarker(MarkerType.BracketBegin) ||
          stream.Current.IsKeyword(KeywordType.Function)) {
        returnType = " " + ConsumeTyping(stream);
      }

      return $"fn({string.Join(", ", paramTypes)}){returnType}";
    }

    var t = stream.Consume();
    if (t.Type != TokenType.Identifier) {
      throw Error(stream, $"Expected Typing, got {t}");
    }
    string typeName = t.Data;
    if (stream.Current.IsMarker(MarkerType.BracketBegin)) {
      stream.Consume(); // consume '['
      var subTypes = new List<string>();
      while (!stream.Current.IsMarker(MarkerType.BracketEnd)) {
        subTypes.Add(ConsumeTyping(stream));
        if (stream.Current.IsMarker(MarkerType.Comma)) {
          stream.Consume();
        }
      }
      if (!stream.Consume().IsMarker(MarkerType.BracketEnd)) {
        throw Error(stream, "Expected ']' to close generic type arguments");
      }
      typeName += "[" + string.Join(", ", subTypes) + "]";
    }
    return typeName;
  }
}
