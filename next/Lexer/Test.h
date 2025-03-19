#pragma once

#include <functional>
#include <string>
#include <vector>

class Test {
  int count_passed = 0;
  int count_failed = 0;
  bool with_test_name = false;

  bool each(std::vector<bool> cases);

  void run(const std::string &name, const std::function<bool()> &test);

  void test_identifier();

  void test_keyword();

  void test_literal();

  void test_marker();

  void test_operator();

  void test_constant();

  void test_function_call();

  void test_variable();

  void print_results();

public:
  void run_all(const bool &with_test_name);
};