# ACP Protocol Implementation Plan for Andy.Acp.Core

## Goal
Implement the complete Agent Client Protocol (ACP) in Andy.Acp.Core as a general-purpose library that any agent can use to integrate with ACP-compatible editors like Zed.

## Current State

### Already Implemented ✓
- `initialize` - Protocol handshake
- `initialized` - Notification confirming initialization
- `shutdown` - Graceful shutdown
- `tools/list` - List available tools (MCP-style)
- `tools/call` - Execute a tool (MCP-style)
- JSON-RPC 2.0 transport over stdio
- Session management infrastructure
- StdioTransport with Content-Length headers

### Missing (ACP Protocol Methods) ✗

#### Session Management
- `authenticate` - Optional authentication (may not be needed for local agents)
- `session/new` - Create a new conversation session
- `session/load` - Resume an existing session
- **`session/prompt`** - Send user message and receive agent response (CRITICAL)
- `session/set_mode` - Switch agent operating modes
- `session/set_model` - Select a model variant
- `session/cancel` - Cancel ongoing operations
- `session/request_permission` - Request user approval for sensitive operations
- `session/update` (notification) - Stream updates to client

#### File System Operations
- `fs/read_text_file` - Read file contents
- `fs/write_text_file` - Write/create files

#### Terminal Management
- `terminal/create` - Execute command in terminal
- `terminal/output` - Get current terminal output
- `terminal/wait_for_exit` - Block until command completes
- `terminal/kill` - Terminate command
- `terminal/release` - Free terminal resources

## Architecture Design

### Core Interfaces (Andy.Acp.Core)

```csharp
namespace Andy.Acp.Core.Agent
{
    /// <summary>
    /// Core interface that agent implementations must provide
    /// </summary>
    public interface IAgentProvider
    {
        /// <summary>
        /// Process a user prompt and return agent response
        /// </summary>
        Task<AgentResponse> ProcessPromptAsync(
            string sessionId,
            PromptMessage prompt,
            IResponseStreamer streamer,
            CancellationToken cancellationToken);

        /// <summary>
        /// Create a new session with optional context
        /// </summary>
        Task<SessionMetadata> CreateSessionAsync(
            NewSessionParams? parameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// Load an existing session
        /// </summary>
        Task<SessionMetadata> LoadSessionAsync(
            string sessionId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Cancel ongoing operations in a session
        /// </summary>
        Task CancelSessionAsync(string sessionId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// File system operations provider
    /// </summary>
    public interface IFileSystemProvider
    {
        Task<string> ReadTextFileAsync(string path, CancellationToken cancellationToken);
        Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Terminal operations provider
    /// </summary>
    public interface ITerminalProvider
    {
        Task<string> CreateTerminalAsync(
            string command,
            string? workingDirectory,
            Dictionary<string, string>? env,
            CancellationToken cancellationToken);

        Task<TerminalOutputResult> GetTerminalOutputAsync(
            string terminalId,
            CancellationToken cancellationToken);

        Task<int> WaitForTerminalExitAsync(
            string terminalId,
            CancellationToken cancellationToken);

        Task KillTerminalAsync(string terminalId, CancellationToken cancellationToken);
        Task ReleaseTerminalAsync(string terminalId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Streams response updates back to the client
    /// </summary>
    public interface IResponseStreamer
    {
        Task SendMessageChunkAsync(string text, CancellationToken cancellationToken);
        Task SendToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken);
        Task SendToolResultAsync(ToolResult result, CancellationToken cancellationToken);
    }
}
```

### Handler Classes (Andy.Acp.Core)

```csharp
namespace Andy.Acp.Core.Protocol
{
    // New handler for session-related methods
    public class AcpSessionHandler
    {
        public void RegisterMethods(JsonRpcHandler handler)
        {
            handler.RegisterMethod("session/new", HandleNewSessionAsync);
            handler.RegisterMethod("session/load", HandleLoadSessionAsync);
            handler.RegisterMethod("session/prompt", HandlePromptAsync);
            handler.RegisterMethod("session/set_mode", HandleSetModeAsync);
            handler.RegisterMethod("session/set_model", HandleSetModelAsync);
            handler.RegisterMethod("session/cancel", HandleCancelAsync);
            handler.RegisterMethod("session/request_permission", HandleRequestPermissionAsync);
        }
    }

    // New handler for file system methods
    public class AcpFileSystemHandler
    {
        public void RegisterMethods(JsonRpcHandler handler)
        {
            handler.RegisterMethod("fs/read_text_file", HandleReadTextFileAsync);
            handler.RegisterMethod("fs/write_text_file", HandleWriteTextFileAsync);
        }
    }

    // New handler for terminal methods
    public class AcpTerminalHandler
    {
        public void RegisterMethods(JsonRpcHandler handler)
        {
            handler.RegisterMethod("terminal/create", HandleCreateTerminalAsync);
            handler.RegisterMethod("terminal/output", HandleTerminalOutputAsync);
            handler.RegisterMethod("terminal/wait_for_exit", HandleWaitForExitAsync);
            handler.RegisterMethod("terminal/kill", HandleKillTerminalAsync);
            handler.RegisterMethod("terminal/release", HandleReleaseTerminalAsync);
        }
    }
}
```

### Server Host (Andy.Acp.Core)

```csharp
namespace Andy.Acp.Core.Server
{
    public class AcpServer
    {
        private readonly IAgentProvider _agentProvider;
        private readonly IFileSystemProvider? _fileSystemProvider;
        private readonly ITerminalProvider? _terminalProvider;

        public AcpServer(
            IAgentProvider agentProvider,
            IFileSystemProvider? fileSystemProvider = null,
            ITerminalProvider? terminalProvider = null,
            ServerInfo? serverInfo = null,
            ILoggerFactory? loggerFactory = null)
        {
            // Initialize server with all capabilities
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            // Main server loop
            // Register all handlers based on available providers
            // Handle stdio transport
        }
    }
}
```

## Implementation in Andy.CLI (Minimal Adapter)

```csharp
namespace Andy.Cli.ACP
{
    public class AndyAgentProvider : IAgentProvider
    {
        private readonly ILlmService _llmService;  // Andy's existing LLM service
        private readonly IToolExecutor _toolExecutor;  // Andy's existing tool executor

        public async Task<AgentResponse> ProcessPromptAsync(
            string sessionId,
            PromptMessage prompt,
            IResponseStreamer streamer,
            CancellationToken cancellationToken)
        {
            // 1. Send prompt to Andy's LLM
            // 2. Stream responses back via streamer.SendMessageChunkAsync()
            // 3. When LLM requests tool use, call _toolExecutor
            // 4. Stream tool results via streamer.SendToolResultAsync()
            // 5. Return final response
        }

        // Other methods...
    }

    public class AndyFileSystemProvider : IFileSystemProvider
    {
        // Wrap Andy's ReadFileTool and WriteFileTool
    }

    public class AndyTerminalProvider : ITerminalProvider
    {
        // Wrap Andy's BashCommandTool
    }
}
```

## Phase 1: Core Protocol Types (Priority 1)

### Files to Create in Andy.Acp.Core

1. `/Agent/IAgentProvider.cs` - Core agent interface
2. `/Agent/IResponseStreamer.cs` - Response streaming interface
3. `/Agent/AgentModels.cs` - PromptMessage, AgentResponse, etc.
4. `/FileSystem/IFileSystemProvider.cs` - File operations interface
5. `/Terminal/ITerminalProvider.cs` - Terminal operations interface
6. `/Terminal/TerminalModels.cs` - Terminal-related models

## Phase 2: Protocol Handlers (Priority 1)

7. `/Protocol/AcpSessionHandler.cs` - Implement session/* methods
8. `/Protocol/AcpFileSystemHandler.cs` - Implement fs/* methods
9. `/Protocol/AcpTerminalHandler.cs` - Implement terminal/* methods

## Phase 3: Server (Priority 1)

10. `/Server/AcpServer.cs` - Unified server that composes all handlers
11. `/Server/ResponseStreamer.cs` - Implementation of IResponseStreamer

## Phase 4: Andy.CLI Integration (Priority 2)

12. `/Andy.Cli.ACP/AndyAgentProvider.cs` - Wire up Andy's LLM
13. `/Andy.Cli.ACP/AndyFileSystemProvider.cs` - Wire up file tools
14. `/Andy.Cli.ACP/AndyTerminalProvider.cs` - Wire up bash tool
15. Update `Program.cs` to use new AcpServer

## Phase 5: Testing (Priority 3)

16. Add tests for all handlers
17. Integration test with Zed
18. Document the integration

## Success Criteria

- [x] Andy.Acp.Core implements all core ACP protocol methods
- [x] Andy.Acp.Core has no dependencies on Andy.CLI
- [x] Andy.CLI implementation is minimal (AndyAgentProvider ~200 lines)
- [x] Works with Zed editor (no "Loading..." hang)
- [x] Streaming responses work correctly (word-by-word via session/update)
- [x] Agent prompts execute and return results
- [ ] File and terminal operations work (providers defined, not yet implemented)

## Notes

- Keep MCP tools/list and tools/call for backwards compatibility
- Session management can be lightweight (in-memory sessions)
- Focus on session/prompt first - that's the critical path
- File/Terminal providers can be optional (graceful degradation)
