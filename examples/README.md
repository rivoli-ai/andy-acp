# Andy ACP Examples

This directory contains examples demonstrating the ACP (Agent Client Protocol) implementation.

## Available Examples

### 1. SimpleEchoAgent (Recommended for Getting Started)

**Location**: `examples/SimpleEchoAgent/`

A minimal working example (~150 lines) that demonstrates:
- Basic `IAgentProvider` implementation
- Session management
- Response streaming via `IResponseStreamer`
- ACP server setup with `AcpServer`

Perfect for:
- Learning the ACP protocol
- Testing Zed integration
- Understanding core concepts
- Building your own agent

See `SimpleEchoAgent/README.md` for complete documentation.

**Quick Start**:
```bash
# Build
dotnet build examples/SimpleEchoAgent -c Release

# Run
dotnet run --project examples/SimpleEchoAgent -- --acp
```

### 2. Andy.Acp.Examples (Full Protocol Demonstration)

**Location**: `examples/Andy.Acp.Examples/`

A comprehensive example demonstrating:
- Complete ACP protocol flow (initialize → tools/list → tools/call → shutdown)
- Tool framework with JSON Schema validation
- Example tools: echo, calculator, get_time, reverse_string
- Both server and client modes
- Session management

Perfect for:
- Understanding the full protocol
- Learning tool implementation
- Testing MCP compatibility
- Advanced integrations

See `Andy.Acp.Examples/README.md` for complete documentation.

**Quick Start**:
```bash
# Run automated test
cd examples/Andy.Acp.Examples && ./test-server.sh

# Run in server mode
dotnet run --project examples/Andy.Acp.Examples -- --server

# Run client → server pipeline
dotnet run --project examples/Andy.Acp.Examples -- --client | \
    dotnet run --project examples/Andy.Acp.Examples -- --server
```

## Integration with Zed Editor

Both examples can be used as Zed agents. Configuration example:

```json
{
  "agent": {
    "provider": {
      "name": "custom",
      "command": "/path/to/example/binary",
      "args": ["--acp"]  // or ["--server"] for Andy.Acp.Examples
    }
  }
}
```

**Recommended Path**:
1. Start with **SimpleEchoAgent** to verify Zed integration works
2. Move to **Andy.Acp.Examples** to explore tool capabilities
3. Build your own agent using the patterns learned

## ACP Protocol Support

### SimpleEchoAgent Supports
- `initialize` / `initialized`
- `session/new`
- `session/load`
- `session/prompt` with streaming via `session/update` notifications

### Andy.Acp.Examples Supports
- `initialize` / `initialized` / `shutdown`
- `tools/list`
- `tools/call`
- Session management with timeouts
- JSON Schema tool validation

## Message Formats

Both examples support **line-delimited JSON** (Zed-compatible):
```
{"jsonrpc":"2.0","method":"initialize","id":1,"params":{...}}
{"jsonrpc":"2.0","id":1,"result":{...}}
```

Andy.Acp.Examples also supports **Content-Length headers** (MCP/LSP compatible):
```
Content-Length: 123\r\n
\r\n
{"jsonrpc":"2.0",...}
```

## Building All Examples

```bash
# From repository root
dotnet build examples/SimpleEchoAgent
dotnet build examples/Andy.Acp.Examples
```

## Testing

All examples write diagnostic logs to **stderr** to avoid interfering with the ACP protocol on stdout.

To see detailed logs, check stderr output or redirect it to a file:
```bash
dotnet run --project examples/SimpleEchoAgent -- --acp 2> /tmp/acp-debug.log
```

## Next Steps

After exploring these examples, check out the **Andy.CLI integration** which provides a production-ready LLM-powered agent with full tool support.

See the [andy-cli repository](https://github.com/rivoli-ai/andy-cli) for details.
