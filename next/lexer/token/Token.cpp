#pragma once

#include <regex>
#include "./Mapper.cpp"
#include "./Token.h"
#include "../../Common.h"

Token::Token(TOKEN_TYPE token_type, std::string data) {
  this->token_type = token_type;
  this->data = data;
  this->name = Mapper::get_token_name_from_enum(token_type);
}

Token::Token(TOKEN_TYPE token_type, std::string data, std::string name) {
  this->token_type = token_type;
  this->data = data;
  this->name = name;
}

TOKEN_TYPE Token::get_type() const {
  return token_type;
}

std::string Token::get_data() const {
  return data;
}

std::string Token::get_name() const {
  return name;
}

void Token::print() const {
  println("Token {");
  println("  type: " + get_name());
  println("  data: " + get_data());
  println("}");
}

bool Token::equals(const Token &candidate) const {
  return this->token_type == candidate.get_type() and this->data == candidate.get_data();
}