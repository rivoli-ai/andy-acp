using System.Collections.Generic;

namespace Andy.Acp.Core.Tools
{
    /// <summary>
    /// Represents a tool definition in ACP format (MCP-compatible).
    /// Minimal model aligned with ACP protocol specification.
    /// </summary>
    public class AcpToolDefinition
    {
        /// <summary>
        /// Gets or sets the unique name of the tool.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Gets or sets the description of what this tool does.
        /// </summary>
        public required string Description { get; set; }

        /// <summary>
        /// Gets or sets the input schema (JSON Schema format).
        /// </summary>
        public required AcpInputSchema InputSchema { get; set; }
    }

    /// <summary>
    /// Represents the JSON Schema for tool input parameters.
    /// Follows JSON Schema draft-07 specification.
    /// </summary>
    public class AcpInputSchema
    {
        /// <summary>
        /// Gets or sets the schema type (typically "object").
        /// </summary>
        public string Type { get; set; } = "object";

        /// <summary>
        /// Gets or sets the properties schema.
        /// Key is parameter name, value is JSON Schema for that parameter.
        /// </summary>
        public Dictionary<string, object> Properties { get; set; } = new();

        /// <summary>
        /// Gets or sets the list of required parameter names.
        /// </summary>
        public List<string>? Required { get; set; }

        /// <summary>
        /// Gets or sets additional properties allowed.
        /// Default is false for strict validation.
        /// </summary>
        public bool AdditionalProperties { get; set; } = false;
    }

    /// <summary>
    /// Represents the result of a tool execution.
    /// Minimal model aligned with ACP protocol specification.
    /// </summary>
    public class AcpToolResult
    {
        /// <summary>
        /// Gets or sets the result data from tool execution.
        /// Null if execution failed.
        /// </summary>
        public object? Result { get; set; }

        /// <summary>
        /// Gets or sets whether this is an error result.
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Gets or sets the error message if IsError is true.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Creates a successful tool result.
        /// </summary>
        /// <param name="result">The result data.</param>
        /// <returns>A successful AcpToolResult.</returns>
        public static AcpToolResult Success(object? result = null)
            => new() { Result = result, IsError = false };

        /// <summary>
        /// Creates a failed tool result.
        /// </summary>
        /// <param name="error">The error message.</param>
        /// <returns>A failed AcpToolResult.</returns>
        public static AcpToolResult Failure(string error)
            => new() { IsError = true, Error = error };
    }
}
