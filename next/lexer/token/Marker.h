#pragma once

#include "./Token.h"

class Marker : public Token {
  MARKER_TYPE marker_type;

  public:
    Marker(MARKER_TYPE marker_type, const char data);

    MARKER_TYPE get_marker_type() const;

    void print() const override;
};