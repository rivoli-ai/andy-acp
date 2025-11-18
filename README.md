# andy-acp
Andy's implementation of ACP (Agent Client Protocol)

> **ALPHA RELEASE WARNING**
>
> This software is in ALPHA stage. **NO GUARANTEES** are made about its functionality, stability, or safety.
>
> **CRITICAL WARNINGS:**
> - This library implements a protocol server that accepts and executes commands
> - Security features are **NOT FULLY TESTED** and may have vulnerabilities
> - **DO NOT USE** in production environments
> - **DO NOT USE** on systems exposed to untrusted networks or clients
> - **DO NOT USE** without proper security review and hardening
> - The authors assume **NO RESPONSIBILITY** for security breaches, data loss, or system damage
>
> **USE AT YOUR OWN RISK**

## Overview

This repository contains a C# implementation of the Agent Client Protocol (ACP), designed to enable AI agents to integrate with tools and editors like Zed. The implementation follows the ACP specification and provides a foundation for building ACP-compliant servers.

## Project Structure

```
andy-acp/
├── src/
│   └── Andy.Acp.Core/              # Core ACP library
│       ├── Transport/              # Transport layer (stdio, line-delimited JSON)
│       ├── JsonRpc/                # JSON-RPC 2.0 implementation
│       ├── Session/                # Session management
│       ├── Protocol/               # ACP protocol handlers
│       ├── Agent/                  # Agent provider interfaces
│       ├── FileSystem/             # File system provider interface
│       ├── Terminal/               # Terminal provider interface
│       ├── Server/                 # Unified ACP server
│       └── Tools/                  # Tool framework (legacy/MCP compatibility)
├── tests/
│   └── Andy.Acp.Tests/             # Comprehensive unit tests (239 tests)
│       ├── Transport/
│       ├── JsonRpc/
│       ├── Session/
│       ├── Protocol/
│       └── Tools/
├── examples/
│   ├── SimpleEchoAgent/            # Minimal working example
│   │   ├── SimpleEchoAgentProvider.cs
│   │   ├── Program.cs
│   │   ├── SimpleEchoAgent.csproj
│   │   └── README.md
│   └── Andy.Acp.Examples/          # Full-featured ACP server example
│       ├── Program.cs              # Server/client implementation
│       ├── README.md               # Zed integration guide
│       ├── zed-settings.example.json
│       └── test-server.sh
├── docs/
│   ├── TOOLS_COMPARISON.md         # Andy.Tools vs ACP interfaces analysis
│   └── ZED_INTEGRATION_TESTING.md  # Comprehensive Zed testing guide
└── Andy.Acp.sln
```

## Features Implemented

### [COMPLETE] Issue #2: Stdio Transport Layer Foundation
**Status: Complete** | 17 tests | [Issue #2](https://github.com/rivoli-ai/andy-acp/issues/2)

- **ITransport Interface**: Defines the contract for transport implementations
- **StdioTransport Class**: Implements stdio-based communication with:
  - **Dual format support**: Auto-detects Content-Length headers OR line-delimited JSON
  - **Line-delimited JSON**: Newline-separated messages (Zed/Gemini style)
  - **Content-Length framing**: Traditional LSP/MCP style (backwards compatible)
  - Async read/write operations with cancellation token support
  - Proper stream handling and disposal
  - Comprehensive error handling and EOF detection
  - Thread-safe write operations using semaphores
  - Enhanced signal handling (SIGINT/SIGTERM) for graceful shutdown
- **Structured Logging**: Microsoft.Extensions.Logging with stderr output
- **Signal Handling**: POSIX signals for Unix systems, Console.CancelKeyPress for interactive mode

### [COMPLETE] Issue #3: JSON-RPC 2.0 Message Handling
**Status: Complete** | 86 tests | [Issue #3](https://github.com/rivoli-ai/andy-acp/issues/3)

- **JsonRpcSerializer**: Serialization/deserialization of JSON-RPC 2.0 messages
- **JsonRpcHandler**: Method registration and request dispatching
- **Message Types**: Request, Response, Notification, Error, Batch
- **Error Handling**: Standard JSON-RPC error codes and custom exceptions
- **Method Registration**: Dynamic method registration with async handlers
- **Batch Support**: Process multiple requests in a single message
- **Validation**: Strict JSON-RPC 2.0 specification compliance

### [COMPLETE] Issue #4: Session Management and State Handling
**Status: Complete** | 73 tests | [Issue #4](https://github.com/rivoli-ai/andy-acp/issues/4)

- **AcpSession**: Session lifecycle management (Created → Initializing → Initialized → Active → ShuttingDown → Terminated)
- **ISessionManager**: Session creation, tracking, and cleanup
- **SessionManager**: Default implementation with timeout support
- **Pending Request Tracking**: Track in-flight requests for graceful shutdown
- **Session Health Monitoring**: Automatic timeout detection
- **Client Capabilities**: Store and manage client-provided capabilities
- **Background Cleanup**: Automatic session cleanup for terminated sessions

### [COMPLETE] Issue #5: Initialization Handshake Protocol
**Status: Complete** | 23 tests | [Issue #5](https://github.com/rivoli-ai/andy-acp/issues/5)

- **AcpProtocolHandler**: Handles initialize, initialized, shutdown methods
- **Protocol Models**: InitializeParams, InitializeResult, ServerInfo, ServerCapabilities
- **Capability Negotiation**: Tools, Prompts, Resources, Logging capabilities
- **Protocol Version Validation**: Ensures client/server compatibility
- **Graceful Shutdown**: Waits up to 5 seconds for pending requests
- **State Management**: Validates requests based on session state
- **Session Information**: Returns session ID, timeout, and metadata

### [COMPLETE] Issue #6: Tool Framework
**Status: Complete** | 34 tests | [Issue #6](https://github.com/rivoli-ai/andy-acp/issues/6)

- **IAcpToolProvider**: Interface for tool registration and execution
- **AcpToolsHandler**: Handles tools/list and tools/call protocol methods
- **Tool Models**: AcpToolDefinition, AcpInputSchema, AcpToolResult (minimal, ACP-aligned)
- **SimpleToolProvider**: Example implementation with 4 demonstration tools
- **JSON Schema Support**: Parameter validation using JSON Schema (draft-07)
- **Error Handling**: Graceful error responses for missing tools and execution failures
- **Tool Registration**: Dynamic tool registration with async execution handlers
- **Example Tools**: echo, calculator, get_time, reverse_string

**Note**: This is the generic framework. Concrete andy-cli tools will use andy-tools library with an adapter pattern (see docs/TOOLS_COMPARISON.md).

### [COMPLETE] Agent Provider Interface & Session Management
**Status: Complete** | Integrated with Andy.CLI

- **IAgentProvider**: Core interface for implementing ACP-compatible agents
- **IResponseStreamer**: Interface for streaming responses to clients
- **Session Methods**: session/new, session/load, session/prompt, session/cancel, session/set_mode, session/set_model
- **SessionUpdateStreamer**: Sends session/update notifications for streaming responses
- **AcpSessionHandler**: Handles all session/* protocol methods
- **AcpServer**: Unified server that composes all handlers and providers
- **Working Integration**: Andy.CLI successfully integrates via AndyAgentProvider

**Agent Protocol Flow**:
1. Client sends `initialize` with protocol version
2. Server responds with capabilities (agent, filesystem, terminal)
3. Client sends `session/new` to create conversation session
4. Server returns session metadata
5. Client sends `session/prompt` with user message
6. Server streams response via `session/update` notifications
7. Server returns stopReason when complete

### [COMPLETE] Zed Editor Integration
**Status: Working** | Tested with Andy.CLI

- **Line-delimited JSON** transport working with Zed
- **SimpleEchoAgent** example provides minimal working implementation
- **Andy.CLI** integration providing full LLM + tools via ACP
- **Response streaming** working (word-by-word display in Zed)
- **Session management** properly tracking conversation state

**Total: 239 tests passing**

## Building and Testing

### Prerequisites
- .NET 9.0 SDK or later

### Build
```bash
dotnet build
```

### Run Tests
```bash
dotnet test
```

### Run Examples

#### SimpleEchoAgent (Minimal Example)

Perfect for learning and testing:

```bash
# Build and run
dotnet build examples/SimpleEchoAgent -c Release
dotnet run --project examples/SimpleEchoAgent -- --acp

# Or use the published binary
dotnet run --project examples/SimpleEchoAgent -- --help
```

See `examples/SimpleEchoAgent/README.md` for Zed configuration.

#### Andy.Acp.Examples (Full-Featured)

Demonstrates complete protocol with tools:

```bash
# Show usage help
dotnet run --project examples/Andy.Acp.Examples

# Run automated test (recommended)
cd examples/Andy.Acp.Examples && ./test-server.sh

# Run as server (receives ACP messages from stdin)
dotnet run --project examples/Andy.Acp.Examples -- --server

# Run as client (sends ACP messages to stdout)
dotnet run --project examples/Andy.Acp.Examples -- --client

# Pipe client output to server (demonstrates full ACP protocol)
dotnet run --project examples/Andy.Acp.Examples -- --client | \
    dotnet run --project examples/Andy.Acp.Examples -- --server
```

### Test with Zed Editor

**Recommended**: Start with SimpleEchoAgent for a working baseline.

Quick start:
1. Build: `dotnet build examples/SimpleEchoAgent -c Release`
2. Add to `~/.config/zed/settings.json`:
```json
{
  "agent": {
    "provider": {
      "name": "custom",
      "command": "/path/to/andy-acp/examples/SimpleEchoAgent/bin/Release/net8.0/SimpleEchoAgent",
      "args": ["--acp"]
    }
  }
}
```
3. Restart Zed and open Assistant panel
4. Type a message - you should see it echoed back!

See `examples/SimpleEchoAgent/README.md` and `examples/Andy.Acp.Examples/README.md` for detailed instructions.

## Message Format

The transport layer supports two message formats:

### Line-Delimited JSON (Default for Zed)
```
{"jsonrpc":"2.0","method":"initialize","id":1,"params":{...}}
{"jsonrpc":"2.0","id":1,"result":{...}}
```

Each message is a single line terminated by `\n`. This is the format used by Zed and Gemini CLI.

### Content-Length Headers (LSP/MCP Compatible)
```
Content-Length: <length>\r\n
\r\n
<JSON message body>
```

The transport auto-detects which format is being used.

## Protocol Flow

### Agent Session Flow (Primary)
```json
// 1. Client sends initialize
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{
  "protocolVersion":1,
  "clientCapabilities":{"promptFormats":{"text":true}}
}}

// 2. Server responds with capabilities
{"jsonrpc":"2.0","id":1,"result":{
  "protocolVersion":1,
  "serverInfo":{"name":"Andy.CLI","version":"1.0.0"},
  "capabilities":{
    "loadSession":true,
    "audioPrompts":false,
    "imagePrompts":false,
    "embeddedContext":true
  }
}}

// 3. Client creates a new session
{"jsonrpc":"2.0","id":2,"method":"session/new","params":{}}

// 4. Server returns session metadata
{"jsonrpc":"2.0","id":2,"result":{
  "sessionId":"session-abc123",
  "createdAt":"2025-11-18T05:00:00Z",
  "mode":"assistant",
  "model":"andy-cli"
}}

// 5. Client sends a prompt
{"jsonrpc":"2.0","id":3,"method":"session/prompt","params":{
  "sessionId":"session-abc123",
  "prompt":[{"type":"text","text":"Hello! What is 15 + 27?"}]
}}

// 6. Server streams response via session/update notifications
{"jsonrpc":"2.0","method":"session/update","params":{
  "sessionId":"session-abc123",
  "update":{
    "content":{"type":"text","text":"Hello! "},
    "sessionUpdate":"agent_message_chunk"
  }
}}
{"jsonrpc":"2.0","method":"session/update","params":{
  "sessionId":"session-abc123",
  "update":{
    "content":{"type":"text","text":"15 + 27 = 42"},
    "sessionUpdate":"agent_message_chunk"
  }
}}

// 7. Server returns final stopReason
{"jsonrpc":"2.0","id":3,"result":{"stopReason":"end_turn"}}
```

### Tool-Based Flow (Legacy/MCP Compatible)
The library also supports the traditional MCP-style tools/list and tools/call methods. See `examples/Andy.Acp.Examples` for a demonstration.

## Examples

### SimpleEchoAgent (Recommended for Learning)

A minimal 150-line example demonstrating core concepts:
- **IAgentProvider** implementation with session management
- **Response streaming** via IResponseStreamer
- **ACP server setup** with minimal configuration
- **Perfect for learning** and testing Zed integration

See `examples/SimpleEchoAgent/README.md` for setup instructions.

### Andy.Acp.Examples (Full-Featured)

Demonstrates the complete ACP protocol with tools support:
- Traditional **tools/list** and **tools/call** methods (MCP-compatible)
- Example tools: echo, calculator, get_time, reverse_string
- Client/server test modes
- Comprehensive protocol flow demonstration

See `examples/Andy.Acp.Examples/README.md` for details.

### Andy.CLI Integration (Production)

Full LLM-powered agent working in Zed:
- Complete **session/prompt** implementation with streaming
- **All Andy tools** available via tool execution
- **Real LLM reasoning** (OpenAI, Anthropic, Gemini, Cerebras, Ollama)
- Production-ready agent experience

The integration is in the [andy-cli repository](https://github.com/rivoli-ai/andy-cli) via `AndyAgentProvider.cs`.

## Status & Next Steps

### [COMPLETE] Zed Integration
**Status: Working** | Successfully tested with Andy.CLI

✓ **Protocol Implementation**:
- Line-delimited JSON transport (Zed-compatible)
- session/new, session/load, session/prompt methods
- session/update streaming notifications
- Property name mapping (camelCase ↔ PascalCase)

✓ **Working Examples**:
- SimpleEchoAgent: Minimal working example (echoes messages)
- Andy.CLI: Full LLM + tools integration

✓ **Tested Features**:
- Session creation and management
- Response streaming (word-by-word display)
- Multiple conversation turns
- Graceful error handling

### Future Enhancements

Potential additions for future releases:
- **File System Provider**: Implement `fs/read_text_file` and `fs/write_text_file`
- **Terminal Provider**: Implement `terminal/create` and related methods
- **Permission System**: Add `session/request_permission` for sensitive operations
- **Audio/Image Support**: Enable multimodal prompts
- **Performance Optimizations**: Caching, batching, connection pooling

## Contributing

This project follows standard C# coding conventions and includes comprehensive tests for all features. When implementing new issues:

1. Write unit tests in the `tests/` directory
2. Create examples in the `examples/` directory
3. Ensure all tests pass before marking an issue as complete
4. Update this README with implementation status

## License

Apache License 2.0 - See LICENSE file for details
