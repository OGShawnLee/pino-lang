# Specify the minimum CMake version
cmake_minimum_required(VERSION 3.10)

# Set the project name
project(pino_lang)

# Set the C++ standard
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED True)

# Add the source files
set(SOURCES
    next/main.cpp
    next/Lexer/Token/Keyword.cpp
    next/Lexer/Token/Keyword.h
    next/Lexer/Token/Literal.cpp
    next/Lexer/Token/Literal.h
    next/Lexer/Token/Operator.cpp
    next/Lexer/Token/Operator.h
    next/Lexer/Token/Mapper.cpp
    next/Lexer/Token/Mapper.h
    next/Lexer/Token/Marker.cpp
    next/Lexer/Token/Marker.h
    next/Lexer/Token/Matcher.cpp
    next/Lexer/Token/Matcher.h
    next/Lexer/Token/Operator.cpp
    next/Lexer/Token/Operator.h
    next/Lexer/Token/Stream.cpp
    next/Lexer/Token/Stream.h
    next/Lexer/Token/Token.cpp
    next/Lexer/Token/Token.h
    next/Lexer/Lexer.cpp
    next/Lexer/Lexer.h
    next/Lexer/Test.cpp
    next/Lexer/Test.h
    next/Parser/Statement/Expression/Expression.cpp
    next/Parser/Statement/Expression/Expression.h
    next/Parser/Statement/Expression/FunctionCall.cpp
    next/Parser/Statement/Expression/FunctionCall.h 
    next/Parser/Statement/Expression/Identifier.cpp
    next/Parser/Statement/Expression/Identifier.h
    next/Parser/Statement/Expression/Value.cpp
    next/Parser/Statement/Expression/Value.h
    next/Parser/Statement/ParserMapper.cpp
    next/Parser/Statement/ParserMapper.h
    next/Parser/Statement/Declaration.cpp
    next/Parser/Statement/Declaration.h
    next/Parser/Statement/Function.cpp
    next/Parser/Statement/Function.h
    next/Parser/Statement/Statement.cpp
    next/Parser/Statement/Statement.h
    next/Parser/Statement/Variable.cpp
    next/Parser/Statement/Variable.h
    next/Parser/Parser.cpp
    next/Parser/Parser.h
    next/Transpiler/Transpiler.cpp
    next/Transpiler/Transpiler.h
    next/Common.cpp
    next/Common.h
)

include_directories(${CMAKE_SOURCE_DIR}/next)
include_directories(${CMAKE_SOURCE_DIR}/next/Lexer)
include_directories(${CMAKE_SOURCE_DIR}/next/Parser)
include_directories(${CMAKE_SOURCE_DIR}/next/Transpiler)

# Create the executable
add_executable(pino ${SOURCES})