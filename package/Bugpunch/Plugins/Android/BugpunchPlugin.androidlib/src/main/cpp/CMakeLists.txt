cmake_minimum_required(VERSION 3.10)
project(bugpunch_crash C)

add_library(bugpunch_crash SHARED bugpunch_crash.c)
target_link_libraries(bugpunch_crash log)

# Strip in release for smaller .so
set(CMAKE_C_FLAGS_RELEASE "${CMAKE_C_FLAGS_RELEASE} -Os -DNDEBUG")
