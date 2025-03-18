#pragma once

#include "./Marker.h"
#include "../../Common.h"

Marker::Marker(MARKER_TYPE marker_type, const char data) : Token(
  TOKEN_TYPE::MARKER, 
  std::string(1, data), 
  Mapper::get_marker_name_from_enum(marker_type)
) {
  this->marker_type = marker_type;
}

MARKER_TYPE Marker::get_marker_type() const {
  return this->marker_type;
}

bool Marker::is_given_marker_type(MARKER_TYPE marker_type) const {
  return this->marker_type == marker_type;
}

bool Marker::is_target_marker_type(const std::shared_ptr<Token> &token, MARKER_TYPE marker_type) {
  return 
    token->is_given_type(TOKEN_TYPE::MARKER) and
    Marker::from_base(token)->is_given_marker_type(marker_type);
}

Marker* Marker::from_base(const std::shared_ptr<Token> &base) {
  return dynamic_cast<Marker*>(base.get());
}

void Marker::print() const {
  println("Marker {");
  println("  type: " + this->get_name());
  println("  data: " + this->get_data());
  println("}");
}