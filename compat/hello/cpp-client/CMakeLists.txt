# compat/hello — C++ client. Mirrors go-client and dotnet-server's logic but
# from the C++ side, proving wire-format interop dotnet-server ⇄ vertex-cpp.
#
# Consumes vertex-cpp via sibling-clone add_subdirectory（与 dotnet-server 用
# ProjectReference 同思路）。CI / 本地都要求三个仓库平铺：
#   <workspace>/Vertex/        ← 你看到的本仓
#   <workspace>/vertex-cpp/    ← 必须 sibling
#   <workspace>/vertex-dotnet/ ← dotnet-server 用

cmake_minimum_required(VERSION 3.20)
project(vertex-hello-compat-cpp CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Sibling vertex-cpp。Compat 测试不需要 vertex-cpp 自己的 tests / samples。
set(VERTEX_BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(VERTEX_BUILD_SAMPLES OFF CACHE BOOL "" FORCE)
set(VERTEX_WITH_GRPC     ON  CACHE BOOL "" FORCE)

set(VERTEX_CPP_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../../vertex-cpp"
    CACHE PATH "Path to sibling vertex-cpp clone")
if(NOT EXISTS "${VERTEX_CPP_DIR}/CMakeLists.txt")
    message(FATAL_ERROR
        "vertex-cpp not found at ${VERTEX_CPP_DIR} — clone dengxuan/vertex-cpp "
        "next to Vertex/, or pass -DVERTEX_CPP_DIR=<path>")
endif()
add_subdirectory("${VERTEX_CPP_DIR}" "${CMAKE_CURRENT_BINARY_DIR}/_vertex-cpp")

# 生成 hello.pb.{h,cc} 自上一层的 hello.proto（go-client 走 go_package；
# dotnet-server 走 csharp_namespace；这里走 protoc cpp_out 默认 namespace
# vertex.compat.hello.v1）
find_package(Protobuf CONFIG REQUIRED)

set(HELLO_PROTO ${CMAKE_CURRENT_SOURCE_DIR}/../hello.proto)
set(HELLO_OUT   ${CMAKE_CURRENT_BINARY_DIR}/generated)
file(MAKE_DIRECTORY ${HELLO_OUT})

add_custom_command(
    OUTPUT ${HELLO_OUT}/hello.pb.cc ${HELLO_OUT}/hello.pb.h
    COMMAND $<TARGET_FILE:protobuf::protoc>
            --proto_path=${CMAKE_CURRENT_SOURCE_DIR}/..
            --cpp_out=${HELLO_OUT}
            ${HELLO_PROTO}
    DEPENDS ${HELLO_PROTO} protobuf::protoc
    COMMENT "Generating hello.proto C++ stubs for cpp-client"
    VERBATIM
)

add_executable(cpp-client main.cpp ${HELLO_OUT}/hello.pb.cc)
target_include_directories(cpp-client PRIVATE ${HELLO_OUT})
target_link_libraries(cpp-client PRIVATE vertex-cpp::vertex)

if(MSVC)
    target_compile_options(cpp-client PRIVATE /utf-8 /W4)
endif()
