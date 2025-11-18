# Simple Echo Agent Example

This is a minimal example of an ACP-compatible agent that demonstrates the core concepts of the Agent Client Protocol.

## What It Does

The Simple Echo Agent:
- Accepts user prompts via the ACP protocol
- Echoes back the message with a simple prefix
- Demonstrates streaming responses (word-by-word)
- Shows session management basics

This is perfect for:
- Testing Zed integration
- Learning how to implement `IAgentProvider`
- Understanding ACP protocol flow
- Debugging ACP communication

## Building

```bash
# From the andy-acp root directory
dotnet build examples/SimpleEchoAgent -c Release

# Or from this directory
dotnet build -c Release
```

## Running

### Standalone Mode (for testing)

```bash
dotnet run --project examples/SimpleEchoAgent -- --acp
```

The agent will start and wait for ACP protocol messages on stdin.

### With Zed Editor

1. Build the example:
   ```bash
   dotnet build examples/SimpleEchoAgent -c Release
   ```

2. Configure Zed by editing `~/.config/zed/settings.json`:
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

3. Restart Zed or reload the assistant panel

4. Open the Assistant panel and try sending a message. You should see your message echoed back!

## Code Structure

### SimpleEchoAgentProvider.cs

The main agent implementation that shows:
- **Session Management**: Creating and loading sessions
- **Prompt Processing**: Handling user messages
- **Response Streaming**: Sending chunks via `IResponseStreamer`
- **Capabilities**: Declaring what the agent supports

Key methods:
- `CreateSessionAsync()` - Initialize a new conversation session
- `LoadSessionAsync()` - Restore an existing session
- `ProcessPromptAsync()` - Handle user prompts and generate responses
- `GetCapabilities()` - Declare agent features

### Program.cs

Shows how to:
- Create an `AcpServer` instance
- Wire up the agent provider
- Configure logging (to stderr to avoid interfering with protocol)
- Run the server in stdio mode

## Testing

You can test the agent manually using the ACP protocol:

```bash
# Start the agent
dotnet run --project examples/SimpleEchoAgent -- --acp

# In another terminal, send JSON-RPC messages:
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{"promptFormats":{"text":true}}}}' | dotnet run --project examples/SimpleEchoAgent -- --acp
```

Or use the test script if available in the examples directory.

## Key Concepts Demonstrated

### 1. IAgentProvider Interface

The core interface that all ACP agents must implement:
- Session lifecycle management
- Prompt processing with streaming
- Capability advertisement

### 2. Streaming Responses

The agent demonstrates proper streaming using `IResponseStreamer`:
```csharp
foreach (var word in words)
{
    await streamer.SendMessageChunkAsync(word + " ", cancellationToken);
    await Task.Delay(10, cancellationToken); // Simulate typing
}
```

### 3. Session State

Shows how to maintain conversation history and session metadata:
- Created/LastAccessed timestamps
- Message history
- Session-specific settings (mode, model)

### 4. ACP Server Integration

Demonstrates the minimal setup needed to run an ACP server:
```csharp
var acpServer = new AcpServer(
    agentProvider: agentProvider,
    fileSystemProvider: null,
    terminalProvider: null,
    serverInfo: serverInfo,
    loggerFactory: loggerFactory
);

await acpServer.RunAsync();
```

## Next Steps

To create a real agent based on this example:

1. Replace the echo logic in `ProcessPromptAsync()` with your LLM integration
2. Add tool execution if needed
3. Implement file system provider for file operations
4. Implement terminal provider for shell commands
5. Add proper error handling and validation

See the Andy.CLI integration for a full example with LLM and tools.

## Troubleshooting

### Agent doesn't show up in Zed

- Check that the path in settings.json is absolute and correct
- Verify the executable exists: `ls -l /path/to/SimpleEchoAgent`
- Check Zed logs for errors

### Messages not appearing

- Ensure all `Console.WriteLine` calls go to `Console.Error` (not stdout)
- Check that logging is configured to use stderr
- Verify the agent is receiving messages by checking logs

### Build errors

- Make sure you're building from the andy-acp root directory
- Ensure Andy.Acp.Core is built: `dotnet build src/Andy.Acp.Core`
- Check that .NET 8.0 SDK is installed: `dotnet --version`
