# Andy ACP Examples

This example demonstrates a working ACP (Agent Client Protocol) server implementation that can be tested with Zed editor and other ACP clients.

## What This Example Demonstrates

- Complete ACP initialization handshake (initialize → initialized → shutdown)
- JSON-RPC 2.0 message handling over stdio transport
- Session management with timeout tracking
- Server capabilities advertisement
- Tool framework with 4 example tools (echo, calculator, get_time, reverse_string)
- tools/list and tools/call protocol methods
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

- **echo**: Echoes back the provided text with timestamp
  - Parameters: `text` (string, required)
  - Example: `{"text": "Hello from Zed!"}`

- **calculator**: Performs basic arithmetic operations
  - Parameters: `operation` (add/subtract/multiply/divide), `a` (number), `b` (number)
  - Example: `{"operation": "add", "a": 15, "b": 27}`

- **get_time**: Returns the current date and time
  - Parameters: `timezone` (string, optional, defaults to "UTC")
  - Example: `{"timezone": "UTC"}`

- **reverse_string**: Reverses the provided string
  - Parameters: `text` (string, required)
  - Example: `{"text": "Hello"}`

### Example Interaction

1. Zed sends `initialize` request with client capabilities
2. Server responds with server info and capabilities
3. Zed sends `initialized` notification
4. Session is now active
5. Zed can discover tools via `tools/list`
6. Zed can execute tools via `tools/call` with tool name and parameters
7. When done, Zed sends `shutdown` request
8. Server gracefully terminates after completing pending requests

## Server Capabilities

This example server advertises the following capabilities:

```json
{
  "tools": {
    "supported": true,
    "available": ["echo", "calculator", "get_time", "reverse_string"],
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
     |------- tools/list request -------->|
     |                                    |
     |<----- tools/list response ---------|
     |  (tool definitions + schemas)      |
     |                                    |
     |------- tools/call request -------->|
     |   (name: "calculator", params)     |
     |                                    |
     |<----- tools/call response ---------|
     |        (tool result)               |
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

1. Add to SimpleToolProvider or create your own IAcpToolProvider:
   ```csharp
   toolProvider.RegisterTool(
       new AcpToolDefinition
       {
           Name = "my_tool",
           Description = "Description of my tool",
           InputSchema = new AcpInputSchema
           {
               Properties = new Dictionary<string, object>
               {
                   ["param1"] = new
                   {
                       type = "string",
                       description = "Parameter description"
                   }
               },
               Required = new List<string> { "param1" }
           }
       },
       parameters =>
       {
           // Tool implementation
           var param = parameters?.GetValueOrDefault("param1")?.ToString();
           return Task.FromResult(AcpToolResult.Success(new { result = "data" }));
       }
   );
   ```

2. The tool will automatically be:
   - Listed in server capabilities
   - Discoverable via tools/list
   - Executable via tools/call

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
