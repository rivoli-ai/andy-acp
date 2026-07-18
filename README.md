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
│       ├── Client/                 # Agent-to-client requests (fs, terminal, permission)
│       ├── Server/                 # Unified ACP server
│       └── Tools/                  # Tool framework (MCP compatibility)
├── tests/
│   └── Andy.Acp.Tests/             # Comprehensive unit tests (276 tests)
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
- **Message Types**: Request, Response, Notification, Error
- **Error Handling**: Standard JSON-RPC error codes and custom exceptions
- **Method Registration**: Dynamic method registration with async handlers
- **Compliance**: `result: null` on success, explicit-null-id vs. notification,
  `error.data`, and safe internal-error messages. **Batch requests are not
  supported** and a top-level array is rejected. Parameters are validated as
  structured JSON values (not full JSON Schema validation).

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

- **AcpProtocolHandler**: Handles the ACP v1 `initialize` handshake (only)
- **Protocol Models**: ACP `ClientCapabilities`/`AgentCapabilities`, `Implementation`
- **Capability Negotiation**: Integer `protocolVersion` negotiation; client fs/terminal
  capabilities recorded for agent-to-client requests
- **Initialize-before-session ordering**: session methods fail until `initialize` completes
- **Note**: `initialized` and `shutdown` were removed (not part of ACP v1)

### MCP-compatibility Tool Framework (not ACP tool calls)
**Status: Available for MCP-style hosts** | [Issue #6](https://github.com/rivoli-ai/andy-acp/issues/6)

> **Note:** This `tools/list` + `tools/call` framework is an **MCP-style** convenience,
> distinct from ACP. In ACP, tool activity is reported to the client as `tool_call` and
> `tool_call_update` **session/update** notifications (see the streaming section), not via
> `tools/list`/`tools/call`. Use this framework only when hosting MCP-style tool calls.

- **IAcpToolProvider**: Interface for tool registration and execution
- **AcpToolsHandler**: Handles `tools/list` and `tools/call` (MCP-style)
- **Tool Models**: AcpToolDefinition, AcpInputSchema, AcpToolResult (minimal)
- **SimpleToolProvider**: Example implementation with 4 demonstration tools
- **Error Handling**: Graceful error responses for missing tools and execution failures
- **Example Tools**: echo, calculator, get_time, reverse_string

**Note**: This is the generic framework. Concrete andy-cli tools will use andy-tools library with an adapter pattern (see docs/TOOLS_COMPARISON.md).

### [COMPLETE] Agent Provider Interface & Session Management
**Status: Complete** | Integrated with Andy.CLI

- **IAgentProvider**: Core interface for implementing ACP-compatible agents
- **IResponseStreamer**: Interface for streaming responses to clients
- **Session Methods**: session/new, session/load, session/prompt, session/cancel, session/set_mode
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

**Total: 276 tests passing** (including schema-backed ACP v1 validation and end-to-end stdio flows)

## Supported ACP version and capabilities

- **Protocol version:** ACP v1 (the `protocolVersion` integer `1`). On `initialize`, a
  higher requested version is negotiated down to `1`.
- **Agent capabilities advertised:** `loadSession`, and `promptCapabilities`
  (`image`/`audio`/`embeddedContext`) reflecting the agent provider. Text and
  `resource_link` content are always accepted.
- **Client capabilities consumed:** `fs.readTextFile`, `fs.writeTextFile`, and `terminal`.
  The agent issues `fs/*`, `terminal/*`, and `session/request_permission` **to the client**
  and only when the client advertised the matching capability.
- **Lifecycle methods:** `initialize`, `session/new`, `session/load`, `session/prompt`,
  `session/set_mode` (by `modeId`), and the `session/cancel` notification. `initialized`,
  `shutdown`, and `session/set_model` are **not** part of ACP v1 and are not implemented.

See **Known limitations** below for optional ACP v1 features not yet implemented.

## Building and Testing

### Prerequisites
- .NET 8.0 SDK (all projects target `net8.0`)

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
2. Add a custom agent to `~/.config/zed/settings.json` using the current
   `agent_servers` syntax:
```json
{
  "agent_servers": {
    "Andy Echo": {
      "command": "/path/to/andy-acp/examples/SimpleEchoAgent/bin/Release/net8.0/SimpleEchoAgent",
      "args": ["--acp"],
      "env": {}
    }
  }
}
```
3. Restart Zed, open the Agent Panel, and pick **Andy Echo** as the agent.
4. Type a message — you should see it echoed back.

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
// 1. Client sends initialize (protocolVersion is an integer)
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{
  "protocolVersion":1,
  "clientCapabilities":{"fs":{"readTextFile":true,"writeTextFile":true},"terminal":true}
}}

// 2. Agent responds with the negotiated version and its capabilities
{"jsonrpc":"2.0","id":1,"result":{
  "protocolVersion":1,
  "agentInfo":{"name":"Andy ACP Server","version":"1.0.0"},
  "agentCapabilities":{
    "loadSession":true,
    "promptCapabilities":{"image":false,"audio":false,"embeddedContext":false},
    "mcpCapabilities":{"http":false,"sse":false}
  },
  "authMethods":[]
}}

// 3. Client creates a new session (cwd and mcpServers are required)
{"jsonrpc":"2.0","id":2,"method":"session/new","params":{
  "cwd":"/absolute/path/to/workspace",
  "mcpServers":[]
}}

// 4. Agent returns the session id (and optional mode state)
{"jsonrpc":"2.0","id":2,"result":{
  "sessionId":"session-abc123"
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

### Andy.CLI Integration

Full LLM-powered agent working in Zed:
- **session/prompt** implementation with streaming
- Andy tools surfaced as ACP `tool_call`/`tool_call_update` updates
- LLM reasoning (OpenAI, Anthropic, Gemini, Cerebras, Ollama)

The integration is in the [andy-cli repository](https://github.com/rivoli-ai/andy-cli) via `AndyAgentProvider.cs`.

> This library is **ALPHA** (see the warning at the top). It is not production-ready and
> has not undergone a security review.

## Status & Next Steps

### [COMPLETE] Zed Integration
**Status: Working** | Successfully tested with Andy.CLI

✓ **Protocol Implementation**:
- Line-delimited JSON transport (Zed-compatible)
- session/new, session/load, session/prompt methods
- session/update streaming notifications
- Consistent camelCase ACP wire serialization

✓ **Working Examples**:
- SimpleEchoAgent: Minimal working example (echoes messages)
- Andy.CLI: Full LLM + tools integration

✓ **Tested Features**:
- Session creation and management
- Response streaming (word-by-word display)
- Multiple conversation turns
- Graceful error handling

## Known limitations

Implemented and covered by tests:
- `initialize` handshake with integer protocol-version negotiation
- `session/new`, `session/load` (with history replay), `session/prompt`,
  `session/set_mode` (by `modeId`), `session/cancel`
- All `session/update` variants (message/thought chunks, `tool_call`,
  `tool_call_update`, `plan`)
- Multimodal prompt content blocks (text, image, audio, resource, resource_link),
  validated against negotiated capabilities
- Agent → client `fs/read_text_file`, `fs/write_text_file`, `terminal/*`, and
  `session/request_permission`
- Schema-backed validation of wire output against the pinned ACP v1 schema

Not yet implemented (optional ACP v1 surface):
- `authenticate` / `logout` and auth methods
- `session/set_config_option` and the config-option system (models, reasoning levels)
- `session/list`, `session/delete`, `session/resume`, `session/close`
- MCP server transports beyond passing configuration through to the agent
- `available_commands_update`, `current_mode_update`, `usage_update`,
  `session_info_update`, `config_option_update` session updates
- Diff/terminal `ToolCallContent` variants and tool-call `locations`

JSON-RPC batch requests are intentionally **not** supported and are rejected.

## Contributing

This project follows standard C# coding conventions and includes comprehensive tests for all features. When implementing new issues:

1. Write unit tests in the `tests/` directory
2. Create examples in the `examples/` directory
3. Ensure all tests pass before marking an issue as complete
4. Update this README with implementation status

## License

Apache License 2.0 - See LICENSE file for details
