# Andy ACP Examples

This example demonstrates a working ACP (Agent Client Protocol) server implementation that can be tested with Zed editor and other ACP clients.

## What This Example Demonstrates

- Complete ACP initialization handshake (initialize → initialized → shutdown)
- JSON-RPC 2.0 message handling over stdio transport
- Session management with timeout tracking
- Server capabilities advertisement
- Example tool methods (ping, echo, test)
- Graceful shutdown with pending request handling

## Running the Example

### Server Mode (for Zed integration)

Start the ACP server to accept connections from Zed:

```bash
cd examples/Andy.Acp.Examples
dotnet run -- --server
```

The server will:
- Listen for JSON-RPC messages on stdin
- Send responses to stdout
- Log diagnostic information to stderr
- Support complete ACP protocol lifecycle

### Client Mode (for testing)

Test the server by sending sample ACP messages:

```bash
# Terminal 1: Start server
dotnet run -- --server

# Terminal 2: Send test messages
dotnet run -- --client | dotnet run -- --server
```

Or use the simpler pipe command:

```bash
dotnet run -- --client | dotnet run -- --server
```

## Testing with Zed Editor

### Prerequisites

1. **Install Zed**: Download from [zed.dev](https://zed.dev/)
2. **Build the example**:
   ```bash
   cd examples/Andy.Acp.Examples
   dotnet build
   ```

### Zed Configuration

Zed uses a configuration file to connect to ACP servers. The configuration is located at:

**macOS/Linux**: `~/.config/zed/settings.json`
**Windows**: `%APPDATA%\Zed\settings.json`

Add the following to your Zed settings:

```json
{
  "assistant": {
    "version": "2",
    "provider": {
      "name": "acp",
      "servers": [
        {
          "name": "andy-acp-example",
          "command": "dotnet",
          "args": [
            "run",
            "--project",
            "/absolute/path/to/andy-acp/examples/Andy.Acp.Examples",
            "--",
            "--server"
          ],
          "env": {}
        }
      ]
    }
  }
}
```

**Important**: Replace `/absolute/path/to/andy-acp` with the actual path to your andy-acp repository.

### Alternative: Using Published Binary

If you publish the example as a standalone binary:

```bash
cd examples/Andy.Acp.Examples
dotnet publish -c Release -r osx-arm64 --self-contained -o ./bin/publish
```

Then update Zed configuration:

```json
{
  "assistant": {
    "version": "2",
    "provider": {
      "name": "acp",
      "servers": [
        {
          "name": "andy-acp-example",
          "command": "/absolute/path/to/andy-acp/examples/Andy.Acp.Examples/bin/publish/Andy.Acp.Examples",
          "args": ["--server"],
          "env": {}
        }
      ]
    }
  }
}
```

### Using with Zed

1. **Open Zed Editor**
2. **Open the Assistant Panel**: Use `Cmd+?` (macOS) or `Ctrl+?` (Windows/Linux)
3. **Check Connection**: The assistant should connect to your ACP server
4. **View Logs**: Check stderr output for connection and message logs

### Debugging Connection Issues

If Zed doesn't connect:

1. **Test server manually**:
   ```bash
   cd examples/Andy.Acp.Examples
   echo '{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"1.0"}}' | dotnet run -- --server
   ```

2. **Check Zed logs**: Look for error messages in Zed's log panel

3. **Verify paths**: Ensure all paths in settings.json are absolute and correct

4. **Check permissions**: Ensure the binary is executable

## Example Workflow with Zed

Once connected, the ACP server provides these capabilities to Zed:

### Available Tools

- **ping**: Health check that returns current timestamp
- **echo**: Echoes back any data you send
- **test**: Demonstrates complex parameter handling

### Example Interaction

1. Zed sends `initialize` request with client capabilities
2. Server responds with server info and capabilities
3. Zed sends `initialized` notification
4. Session is now active
5. Zed can call tools: `ping`, `echo`, `test`
6. When done, Zed sends `shutdown` request
7. Server gracefully terminates after completing pending requests

## Server Capabilities

This example server advertises the following capabilities:

```json
{
  "tools": {
    "supported": true,
    "available": ["echo", "ping", "test"],
    "listSupported": true,
    "executionSupported": true
  },
  "resources": {
    "supported": true,
    "supportedSchemes": ["file://"]
  },
  "prompts": {
    "supported": false
  },
  "logging": {
    "supported": true,
    "supportedLevels": ["debug", "info", "warning", "error"]
  }
}
```

## Protocol Flow

```
Client (Zed)                    Server (Andy.Acp.Examples)
     |                                    |
     |------- initialize request -------->|
     |                                    |
     |<----- initialize response ---------|
     |    (server info + capabilities)    |
     |                                    |
     |------ initialized notification --->|
     |                                    |
     |========= Session Active ===========|
     |                                    |
     |--------- tool requests ----------->|
     |<-------- tool responses -----------|
     |                                    |
     |------- shutdown request ---------->|
     |                                    |
     |<------ shutdown response ----------|
     |    (waits for pending requests)    |
     |                                    |
     |=========== Terminated =============|
```

## Code Structure

- **Program.cs**: Main entry point with server and client modes
- **RegisterExampleMethods()**: Registers ACP protocol and example methods
- **RunServerAsync()**: Stdio transport server loop
- **RunClientAsync()**: Test client that sends sample requests

## Key Implementation Details

### Stdio Transport

All JSON-RPC messages use stdio:
- **stdin**: Receives messages from client (Zed)
- **stdout**: Sends messages to client
- **stderr**: Diagnostic logging (doesn't interfere with protocol)

### Session Management

- Creates session on `initialize` request
- Tracks pending requests for graceful shutdown
- 5-second timeout waiting for pending operations during shutdown
- Automatic session cleanup

### Error Handling

- Invalid JSON → Parse error response
- Unknown method → Method not found error
- Exception in handler → Internal error response
- All errors follow JSON-RPC 2.0 error format

## Extending This Example

### Adding New Tools

1. Register method in `RegisterExampleMethods()`:
   ```csharp
   handler.RegisterMethod("my_tool", async (parameters, ct) =>
   {
       // Tool implementation
       return new { result = "data" };
   });
   ```

2. Add to server capabilities:
   ```csharp
   Available = new[] { "echo", "ping", "test", "my_tool" }
   ```

### Supporting More Capabilities

Implement handlers for:
- `prompts/list` and `prompts/get`
- `resources/list` and `resources/read`
- `logging/setLevel`

## Troubleshooting

### "Connection refused" in Zed
- Verify the command path is absolute
- Check that `dotnet` is in PATH
- Test the command manually in terminal

### "Invalid JSON-RPC message"
- Check that no extra output goes to stdout (only stderr for logs)
- Verify message format follows JSON-RPC 2.0 spec

### "Session timeout"
- Check that `initialized` notification is sent after `initialize`
- Verify session is marked active

## Additional Resources

- [ACP Specification](https://agentclientprotocol.com/)
- [JSON-RPC 2.0 Specification](https://www.jsonrpc.org/specification)
- [Zed Documentation](https://zed.dev/docs)
- [andy-acp Repository](https://github.com/rivoli-ai/andy-acp)

## License

MIT - See repository LICENSE file
