#include "Keyword.h"
#include "Common.h"
#include "Mapper.h"

Keyword::Keyword(KEYWORD_TYPE keyword) : Token(
  TOKEN_TYPE::KEYWORD, 
  Mapper::get_keyword_str_from_enum(keyword), 
  Mapper::get_keyword_name_from_enum(keyword)
) {
  this->keyword = keyword;
}

KEYWORD_TYPE Keyword::get_keyword() const {
  return keyword;
}

Keyword* Keyword::from_base(const std::shared_ptr<Token> &base) {
  return dynamic_cast<Keyword*>(base.get());
}

bool Keyword::is_given_keyword(const std::shared_ptr<Token> &token, KEYWORD_TYPE keyword) {
  return token->is_given_type(TOKEN_TYPE::KEYWORD) and Keyword::from_base(token)->get_keyword() == keyword;
}

void Keyword::print() const {
  println("Keyword {");
  println("  type: " + get_name());
  println("  data: " + get_data());
  println("}");
}
