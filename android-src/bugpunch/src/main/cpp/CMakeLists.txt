cmake_minimum_required(VERSION 3.10)
project(bp C)

# Short target name keeps CMake's .cxx/.../CMakeFiles/<tgt>.dir/ path under
# CMAKE_OBJECT_PATH_MAX (250 chars) on deeply-nested Jenkins workspaces.
# OUTPUT_NAME preserves the on-disk libbugpunch_crash.so that Java expects
# via System.loadLibrary("bugpunch_crash").
add_library(bp SHARED bp.c bp_video.c)
set_target_properties(bp PROPERTIES OUTPUT_NAME bugpunch_crash)
target_link_libraries(bp log)

set(CMAKE_C_FLAGS_RELEASE "${CMAKE_C_FLAGS_RELEASE} -Os -DNDEBUG")
