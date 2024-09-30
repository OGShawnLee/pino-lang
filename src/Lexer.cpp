#include "Common.h"
#include "Lexer.h"

std::map<Lexer::Token::Type, std::string> Lexer::TYPE_NAME_MAPPING = {
  {Token::Type::ILLEGAL, "Illegal"},
  {Token::Type::IDENTIFIER, "Identifier"},
  {Token::Type::KEYWORD, "Keyword"},
  {Token::Type::LITERAL, "Literal"},
  {Token::Type::MARKER, "Marker"},
  {Token::Type::OPERATOR, "Operator"},
};

std::map<std::string, Lexer::Token::Keyword> Lexer::KEYWORD_MAPPING = {
  {"val", Token::Keyword::CONSTANT},
  {"var", Token::Keyword::VARIABLE},
  {"fn", Token::Keyword::FUNCTION},
  {"return", Token::Keyword::RETURN},
  {"import", Token::Keyword::IMPORT},
  {"from", Token::Keyword::FROM},
  {"as", Token::Keyword::AS},
  {"struct", Token::Keyword::STRUCT},
  {"static", Token::Keyword::STATIC},
  {"pub", Token::Keyword::PUB},
  {"enum", Token::Keyword::ENUM},
  {"if", Token::Keyword::IF},
  {"else", Token::Keyword::ELSE},
  {"in", Token::Keyword::IN},
  {"match", Token::Keyword::MATCH},
  {"case", Token::Keyword::CASE},
  {"for", Token::Keyword::LOOP},
  {"continue", Token::Keyword::CONTINUE},
  {"break", Token::Keyword::BREAK},
};

std::map<char, Lexer::Token::Marker> Lexer::MARKER_MAPPING = {
  {'{', Token::Marker::BLOCK_BEGIN},
  {'}', Token::Marker::BLOCK_END},
  {'[', Token::Marker::BRACKET_BEGIN},
  {']', Token::Marker::BRACKET_END},
  {',', Token::Marker::COMMA},
  {'#', Token::Marker::COMMENT},
  {'(', Token::Marker::PARENTHESIS_BEGIN},
  {')', Token::Marker::PARENTHESIS_END},
  {'"', Token::Marker::STR_QUOTE},
};

std::map<std::string, Lexer::Token::Operator> Lexer::OPERATOR_MAPPING = {
  {"=", Token::Operator::ASSIGNMENT},
  {"+", Token::Operator::ADDITION},
  {"+=", Token::Operator::ADDITION_ASSIGNMENT},
  {"-", Token::Operator::SUBTRACTION},
  {"-=", Token::Operator::SUBTRACTION_ASSIGNMENT},
  {"*", Token::Operator::MULTIPLICATION},
  {"*=", Token::Operator::MULTIPLICATION_ASSIGNMENT},
  {"/", Token::Operator::DIVISION},
  {"/=", Token::Operator::DIVISION_ASSIGNMENT},
  {"%", Token::Operator::MODULUS},
  {"%=", Token::Operator::MODULUS_ASSIGNMENT},
  {"<", Token::Operator::LESS_THAN},
  {"<=", Token::Operator::LESS_THAN_EQUAL},
  {">", Token::Operator::GREATER_THAN},
  {">=", Token::Operator::GREATER_THAN_EQUAL},
  {"!=", Token::Operator::NOT_EQUAL},
  {"==", Token::Operator::EQUAL},
  {"and", Token::Operator::AND},
  {"or", Token::Operator::OR},
  {"not", Token::Operator::NOT},
  {":", Token::Operator::MEMBER_ACCESS},
  {"::", Token::Operator::STATIC_MEMBER_ACCESS},
};

Lexer::Token::Token(const std::string &value, const Type &type) {
  this->value = value;
  this->type = type;
}

Lexer::Token::Token(const std::string &value, const Type &type, const std::vector<std::string> &injections) {
  this->value = value;
  this->type = type;
  this->injections = injections;
}

Lexer::Token::Type Lexer::Token::get_type() const {
  return this->type;
}

std::string Lexer::Token::get_value() const {
  return this->value;
}

Lexer::Token::Keyword Lexer::Token::get_keyword() const {
  return KEYWORD_MAPPING.at(this->value);
}

bool Lexer::Token::is_given_marker(Marker marker) const {
  return type == Type::MARKER && MARKER_MAPPING.at(this->value[0]) == marker;
}

bool Lexer::Token::is_given_operator(Operator operation) const {
  return type == Type::OPERATOR && OPERATOR_MAPPING.at(this->value) == operation;
}

bool Lexer::Token::is_given_type(Type type_a, Type type_b) const {
  return type == type_a || type == type_b;
}

Lexer::Token Lexer::consume_buffer(std::string &buffer) {
  Token::Type type;

  if (is_boolean_literal(buffer)) {
    type = Token::Type::LITERAL;
  } else if (is_float_literal(buffer)) {
    type = Token::Type::LITERAL;
  } else if (is_integer_literal(buffer)) {
    type = Token::Type::LITERAL;
  } else if (is_keyword(buffer)) {
    type = Token::Type::KEYWORD;
  } else if (is_operator(buffer)) {
    type = Token::Type::OPERATOR;
  } else if (is_identifier(buffer)) {
    type = Token::Type::IDENTIFIER;
  } else {
    type = Token::Type::ILLEGAL;
  }

  Token token(buffer, type);
  buffer = "";
  return token;
}

std::string Lexer::consume_str_injection(const std::string &line, int &index) {
  std::string injection = "";

  for (int i = index + 1; i < line.size(); i++) {
    const char &character = line[i];

    if (is_identifier(character)) {
      injection += character;
      continue;
    }

    index = i - 1;
    return injection;
  }

  throw std::runtime_error("Unterminated String Injection");
}

Lexer::Token Lexer::build_str_literal(const std::string &line, int &index) {
  std::string buffer = "";
  std::vector<std::string> injections;
  
  for (int i = index + 1; i < line.size(); i++) {
    const char &character = line[i];

    if (character == '"') {
      index = i;
      return Token(buffer, Token::Type::LITERAL, injections);
    }

    if (is_str_injection(line, i)) {
      std::string injection = consume_str_injection(line, i);
      injections.push_back(injection);
      buffer += "#" + injection;
      continue;
    }

    buffer += character;
  }

  throw std::runtime_error("Unterminated String Literal");
}

Lexer::Token Lexer::build_operator(const std::string &buffer, int &index) {
  std::string single_character_operator = std::string(1, buffer[index]);

  for (int i = index + 1; i < buffer.size(); i++) {
    const char &character = buffer[i];

    if (isspace(character)) continue;

    std::string character_str = std::string(1, character);

    if (is_operator(character_str) or character_str == "!") {
      std::string dual_character_operator = single_character_operator + character_str;

      index = i;
      return Token(
        dual_character_operator,
        is_operator(dual_character_operator) ? Token::Type::OPERATOR : Token::Type::ILLEGAL
      );
    } else {
      break;
    }
  }

  return Token(
    single_character_operator, 
    is_operator(single_character_operator) ? Token::Type::OPERATOR : Token::Type::ILLEGAL
  );
}

void Lexer::Token::print() const {
  println("Token {");
  println("  type: " + TYPE_NAME_MAPPING.at(this->type));
  println("  value: " + this->value);
  if (not this->injections.empty()) {
    println("  injections: [");
    for (const std::string &injection : this->injections) {
      println("    " + injection);
    }
    println("  ]");
  }
  println("}");
}

Lexer::Stream::Stream(const std::vector<Token> &collection) {
  this->index = 0;
  this->collection = std::move(collection);
}

const Lexer::Token& Lexer::Stream::current() {
  return this->collection[this->index];
}

const Lexer::Token& Lexer::Stream::consume() {
  return this->collection[this->index++];
}

void Lexer::Stream::next() {
  this->index++;
}

bool Lexer::Stream::has_next() const {
  return this->index < this->collection.size();
}

bool Lexer::Stream::is_next(const std::function<bool(const Token &)> &predicate) const {  
  return this->index + 1 < this->collection.size() and predicate(this->collection[this->index + 1]);
}

bool Lexer::is_boolean_literal(const std::string &buffer) {
  return buffer == "true" or buffer == "false";
}

bool Lexer::is_float_literal(const std::string &buffer) {
  bool has_decimal = false;

  for (int i = 0; i < buffer.size(); i++) {
    const char &character = buffer[i];

    if (isdigit(character)) continue;
    if (character == '.') {
      if (has_decimal) throw std::runtime_error("Invalid Float Literal");
      has_decimal = true;
    } 
  }

  return has_decimal;
}

bool Lexer::is_integer_literal(const std::string &buffer) {
  return std::all_of(buffer.begin(), buffer.end(), [](const char &character) {
    return isdigit(character);
  });
}
bool Lexer::is_identifier(const char &character) {
  return isalnum(character) or character == '_' or character == '$';
}

bool Lexer::is_identifier(const std::string &buffer) {
  return not isdigit(buffer[0]) and std::all_of(buffer.begin(), buffer.end(), [](const char &character) {
    return is_identifier(character);
  });
}

bool Lexer::is_keyword(const std::string &buffer) {
  return KEYWORD_MAPPING.find(buffer) != KEYWORD_MAPPING.end();
}

bool Lexer::is_marker(const char &character) {
  return MARKER_MAPPING.find(character) != MARKER_MAPPING.end();
}

bool Lexer::is_next_char(const std::string &line, int index, std::function<bool(const char &)> predicate) {
  if (index + 1 > line.size()) return false;
  return predicate(line[index + 1]);
}

bool Lexer::is_operator(const std::string &buffer) {
  return OPERATOR_MAPPING.find(buffer) != OPERATOR_MAPPING.end();
}

bool Lexer::is_str_injection(const std::string &line, size_t index) {
  return line[index] == '#' and is_next_char(line, index, [](const char &character) {
    return not isdigit(character) and isalpha(character);
  });
}

Lexer::Token::Marker Lexer::get_marker(const char &character) {
  return MARKER_MAPPING.at(character);
}

std::vector<Lexer::Token> Lexer::lex(const std::string &line) {
  std::vector<Token> collection;
  std::string final_line = line + " ";
  std::string buffer = "";

  for (int i = 0; i < final_line.size(); i++) {
    const char &character = final_line[i];

    if (is_whitespace(character)) {
      if (is_whitespace(buffer)) continue;
      collection.push_back(consume_buffer(buffer));
      continue;
    }

    std::string character_str = std::string(1, character);
    if (is_operator(character_str) or character_str == "!") {
      if (not is_whitespace(buffer)) {
        collection.push_back(consume_buffer(buffer));
      }

      collection.push_back(
        build_operator(final_line, i)
      );
      continue;
    } 
    
    if (is_marker(character)) {
      if (not is_whitespace(buffer)) {
        collection.push_back(consume_buffer(buffer));
      }

      Token::Marker marker = get_marker(character);

      switch (marker) {
        case Token::Marker::STR_QUOTE:
          collection.push_back(build_str_literal(final_line, i));
          break;
        case Token::Marker::COMMENT:
          return collection;
        default:
          collection.push_back(Token(std::string(1, character), Token::Type::MARKER));
      }

      continue;
    }

    buffer += character;
  }

  return collection;
}

Lexer::Stream Lexer::lex_file(const std::string &filename) {
  std::vector<Token> collection;

  each_line(filename, [&collection](const std::string &line) {
    std::vector<Token> tokens = lex(line);
    collection.insert(collection.end(), tokens.begin(), tokens.end());
  });

  return Stream(collection);
}
