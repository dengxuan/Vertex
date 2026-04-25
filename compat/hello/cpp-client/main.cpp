// compat/hello — C++ client side. Mirrors go-client：连 dotnet-server，
// publish 一个 HelloEvent，退出 0。证明 wire-format C++ → .NET 跨语言互通。
//
// 退出码：
//   0  publish 成功
//   2  连不上服务端（GrpcTransport 构造抛 / firstConnect 失败）
//   3  publish 抛异常

#include <chrono>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <string>
#include <thread>

#include "vertex/messaging/MessagingChannel.h"
#include "vertex/serialization/ProtobufSerializer.h"
#include "vertex/transport/grpc/GrpcTransport.h"

#include "hello.pb.h"

using namespace std::chrono_literals;
using vertex::messaging::MessagingChannel;
using vertex::messaging::MessagingChannelOptions;
using vertex::messaging::MessageTypeRegistry;
using vertex::serialization::ProtobufSerializer;
using vertex::transport::grpc::GrpcTransport;
using vertex::transport::grpc::GrpcTransportOptions;

namespace {

std::string EnvOr(const char* name, const char* def) {
    const char* v = std::getenv(name);
    return v && *v ? std::string{v} : std::string{def};
}

// Topic 必须跟 dotnet-server / go-client 完全一致 —— 这是 wire-format §2.1
// 推荐做法：proto descriptor full name（"vertex.compat.hello.v1.HelloEvent"）。
constexpr const char* kHelloTopic = "vertex.compat.hello.v1.HelloEvent";

}  // namespace

int main() {
    const std::string port      = EnvOr("HELLO_PORT", "50051");
    const std::string greeting  = EnvOr("HELLO_GREETING", "hello from cpp");
    const std::string serverAddr = "127.0.0.1:" + port;

    std::cerr << "client: dialing " << serverAddr << std::endl;

    GrpcTransportOptions tOpts;
    tOpts.serverAddress     = serverAddr;
    tOpts.reconnect.enabled = false;  // compat 跑一次性，不重连

    std::unique_ptr<GrpcTransport> transport;
    try {
        transport = std::make_unique<GrpcTransport>("cpp-hello", tOpts);
    } catch (const std::exception& ex) {
        std::cerr << "client: FAIL — transport ctor: " << ex.what() << std::endl;
        return 2;
    }

    auto registry = std::make_shared<MessageTypeRegistry>();
    registry->RegisterEvent<vertex::compat::hello::v1::HelloEvent>(kHelloTopic);

    MessagingChannelOptions chOpts;
    chOpts.name = "hello";
    MessagingChannel channel(*transport, ProtobufSerializer::Instance(), registry, chOpts);

    vertex::compat::hello::v1::HelloEvent ev;
    ev.set_greeting(greeting);
    try {
        channel.Publish(ev);
    } catch (const std::exception& ex) {
        std::cerr << "client: FAIL — publish: " << ex.what() << std::endl;
        return 3;
    }

    std::cout << "client: published HelloEvent{greeting=\"" << greeting << "\"}"
              << std::endl;

    // Grace period before tear-down：vertex-cpp 的 GrpcTransport 现在 Close() 走
    // TryCancel + Finish（非优雅 CloseSend），如果立即析构 transport，刚 publish 的
    // 帧可能还没真正写到 wire 上就被取消。等一会让 server 端 Read() 拿到。
    // TODO（vertex-cpp 后续）: 借 vertex-go 的做法，Close() 先 CloseSend 等 Finish。
    std::this_thread::sleep_for(500ms);
    return 0;
}
