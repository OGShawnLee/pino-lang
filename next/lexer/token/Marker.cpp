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

void Marker::print() const {
  println("Marker {");
  println("  type: " + this->get_name());
  println("  data: " + this->get_data());
  println("}");
}