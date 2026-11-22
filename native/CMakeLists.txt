# Builds aura_whisper: one DLL with whisper.cpp inside. The release workflow points WHISPER_DIR at a
# checkout of whisper.cpp v1.7.6 and builds it twice (with and without AVX2).
cmake_minimum_required(VERSION 3.15)
cmake_policy(SET CMP0091 NEW)
project(aura_whisper C CXX)
# no Visual C++ runtime needed on the PC: everything (whisper.cpp too) uses the static one
set(CMAKE_POLICY_DEFAULT_CMP0091 NEW)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")
set(WHISPER_DIR "${CMAKE_CURRENT_SOURCE_DIR}/whisper.cpp" CACHE PATH "whisper.cpp checkout")
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_SERVER OFF CACHE BOOL "" FORCE)
set(GGML_NATIVE OFF CACHE BOOL "" FORCE)
set(GGML_OPENMP OFF CACHE BOOL "" FORCE)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
add_subdirectory(${WHISPER_DIR} whisper EXCLUDE_FROM_ALL)
add_library(aura_whisper SHARED aura_whisper.c)
target_link_libraries(aura_whisper PRIVATE whisper)
