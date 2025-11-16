# Andy.Tools vs ACP Tool Interfaces Comparison

This document compares the existing andy-tools abstractions with the proposed ACP tool interfaces to ensure alignment and avoid duplication.

## Andy.Tools Architecture (Existing - Mature)

### Core Interfaces

#### `ITool` - Tool Implementation Interface
```csharp
public interface ITool
{
    ToolMetadata Metadata { get; }
    Task InitializeAsync(Dictionary<string, object?>? configuration, CancellationToken ct);
    Task<ToolResult> ExecuteAsync(Dictionary<string, object?> parameters, ToolExecutionContext context);
    IList<string> ValidateParameters(Dictionary<string, object?> parameters);
    bool CanExecuteWithPermissions(ToolPermissions permissions);
    Task DisposeAsync(CancellationToken ct);
}
```

**Key Features:**
- Lifecycle management (Initialize, Execute, Dispose)
- Built-in parameter validation
- Permission checking
- Execution context support

#### `IToolRegistry` - Tool Discovery & Management
```csharp
public interface IToolRegistry
{
    IReadOnlyList<ToolRegistration> Tools { get; }
    ToolRegistration RegisterTool<T>(Dictionary<string, object?>? configuration) where T : class, ITool;
    ToolRegistration RegisterTool(ToolMetadata metadata, Func<IServiceProvider, ITool> factory, ...);
    ITool? CreateTool(string toolId, IServiceProvider serviceProvider);
    ToolRegistration? GetTool(string toolId);
    IReadOnlyList<ToolRegistration> GetTools(ToolCategory? category, ...);
    bool SetToolEnabled(string toolId, bool enabled);
    // Events: ToolRegistered, ToolUnregistered
}
```

**Key Features:**
- Centralized tool registration
- Factory-based tool creation
- Dependency injection support
- Query/filter capabilities
- Enable/disable tools dynamically
- Event notifications

#### `IToolExecutor` - Secure Tool Execution
```csharp
public interface IToolExecutor
{
    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request);
    Task<IList<string>> ValidateExecutionRequestAsync(ToolExecutionRequest request);
    Task<ToolResourceUsage?> EstimateResourceUsageAsync(string toolId, Dictionary<string, object?> parameters);
    Task<int> CancelExecutionsAsync(string correlationId);
    IReadOnlyList<RunningExecutionInfo> GetRunningExecutions();
    ToolExecutionStatistics GetStatistics();
    // Events: ExecutionStarted, ExecutionCompleted, SecurityViolation
}
```

**Key Features:**
- Security enforcement (permissions, resource limits)
- Execution tracking and cancellation
- Performance metrics
- Resource estimation
- Security violation detection

### Data Models

#### `ToolMetadata` - Rich Tool Description
```csharp
public class ToolMetadata
{
    string Id, Name, Description, Version, Author;
    ToolCategory Category;
    IList<string> Tags;
    ToolCapability RequiredCapabilities;
    ToolPermissionFlags RequiredPermissions;
    IList<ToolParameter> Parameters;
    IList<ToolExample> Examples;
    string? HelpUrl;
    bool IsDeprecated, IsExperimental, RequiresConfirmation;
    int? EstimatedExecutionTimeMs;
    long? MaxInputSizeBytes;
    object? OutputSchema;
    IList<string> SupportedPlatforms;
    IList<ToolDependency> Dependencies;
    Dictionary<string, object?> AdditionalMetadata;
}
```

#### `ToolParameter` - Comprehensive Parameter Schema
```csharp
public class ToolParameter
{
    string Name, Description, Type;
    bool Required;
    object? DefaultValue;
    IList<object>? AllowedValues;
    double? MinValue, MaxValue;
    int? MinLength, MaxLength;
    string? Pattern, Format;
    IList<object>? Examples;
    object? Schema; // JSON Schema
    ToolParameter? ItemType; // For arrays
    Dictionary<string, object?> Metadata;
}
```

#### `ToolResult` - Execution Result
```csharp
public class ToolResult
{
    bool IsSuccessful;
    object? Data;
    string? ErrorMessage;
    Dictionary<string, object?> Metadata;
    double? DurationMs;
    string? Message;

    // Convenience methods
    static ToolResult Success(object? data, Dictionary<string, object?>? metadata);
    static ToolResult Failure(string errorMessage, Dictionary<string, object?>? metadata);
}
```

## ACP Protocol Requirements

The ACP protocol defines these tool-related methods:

### Protocol Methods

#### `tools/list`
- **Request**: No parameters (or optional filters)
- **Response**: Array of tool definitions with names, descriptions, parameters

#### `tools/call`
- **Request**: `{ name: string, parameters: object }`
- **Response**: `{ result: any, isError: boolean, error?: string }`

### ACP Tool Format (MCP-compatible)
```typescript
{
  name: string;
  description: string;
  inputSchema: {
    type: "object";
    properties: { [key: string]: JSONSchema };
    required?: string[];
  }
}
```

## Comparison & Analysis

| Aspect | Andy.Tools | ACP Protocol | Alignment |
|--------|-----------|--------------|-----------|
| **Tool Interface** | `ITool` with lifecycle | Simple execute function | ⚠️ Different levels |
| **Registration** | `IToolRegistry` | Not specified | ✅ Can adapt |
| **Metadata** | Rich `ToolMetadata` | Minimal schema | ✅ Subset works |
| **Parameters** | Comprehensive validation | JSON Schema | ✅ Compatible |
| **Execution** | `IToolExecutor` with security | Simple call | ⚠️ Different levels |
| **Results** | `ToolResult` with metadata | Simple result/error | ✅ Can map |
| **Permissions** | Built-in security | Not specified | ➕ Andy.Tools advantage |
| **Lifecycle** | Init/Execute/Dispose | Execute only | ➕ Andy.Tools advantage |
| **Metrics** | Built-in tracking | Not specified | ➕ Andy.Tools advantage |

## Recommendations

### 1. **Layered Architecture** ✅ RECOMMENDED

```
┌─────────────────────────────────────┐
│   ACP Protocol (andy-acp)           │
│   - Lightweight protocol models     │
│   - JSON-RPC handlers               │
│   - Protocol-specific interfaces    │
└─────────────────────────────────────┘
              ↓ Adapter
┌─────────────────────────────────────┐
│   Andy.Tools (andy-tools)           │
│   - Rich tool implementation        │
│   - Security & validation           │
│   - Metrics & observability         │
└─────────────────────────────────────┘
              ↓ Uses
┌─────────────────────────────────────┐
│   Concrete Tools (andy-cli)         │
│   - File operations                 │
│   - Git commands                    │
│   - Code analysis                   │
└─────────────────────────────────────┘
```

### 2. **ACP Layer Interfaces** (andy-acp repository)

Keep ACP-specific, minimal interfaces aligned with protocol:

```csharp
// ACP-specific tool provider (protocol adapter)
public interface IAcpToolProvider
{
    Task<IEnumerable<AcpToolDefinition>> ListToolsAsync(CancellationToken ct);
    Task<AcpToolResult> ExecuteToolAsync(string name, Dictionary<string, object?> parameters, CancellationToken ct);
}

// Minimal ACP tool definition (protocol-aligned)
public class AcpToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required AcpInputSchema InputSchema { get; set; }
}

// JSON Schema for parameters
public class AcpInputSchema
{
    public string Type { get; set; } = "object";
    public Dictionary<string, object> Properties { get; set; } = new();
    public List<string>? Required { get; set; }
}

// Simple result model (protocol-aligned)
public class AcpToolResult
{
    public object? Result { get; set; }
    public bool IsError { get; set; }
    public string? Error { get; set; }
}
```

**Why keep it simple?**
- andy-acp is a protocol library, not a tool framework
- ACP protocol has specific, minimal format
- Keeps andy-acp lightweight and reusable
- Other apps can implement `IAcpToolProvider` easily

### 3. **Andy.Tools Adapter** (andy-cli repository)

Create adapter that wraps andy-tools for ACP:

```csharp
public class AndyToolsAcpAdapter : IAcpToolProvider
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolExecutor _toolExecutor;

    public async Task<IEnumerable<AcpToolDefinition>> ListToolsAsync(CancellationToken ct)
    {
        var tools = _toolRegistry.GetTools(enabledOnly: true);
        return tools.Select(ConvertToAcpDefinition);
    }

    public async Task<AcpToolResult> ExecuteToolAsync(string name, Dictionary<string, object?> parameters, CancellationToken ct)
    {
        try
        {
            var request = new ToolExecutionRequest
            {
                ToolId = name,
                Parameters = parameters,
                // Map ACP context to andy-tools context
                Context = CreateExecutionContext()
            };

            var result = await _toolExecutor.ExecuteAsync(request);

            return new AcpToolResult
            {
                Result = result.Result?.Data,
                IsError = !result.IsSuccessful,
                Error = result.Result?.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new AcpToolResult
            {
                IsError = true,
                Error = ex.Message
            };
        }
    }

    private AcpToolDefinition ConvertToAcpDefinition(ToolRegistration registration)
    {
        // Convert andy-tools metadata to ACP format
        return new AcpToolDefinition
        {
            Name = registration.Metadata.Id,
            Description = registration.Metadata.Description,
            InputSchema = ConvertParametersToSchema(registration.Metadata.Parameters)
        };
    }

    private AcpInputSchema ConvertParametersToSchema(IList<ToolParameter> parameters)
    {
        // Convert andy-tools parameters to JSON Schema
        var schema = new AcpInputSchema();

        foreach (var param in parameters)
        {
            schema.Properties[param.Name] = new
            {
                type = param.Type,
                description = param.Description,
                // Map all andy-tools parameter features to JSON Schema
                minimum = param.MinValue,
                maximum = param.MaxValue,
                minLength = param.MinLength,
                maxLength = param.MaxLength,
                pattern = param.Pattern,
                format = param.Format,
                @enum = param.AllowedValues,
                @default = param.DefaultValue
            };

            if (param.Required)
            {
                schema.Required ??= new List<string>();
                schema.Required.Add(param.Name);
            }
        }

        return schema;
    }
}
```

### 4. **Benefits of This Approach**

✅ **Separation of Concerns**
- andy-acp: Protocol layer (lightweight, reusable)
- andy-tools: Rich tool framework (security, metrics, validation)
- andy-cli: Concrete tools + adapter

✅ **Reusability**
- Any app can implement `IAcpToolProvider` without andy-tools dependency
- andy-cli gets full andy-tools benefits (security, metrics, etc.)
- Protocol stays simple and aligned with ACP spec

✅ **Migration Path**
- Start with simple tools in andy-acp examples
- andy-cli uses full andy-tools with adapter
- Other projects choose their level of sophistication

✅ **No Duplication**
- ACP models are protocol-specific, minimal
- Andy.Tools models are rich, comprehensive
- Adapter maps between them

✅ **Testability**
- Test protocol separately from tool logic
- Test andy-tools integration via adapter
- Mock `IAcpToolProvider` for ACP protocol tests

## Implementation Plan

### Phase 1: ACP Protocol Layer (andy-acp issue #6)
1. Define `IAcpToolProvider` interface
2. Define minimal ACP models (AcpToolDefinition, AcpInputSchema, AcpToolResult)
3. Implement `tools/list` handler
4. Implement `tools/call` handler
5. Create simple example implementation (ping, echo)
6. Comprehensive tests

### Phase 2: Andy.Tools Adapter (andy-cli issue #16)
1. Add andy-tools NuGet reference to andy-cli
2. Implement `AndyToolsAcpAdapter`
3. Convert ToolMetadata → AcpToolDefinition
4. Convert ToolParameter → JSON Schema
5. Map ToolExecutionResult → AcpToolResult
6. Register real tools (file, git, code, search)
7. Integration tests

### Phase 3: Advanced Features (future)
1. Stream tool results for long-running operations
2. Tool cancellation support
3. Progress notifications
4. Resource usage reporting
5. Permission confirmation flows

## Conclusion

**Do NOT import andy-tools into andy-acp.** Instead:

1. **andy-acp** provides lightweight protocol interfaces aligned with ACP spec
2. **andy-cli** uses andy-tools and creates an adapter
3. This keeps andy-acp generic and reusable
4. andy-cli gets all andy-tools benefits (security, metrics, validation)
5. Clear separation: protocol vs implementation

This approach leverages the maturity of andy-tools while keeping the ACP protocol layer simple and protocol-aligned.
