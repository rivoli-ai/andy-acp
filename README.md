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
│       ├── Transport/              # Transport layer (stdio)
│       ├── JsonRpc/                # JSON-RPC 2.0 implementation
│       ├── Session/                # Session management
│       ├── Protocol/               # ACP protocol (initialize, shutdown)
│       └── Tools/                  # Tool framework (IAcpToolProvider, models)
├── tests/
│   └── Andy.Acp.Tests/             # Comprehensive unit tests (239 tests)
│       ├── Transport/
│       ├── JsonRpc/
│       ├── Session/
│       ├── Protocol/
│       └── Tools/
├── examples/
│   └── Andy.Acp.Examples/          # Working ACP server example
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
  - Content-Length header framing (following LSP/ACP specification)
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

See detailed instructions in `examples/Andy.Acp.Examples/README.md`.

Quick start:
1. Build: `dotnet build examples/Andy.Acp.Examples`
2. Copy `zed-settings.example.json` configuration to `~/.config/zed/settings.json`
3. Update paths in configuration
4. Open Zed and press `Cmd+?` (macOS) or `Ctrl+?` (Windows/Linux)

## Message Format

The transport layer implements the standard ACP message framing:

```
Content-Length: <length>\r\n
\r\n
<JSON message body>
```

Example ACP protocol flow:
```json
// 1. Client sends initialize
{"jsonrpc":"2.0","method":"initialize","id":1,"params":{
  "protocolVersion":"1.0",
  "clientInfo":{"name":"Zed","version":"0.1.0"},
  "capabilities":{"supportedTools":["echo","ping"]}
}}

// 2. Server responds with capabilities
{"jsonrpc":"2.0","id":1,"result":{
  "protocolVersion":"1.0",
  "serverInfo":{"name":"Andy.Acp.Examples","version":"1.0.0"},
  "capabilities":{
    "tools":{"supported":true,"available":["echo","calculator","get_time","reverse_string"]},
    "resources":{"supported":true},
    "logging":{"supported":true}
  },
  "sessionInfo":{"sessionId":"abc123","timeoutMs":1800000}
}}

// 3. Client confirms with initialized notification
{"jsonrpc":"2.0","method":"initialized","params":{}}

// 4. Client lists available tools
{"jsonrpc":"2.0","method":"tools/list","id":2,"params":{}}

// 5. Server returns tool definitions with JSON schemas
{"jsonrpc":"2.0","id":2,"result":{
  "tools":[
    {"name":"echo","description":"Echoes back the provided text","inputSchema":{...}},
    {"name":"calculator","description":"Performs basic arithmetic operations","inputSchema":{...}},
    {"name":"get_time","description":"Returns the current date and time","inputSchema":{...}},
    {"name":"reverse_string","description":"Reverses the provided string","inputSchema":{...}}
  ]
}}

// 6. Client calls calculator tool
{"jsonrpc":"2.0","method":"tools/call","id":3,"params":{
  "name":"calculator",
  "parameters":{"operation":"add","a":15,"b":27}
}}

// 7. Server executes tool and returns result
{"jsonrpc":"2.0","id":3,"result":{
  "result":{"operation":"add","a":15,"b":27,"result":42},
  "isError":false
}}

// 8. Client shuts down
{"jsonrpc":"2.0","method":"shutdown","id":4,"params":{"reason":"Done"}}

// 9. Server acknowledges
{"jsonrpc":"2.0","id":4,"result":{"success":true,"message":"Session terminated successfully"}}
```

## What the Example Does

The `examples/Andy.Acp.Examples` project demonstrates a working ACP server that can be used with Zed editor:

**Server Mode** (`--server`):
- Listens for JSON-RPC messages on stdin
- Sends responses to stdout (logs go to stderr)
- Implements complete ACP protocol: initialize → initialized → tools/list → tools/call → shutdown
- **Example tools** (via SimpleToolProvider):
  - **echo**: Echoes back text with timestamp
  - **calculator**: Performs arithmetic (add, subtract, multiply, divide)
  - **get_time**: Returns current time in UTC or local timezone
  - **reverse_string**: Reverses a string and returns length
- Session management with timeout tracking
- Graceful shutdown with pending request handling

**Client Mode** (`--client`):
- Sends a series of test messages demonstrating complete ACP protocol flow
- Tests all 4 example tools with various parameters
- Can be piped to server for testing: `--client | --server`

**Zed Integration**:
- Includes `zed-settings.example.json` with ready-to-use configuration
- Includes comprehensive `README.md` with setup instructions
- Includes `test-server.sh` for automated testing

See `examples/Andy.Acp.Examples/README.md` for detailed Zed integration instructions.

## Next Steps

### [READY FOR TESTING] Issue #7: Zed Integration Testing
**Status: Documentation Complete - Requires Zed Editor** | [Issue #7](https://github.com/rivoli-ai/andy-acp/issues/7)

**Documentation Completed**:
- Comprehensive testing guide created (docs/ZED_INTEGRATION_TESTING.md)
- Example README updated with all 4 tools (echo, calculator, get_time, reverse_string)
- Zed configuration example updated (zed-settings.example.json)
- Automated test script available (test-server.sh)

**Manual Testing Required** (requires Zed editor installation):
- Verify protocol flow with real Zed client
- Test tool execution through Zed UI
- Validate session lifecycle
- Performance and reliability testing
- Document any issues or improvements needed

See `docs/ZED_INTEGRATION_TESTING.md` for complete testing checklist.

## Integration with Zed

Once the full ACP implementation is complete, the server can be integrated with Zed editor by configuring it as an assistant in Zed's settings.

## Contributing

This project follows standard C# coding conventions and includes comprehensive tests for all features. When implementing new issues:

1. Write unit tests in the `tests/` directory
2. Create examples in the `examples/` directory
3. Ensure all tests pass before marking an issue as complete
4. Update this README with implementation status

## License

Apache License 2.0 - See LICENSE file for details
