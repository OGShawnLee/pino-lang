#pragma once

#include "Statement.h"
#include "global.h"

class ELSEStatement : public Statement {
  public:
    ELSEStatement();

    static PeekPtr<ELSEStatement> build(std::vector<Token> stream, size_t index);
};

class IFStatement : public Statement {
  public:
    std::string condition;
    std::unique_ptr<ELSEStatement> else_statement;

    IFStatement();

    static PeekPtr<IFStatement> build(std::vector<Token> stream, size_t index);
};
