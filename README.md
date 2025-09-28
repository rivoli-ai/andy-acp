# andy-acp
Andy's implementation of ACP (Agent Client Protocol)

## Overview

This repository contains a C# implementation of the Agent Client Protocol (ACP), designed to enable AI agents to integrate with tools and editors like Zed. The implementation follows the ACP specification and provides a foundation for building ACP-compliant servers.

## Project Structure

```
andy-acp/
├── src/
│   ├── Andy.Acp.Core/          # Core library with transport layer
│   │   └── Transport/
│   │       ├── ITransport.cs
│   │       └── StdioTransport.cs
│   └── Andy.Acp.Server/         # Server application
│       └── Program.cs
├── tests/
│   └── Andy.Acp.Tests/          # Unit tests
│       └── Transport/
│           └── StdioTransportTests.cs
├── examples/
│   └── Andy.Acp.Examples/       # Usage examples and demonstrations
│       └── Program.cs
├── demo.sh                      # Interactive demonstration script
├── test-*.sh                    # Signal handling and functionality tests
└── Andy.Acp.sln                 # Solution file
```

## Features Implemented

### Issue #2: Stdio Transport Layer Foundation ✅

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
- **Comprehensive Unit Tests**: 17 tests covering all major scenarios including cancellation
- **Working Examples**: Client/server demonstration programs with proper ACP protocol communication
- **Test Scripts**: Automated signal handling and functionality verification

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

# Run complete client-server demo (recommended)
./demo.sh

# Run as server (receives ACP messages from stdin)
dotnet run --project examples/Andy.Acp.Examples -- --server

# Run as client (sends ACP messages to stdout)
dotnet run --project examples/Andy.Acp.Examples -- --client

# Pipe client output to server (demonstrates ACP protocol)
dotnet run --project examples/Andy.Acp.Examples -- --client | dotnet run --project examples/Andy.Acp.Examples -- --server
```

### Test Signal Handling

```bash
# Test Ctrl+C handling
./test-ctrl-c.sh

# Test detailed signal handling (SIGINT and SIGTERM)
./test-detailed-signals.sh

# Test interactive mode
./test-interactive.sh
```

## Message Format

The transport layer implements the standard ACP message framing:

```
Content-Length: <length>\r\n
\r\n
<JSON message body>
```

Example ACP messages sent by the client:
```json
{"method":"initialize","id":1,"params":{"clientInfo":{"name":"Andy.Acp.Examples","version":"1.0.0"}}}
{"method":"ping","id":2,"params":{}}
{"method":"echo","id":3,"params":{"text":"Hello, ACP World!"}}
{"method":"test","id":4,"params":{"data":[1,2,3,4,5]}}
```

Example server responses:
```json
{"id":"example-123","result":{"echo":"...","timestamp":"2025-09-28T15:17:07.290Z"},"jsonrpc":"2.0"}
```

## Next Steps

The following transferred issues from andy-cli are ready for implementation:

1. **Issue #5**: JSON-RPC 2.0 message parsing and handling
2. **Issue #6**: Session management and state handling
3. **Issue #7**: Initialization handshake protocol
4. **Issue #8**: Tool discovery and capability negotiation
5. **Issue #9**: Expose andy-cli tools via ACP protocol
6. **Issue #10**: Resource management (files, URIs)
7. **Issue #11**: Zed editor integration and end-to-end testing

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
