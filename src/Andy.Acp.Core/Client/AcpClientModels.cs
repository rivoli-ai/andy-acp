using System;
using System.Text.Json.Serialization;
using Andy.Acp.Core.JsonRpc;

namespace Andy.Acp.Core.Client
{
    /// <summary>Result of a terminal/output request.</summary>
    public class TerminalOutput
    {
        [JsonPropertyName("output")]
        public string Output { get; set; } = string.Empty;

        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }

        [JsonPropertyName("exitStatus")]
        public TerminalExit? ExitStatus { get; set; }
    }

    /// <summary>Exit status of a terminal command (exitCode and/or signal).</summary>
    public class TerminalExit
    {
        [JsonPropertyName("exitCode")]
        public int? ExitCode { get; set; }

        [JsonPropertyName("signal")]
        public string? Signal { get; set; }
    }

    /// <summary>A permission option presented to the user (ACP <c>PermissionOption</c>).</summary>
    public class PermissionOption
    {
        [JsonPropertyName("optionId")]
        public string OptionId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>allow_once, allow_always, reject_once, or reject_always.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "allow_once";
    }

    /// <summary>
    /// The tool call a permission request is about (ACP <c>ToolCallUpdate</c>; only
    /// <see cref="ToolCallId"/> is required).
    /// </summary>
    public class PermissionToolCall
    {
        [JsonPropertyName("toolCallId")]
        public string ToolCallId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    /// <summary>The outcome of a permission request.</summary>
    public class PermissionOutcome
    {
        /// <summary>True when the user cancelled instead of selecting an option.</summary>
        public bool Cancelled { get; set; }

        /// <summary>The selected option id (null when cancelled).</summary>
        public string? OptionId { get; set; }

        public static PermissionOutcome Selected(string optionId) => new() { OptionId = optionId };
        public static PermissionOutcome CancelledOutcome() => new() { Cancelled = true };
    }

    /// <summary>Thrown when the client returns a JSON-RPC error for an agent-to-client request.</summary>
    public class AcpRequestException : Exception
    {
        public int Code { get; }
        public object? ErrorData { get; }

        public AcpRequestException(JsonRpcError error)
            : base(error?.Message ?? "ACP request failed")
        {
            Code = error?.Code ?? JsonRpcErrorCodes.InternalError;
            ErrorData = error?.Data;
        }
    }

    /// <summary>Thrown when the connection closes with an agent-to-client request still pending.</summary>
    public class AcpClientDisconnectedException : Exception
    {
        public AcpClientDisconnectedException(string message) : base(message) { }
    }

    /// <summary>Thrown when the agent tries to use a client capability the client did not advertise.</summary>
    public class AcpCapabilityNotSupportedException : Exception
    {
        public AcpCapabilityNotSupportedException(string message) : base(message) { }
    }
}
