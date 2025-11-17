using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.Tools
{
    /// <summary>
    /// A simple implementation of IAcpToolProvider for demonstration purposes.
    /// Provides basic example tools that can be used to test the ACP protocol.
    /// </summary>
    public class SimpleToolProvider : IAcpToolProvider
    {
        private readonly Dictionary<string, Func<Dictionary<string, object?>?, Task<AcpToolResult>>> _tools = new();
        private readonly List<AcpToolDefinition> _toolDefinitions = new();

        /// <summary>
        /// Initializes a new instance of the SimpleToolProvider class with default tools.
        /// </summary>
        public SimpleToolProvider()
        {
            RegisterDefaultTools();
        }

        /// <summary>
        /// Registers a tool with the provider.
        /// </summary>
        /// <param name="definition">The tool definition.</param>
        /// <param name="implementation">The tool implementation function.</param>
        public void RegisterTool(
            AcpToolDefinition definition,
            Func<Dictionary<string, object?>?, Task<AcpToolResult>> implementation)
        {
            _toolDefinitions.Add(definition);
            _tools[definition.Name] = implementation;
        }

        /// <inheritdoc />
        public Task<IEnumerable<AcpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<AcpToolDefinition>>(_toolDefinitions);
        }

        /// <inheritdoc />
        public async Task<AcpToolResult> ExecuteToolAsync(
            string name,
            Dictionary<string, object?>? parameters,
            CancellationToken cancellationToken = default)
        {
            if (!_tools.TryGetValue(name, out var implementation))
            {
                return AcpToolResult.Failure($"Tool '{name}' not found");
            }

            try
            {
                return await implementation(parameters);
            }
            catch (Exception ex)
            {
                return AcpToolResult.Failure($"Tool execution failed: {ex.Message}");
            }
        }

        private void RegisterDefaultTools()
        {
            // Echo tool - echoes back input
            RegisterTool(
                new AcpToolDefinition
                {
                    Name = "echo",
                    Description = "Echoes back the provided text",
                    InputSchema = new AcpInputSchema
                    {
                        Properties = new Dictionary<string, object>
                        {
                            ["text"] = new
                            {
                                type = "string",
                                description = "The text to echo back"
                            }
                        },
                        Required = new List<string> { "text" }
                    }
                },
                parameters =>
                {
                    var text = parameters?.GetValueOrDefault("text")?.ToString() ?? "";
                    return Task.FromResult(AcpToolResult.Success(new
                    {
                        echo = text,
                        timestamp = DateTime.UtcNow.ToString("O")
                    }));
                }
            );

            // Calculator tool - performs basic arithmetic
            RegisterTool(
                new AcpToolDefinition
                {
                    Name = "calculator",
                    Description = "Performs basic arithmetic operations (add, subtract, multiply, divide)",
                    InputSchema = new AcpInputSchema
                    {
                        Properties = new Dictionary<string, object>
                        {
                            ["operation"] = new
                            {
                                type = "string",
                                description = "The operation to perform",
                                @enum = new[] { "add", "subtract", "multiply", "divide" }
                            },
                            ["a"] = new
                            {
                                type = "number",
                                description = "First operand"
                            },
                            ["b"] = new
                            {
                                type = "number",
                                description = "Second operand"
                            }
                        },
                        Required = new List<string> { "operation", "a", "b" }
                    }
                },
                parameters =>
                {
                    try
                    {
                        var operation = parameters?.GetValueOrDefault("operation")?.ToString() ?? "";

                        // Handle JsonElement from JSON-RPC deserialization
                        var aValue = parameters?.GetValueOrDefault("a");
                        var bValue = parameters?.GetValueOrDefault("b");

                        double a = aValue is System.Text.Json.JsonElement aElement
                            ? aElement.GetDouble()
                            : Convert.ToDouble(aValue);

                        double b = bValue is System.Text.Json.JsonElement bElement
                            ? bElement.GetDouble()
                            : Convert.ToDouble(bValue);

                        double result = operation switch
                        {
                            "add" => a + b,
                            "subtract" => a - b,
                            "multiply" => a * b,
                            "divide" => b == 0 ? throw new DivideByZeroException("Cannot divide by zero") : a / b,
                            _ => throw new ArgumentException($"Unknown operation: {operation}")
                        };

                        return Task.FromResult(AcpToolResult.Success(new
                        {
                            operation,
                            a,
                            b,
                            result
                        }));
                    }
                    catch (Exception ex)
                    {
                        return Task.FromResult(AcpToolResult.Failure(ex.Message));
                    }
                }
            );

            // Get time tool - returns current time
            RegisterTool(
                new AcpToolDefinition
                {
                    Name = "get_time",
                    Description = "Returns the current date and time",
                    InputSchema = new AcpInputSchema
                    {
                        Properties = new Dictionary<string, object>
                        {
                            ["timezone"] = new
                            {
                                type = "string",
                                description = "Timezone (optional, defaults to UTC)",
                                @default = "UTC"
                            }
                        }
                    }
                },
                parameters =>
                {
                    var timezone = parameters?.GetValueOrDefault("timezone")?.ToString() ?? "UTC";
                    var time = timezone.ToLowerInvariant() == "utc"
                        ? DateTime.UtcNow
                        : DateTime.Now;

                    return Task.FromResult(AcpToolResult.Success(new
                    {
                        timezone,
                        time = time.ToString("O"),
                        unix = new DateTimeOffset(time).ToUnixTimeSeconds()
                    }));
                }
            );

            // Reverse string tool
            RegisterTool(
                new AcpToolDefinition
                {
                    Name = "reverse_string",
                    Description = "Reverses the provided string",
                    InputSchema = new AcpInputSchema
                    {
                        Properties = new Dictionary<string, object>
                        {
                            ["text"] = new
                            {
                                type = "string",
                                description = "The text to reverse"
                            }
                        },
                        Required = new List<string> { "text" }
                    }
                },
                parameters =>
                {
                    var text = parameters?.GetValueOrDefault("text")?.ToString() ?? "";
                    var reversed = new string(text.Reverse().ToArray());

                    return Task.FromResult(AcpToolResult.Success(new
                    {
                        original = text,
                        reversed,
                        length = text.Length
                    }));
                }
            );
        }
    }
}
