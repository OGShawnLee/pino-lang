#pragma once

#include "./Token.h"

class Marker : public Token {
  MARKER_TYPE marker_type;

  public:
    Marker(MARKER_TYPE marker_type, const char data);

    MARKER_TYPE get_marker_type() const;

    bool is_given_marker_type(MARKER_TYPE marker_type) const;
    
    static bool is_target_marker_type(const std::shared_ptr<Token> &token, MARKER_TYPE marker_type);

    static Marker* from_base(const std::shared_ptr<Token> &base);

    void print() const override;
};