using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Andy.Acp.Core.Tools;
using Andy.Acp.Core.JsonRpc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Andy.Acp.Tests.Tools
{
    public class AcpToolResultTests
    {
        [Fact]
        public void Success_CreatesSuccessResult()
        {
            // Act
            var result = AcpToolResult.Success(new { message = "test" });

            // Assert
            Assert.False(result.IsError);
            Assert.Null(result.Error);
            Assert.NotNull(result.Result);
        }

        [Fact]
        public void Success_WithNullResult_CreatesSuccessResult()
        {
            // Act
            var result = AcpToolResult.Success(null);

            // Assert
            Assert.False(result.IsError);
            Assert.Null(result.Error);
            Assert.Null(result.Result);
        }

        [Fact]
        public void Failure_CreatesErrorResult()
        {
            // Act
            var result = AcpToolResult.Failure("test error");

            // Assert
            Assert.True(result.IsError);
            Assert.Equal("test error", result.Error);
            Assert.Null(result.Result);
        }
    }

    public class AcpToolDefinitionTests
    {
        [Fact]
        public void AcpToolDefinition_CanBeCreated()
        {
            // Arrange & Act
            var definition = new AcpToolDefinition
            {
                Name = "test_tool",
                Description = "A test tool",
                InputSchema = new AcpInputSchema
                {
                    Properties = new Dictionary<string, object>
                    {
                        ["param1"] = new { type = "string", description = "First parameter" }
                    },
                    Required = new List<string> { "param1" }
                }
            };

            // Assert
            Assert.Equal("test_tool", definition.Name);
            Assert.Equal("A test tool", definition.Description);
            Assert.NotNull(definition.InputSchema);
            Assert.Single(definition.InputSchema.Properties);
            Assert.Single(definition.InputSchema.Required);
        }

        [Fact]
        public void AcpInputSchema_DefaultsToObject()
        {
            // Arrange & Act
            var schema = new AcpInputSchema();

            // Assert
            Assert.Equal("object", schema.Type);
            Assert.Empty(schema.Properties);
            Assert.False(schema.AdditionalProperties);
        }
    }

    public class SimpleToolProviderTests
    {
        [Fact]
        public async Task ListToolsAsync_ReturnsDefaultTools()
        {
            // Arrange
            var provider = new SimpleToolProvider();

            // Act
            var tools = await provider.ListToolsAsync();
            var toolList = tools.ToList();

            // Assert
            Assert.Equal(4, toolList.Count);
            Assert.Contains(toolList, t => t.Name == "echo");
            Assert.Contains(toolList, t => t.Name == "calculator");
            Assert.Contains(toolList, t => t.Name == "get_time");
            Assert.Contains(toolList, t => t.Name == "reverse_string");
        }

        [Fact]
        public async Task ListToolsAsync_ReturnsToolsWithSchemas()
        {
            // Arrange
            var provider = new SimpleToolProvider();

            // Act
            var tools = await provider.ListToolsAsync();
            var echoTool = tools.First(t => t.Name == "echo");

            // Assert
            Assert.NotNull(echoTool.InputSchema);
            Assert.Equal("object", echoTool.InputSchema.Type);
            Assert.Contains("text", echoTool.InputSchema.Properties.Keys);
            Assert.Contains("text", echoTool.InputSchema.Required);
        }

        [Fact]
        public async Task ExecuteToolAsync_Echo_ReturnsEchoedText()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["text"] = "Hello, World!"
            };

            // Act
            var result = await provider.ExecuteToolAsync("echo", parameters);

            // Assert
            Assert.False(result.IsError);
            Assert.NotNull(result.Result);

            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal("Hello, World!", resultObj.GetProperty("echo").GetString());
            Assert.True(resultObj.TryGetProperty("timestamp", out _));
        }

        [Fact]
        public async Task ExecuteToolAsync_Calculator_Add_ReturnsSum()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["operation"] = "add",
                ["a"] = 15,
                ["b"] = 27
            };

            // Act
            var result = await provider.ExecuteToolAsync("calculator", parameters);

            // Assert
            Assert.False(result.IsError);
            Assert.NotNull(result.Result);

            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal(42, resultObj.GetProperty("result").GetDouble());
            Assert.Equal("add", resultObj.GetProperty("operation").GetString());
        }

        [Fact]
        public async Task ExecuteToolAsync_Calculator_Subtract_ReturnsDifference()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["operation"] = "subtract",
                ["a"] = 50,
                ["b"] = 8
            };

            // Act
            var result = await provider.ExecuteToolAsync("calculator", parameters);

            // Assert
            Assert.False(result.IsError);
            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal(42, resultObj.GetProperty("result").GetDouble());
        }

        [Fact]
        public async Task ExecuteToolAsync_Calculator_Multiply_ReturnsProduct()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["operation"] = "multiply",
                ["a"] = 6,
                ["b"] = 7
            };

            // Act
            var result = await provider.ExecuteToolAsync("calculator", parameters);

            // Assert
            Assert.False(result.IsError);
            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal(42, resultObj.GetProperty("result").GetDouble());
        }

        [Fact]
        public async Task ExecuteToolAsync_Calculator_Divide_ReturnsQuotient()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["operation"] = "divide",
                ["a"] = 84,
                ["b"] = 2
            };

            // Act
            var result = await provider.ExecuteToolAsync("calculator", parameters);

            // Assert
            Assert.False(result.IsError);
            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal(42, resultObj.GetProperty("result").GetDouble());
        }

        [Fact]
        public async Task ExecuteToolAsync_Calculator_DivideByZero_ReturnsError()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["operation"] = "divide",
                ["a"] = 42,
                ["b"] = 0
            };

            // Act
            var result = await provider.ExecuteToolAsync("calculator", parameters);

            // Assert
            Assert.True(result.IsError);
            Assert.Contains("Cannot divide by zero", result.Error);
        }

        [Fact]
        public async Task ExecuteToolAsync_Calculator_UnknownOperation_ReturnsError()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["operation"] = "power",
                ["a"] = 2,
                ["b"] = 3
            };

            // Act
            var result = await provider.ExecuteToolAsync("calculator", parameters);

            // Assert
            Assert.True(result.IsError);
            Assert.Contains("Unknown operation", result.Error);
        }

        [Fact]
        public async Task ExecuteToolAsync_Calculator_WithJsonElement_Works()
        {
            // Arrange
            var provider = new SimpleToolProvider();

            // Simulate parameters coming from JSON-RPC deserialization
            var json = JsonSerializer.Serialize(new
            {
                operation = "add",
                a = 15,
                b = 27
            });
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);

            var parameters = new Dictionary<string, object?>
            {
                ["operation"] = jsonElement.GetProperty("operation"),
                ["a"] = jsonElement.GetProperty("a"),
                ["b"] = jsonElement.GetProperty("b")
            };

            // Act
            var result = await provider.ExecuteToolAsync("calculator", parameters);

            // Assert
            Assert.False(result.IsError);
            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal(42, resultObj.GetProperty("result").GetDouble());
        }

        [Fact]
        public async Task ExecuteToolAsync_GetTime_ReturnsCurrentTime()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["timezone"] = "UTC"
            };

            // Act
            var result = await provider.ExecuteToolAsync("get_time", parameters);

            // Assert
            Assert.False(result.IsError);
            Assert.NotNull(result.Result);

            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal("UTC", resultObj.GetProperty("timezone").GetString());
            Assert.True(resultObj.TryGetProperty("time", out _));
            Assert.True(resultObj.TryGetProperty("unix", out _));
        }

        [Fact]
        public async Task ExecuteToolAsync_GetTime_DefaultsToUTC()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>();

            // Act
            var result = await provider.ExecuteToolAsync("get_time", parameters);

            // Assert
            Assert.False(result.IsError);
            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal("UTC", resultObj.GetProperty("timezone").GetString());
        }

        [Fact]
        public async Task ExecuteToolAsync_ReverseString_ReturnsReversedText()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var parameters = new Dictionary<string, object?>
            {
                ["text"] = "Hello"
            };

            // Act
            var result = await provider.ExecuteToolAsync("reverse_string", parameters);

            // Assert
            Assert.False(result.IsError);
            Assert.NotNull(result.Result);

            var resultJson = JsonSerializer.Serialize(result.Result);
            var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
            Assert.Equal("olleH", resultObj.GetProperty("reversed").GetString());
            Assert.Equal("Hello", resultObj.GetProperty("original").GetString());
            Assert.Equal(5, resultObj.GetProperty("length").GetInt32());
        }

        [Fact]
        public async Task ExecuteToolAsync_UnknownTool_ReturnsError()
        {
            // Arrange
            var provider = new SimpleToolProvider();

            // Act
            var result = await provider.ExecuteToolAsync("unknown_tool", null);

            // Assert
            Assert.True(result.IsError);
            Assert.Contains("not found", result.Error);
        }

        [Fact]
        public async Task RegisterTool_AddsNewTool()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var definition = new AcpToolDefinition
            {
                Name = "custom_tool",
                Description = "A custom tool",
                InputSchema = new AcpInputSchema()
            };

            // Act
            provider.RegisterTool(definition, _ => Task.FromResult(AcpToolResult.Success("custom result")));
            var tools = await provider.ListToolsAsync();

            // Assert
            Assert.Contains(tools, t => t.Name == "custom_tool");
        }

        [Fact]
        public async Task RegisterTool_CanExecuteCustomTool()
        {
            // Arrange
            var provider = new SimpleToolProvider();
            var definition = new AcpToolDefinition
            {
                Name = "custom_tool",
                Description = "A custom tool",
                InputSchema = new AcpInputSchema()
            };

            provider.RegisterTool(definition, _ => Task.FromResult(AcpToolResult.Success("custom result")));

            // Act
            var result = await provider.ExecuteToolAsync("custom_tool", null);

            // Assert
            Assert.False(result.IsError);
            Assert.Equal("custom result", result.Result);
        }
    }

    public class AcpToolsHandlerTests
    {
        private class MockToolProvider : IAcpToolProvider
        {
            public List<AcpToolDefinition> Tools { get; } = new();
            public Dictionary<string, Func<Dictionary<string, object?>?, Task<AcpToolResult>>> Implementations { get; } = new();

            public Task<IEnumerable<AcpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IEnumerable<AcpToolDefinition>>(Tools);
            }

            public Task<AcpToolResult> ExecuteToolAsync(string name, Dictionary<string, object?>? parameters, CancellationToken cancellationToken = default)
            {
                if (Implementations.TryGetValue(name, out var impl))
                {
                    return impl(parameters);
                }
                return Task.FromResult(AcpToolResult.Failure($"Tool '{name}' not found"));
            }
        }

        [Fact]
        public async Task HandleToolsListAsync_ReturnsToolsList()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            mockProvider.Tools.Add(new AcpToolDefinition
            {
                Name = "test_tool",
                Description = "Test tool",
                InputSchema = new AcpInputSchema()
            });

            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            // Act
            var result = await handler.HandleToolsListAsync(null);

            // Assert
            Assert.NotNull(result);
            var toolsResult = Assert.IsType<ToolsListResult>(result);
            Assert.Single(toolsResult.Tools);
            Assert.Equal("test_tool", toolsResult.Tools.First().Name);
        }

        [Fact]
        public async Task HandleToolsListAsync_WithMultipleTools_ReturnsAll()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            mockProvider.Tools.Add(new AcpToolDefinition { Name = "tool1", Description = "Tool 1", InputSchema = new AcpInputSchema() });
            mockProvider.Tools.Add(new AcpToolDefinition { Name = "tool2", Description = "Tool 2", InputSchema = new AcpInputSchema() });
            mockProvider.Tools.Add(new AcpToolDefinition { Name = "tool3", Description = "Tool 3", InputSchema = new AcpInputSchema() });

            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            // Act
            var result = await handler.HandleToolsListAsync(null);

            // Assert
            var toolsResult = Assert.IsType<ToolsListResult>(result);
            Assert.Equal(3, toolsResult.Tools.Count());
        }

        [Fact]
        public async Task HandleToolsCallAsync_WithValidTool_ReturnsResult()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            mockProvider.Implementations["test_tool"] = _ => Task.FromResult(AcpToolResult.Success(new { message = "success" }));

            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            var paramsJson = JsonSerializer.Serialize(new
            {
                name = "test_tool",
                parameters = new Dictionary<string, object> { ["input"] = "test" }
            });
            var paramsElement = JsonSerializer.Deserialize<JsonElement>(paramsJson);

            // Act
            var result = await handler.HandleToolsCallAsync(paramsElement);

            // Assert
            Assert.NotNull(result);
            var toolResult = Assert.IsType<AcpToolResult>(result);
            Assert.False(toolResult.IsError);
        }

        [Fact]
        public async Task HandleToolsCallAsync_WithUnknownTool_ReturnsError()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            var paramsJson = JsonSerializer.Serialize(new
            {
                name = "unknown_tool",
                parameters = new Dictionary<string, object>()
            });
            var paramsElement = JsonSerializer.Deserialize<JsonElement>(paramsJson);

            // Act
            var result = await handler.HandleToolsCallAsync(paramsElement);

            // Assert
            var toolResult = Assert.IsType<AcpToolResult>(result);
            Assert.True(toolResult.IsError);
            Assert.Contains("not found", toolResult.Error);
        }

        [Fact]
        public async Task HandleToolsCallAsync_WithNullParameters_ThrowsException()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            // Act & Assert
            await Assert.ThrowsAsync<JsonRpcProtocolException>(async () =>
            {
                await handler.HandleToolsCallAsync(null);
            });
        }

        [Fact]
        public async Task HandleToolsCallAsync_WithMissingName_ThrowsException()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            var paramsJson = JsonSerializer.Serialize(new
            {
                parameters = new Dictionary<string, object>()
            });
            var paramsElement = JsonSerializer.Deserialize<JsonElement>(paramsJson);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<JsonRpcProtocolException>(async () =>
            {
                await handler.HandleToolsCallAsync(paramsElement);
            });

            Assert.Equal(JsonRpcErrorCodes.InvalidParams, exception.ErrorCode);
        }

        [Fact]
        public async Task HandleToolsCallAsync_WithInvalidJson_ThrowsException()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            // Pass a non-JsonElement object
            var invalidParams = new { invalid = "structure" };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<JsonRpcProtocolException>(async () =>
            {
                await handler.HandleToolsCallAsync(invalidParams);
            });

            Assert.Equal(JsonRpcErrorCodes.InvalidParams, exception.ErrorCode);
        }

        [Fact]
        public async Task HandleToolsCallAsync_WithToolException_ReturnsFailureResult()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            mockProvider.Implementations["failing_tool"] = _ => throw new InvalidOperationException("Tool failed");

            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            var paramsJson = JsonSerializer.Serialize(new
            {
                name = "failing_tool",
                parameters = new Dictionary<string, object>()
            });
            var paramsElement = JsonSerializer.Deserialize<JsonElement>(paramsJson);

            // Act
            var result = await handler.HandleToolsCallAsync(paramsElement);

            // Assert
            var toolResult = Assert.IsType<AcpToolResult>(result);
            Assert.True(toolResult.IsError);
            Assert.Contains("Tool execution failed", toolResult.Error);
        }

        [Fact]
        public void RegisterMethods_RegistersToolMethods()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);
            var jsonRpcHandler = new JsonRpcHandler(NullLogger<JsonRpcHandler>.Instance);

            // Act
            handler.RegisterMethods(jsonRpcHandler);

            // Assert
            var methods = jsonRpcHandler.GetSupportedMethods();
            Assert.Contains("tools/list", methods);
            Assert.Contains("tools/call", methods);
        }

        [Fact]
        public void RegisterMethods_WithNullHandler_ThrowsException()
        {
            // Arrange
            var mockProvider = new MockToolProvider();
            var handler = new AcpToolsHandler(mockProvider, NullLogger<AcpToolsHandler>.Instance);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => handler.RegisterMethods(null!));
        }

        [Fact]
        public void Constructor_WithNullProvider_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AcpToolsHandler(null!, NullLogger<AcpToolsHandler>.Instance));
        }
    }

    public class ToolsCallParamsTests
    {
        [Fact]
        public void Deserialize_WithLowercaseProperties_Works()
        {
            // Arrange
            var json = @"{""name"":""test_tool"",""parameters"":{""key"":""value""}}";

            // Act
            var result = JsonSerializer.Deserialize<ToolsCallParams>(json);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test_tool", result.Name);
            Assert.NotNull(result.Parameters);
            Assert.Single(result.Parameters);
        }

        [Fact]
        public void Serialize_UsesLowercaseProperties()
        {
            // Arrange
            var callParams = new ToolsCallParams
            {
                Name = "test_tool",
                Parameters = new Dictionary<string, object?> { ["key"] = "value" }
            };

            // Act
            var json = JsonSerializer.Serialize(callParams);

            // Assert
            Assert.Contains("\"name\"", json);
            Assert.Contains("\"parameters\"", json);
            Assert.DoesNotContain("\"Name\"", json);
            Assert.DoesNotContain("\"Parameters\"", json);
        }
    }
}
