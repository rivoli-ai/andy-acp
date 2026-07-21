using System.Text.Json.Serialization;

namespace Andy.Acp.Core.JsonRpc
{
    /// <summary>
    /// Base class for all JSON-RPC 2.0 messages
    /// </summary>
    public abstract class JsonRpcMessage
    {
        /// <summary>
        /// JSON-RPC protocol version. Must be "2.0"
        /// </summary>
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 request message
    /// </summary>
    public class JsonRpcRequest : JsonRpcMessage
    {
        /// <summary>
        /// The name of the method to be invoked
        /// </summary>
        [JsonPropertyName("method")]
        public required string Method { get; set; }

        /// <summary>
        /// Parameters for the method (can be object or array)
        /// </summary>
        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Params { get; set; }

        /// <summary>
        /// Request identifier. Per JSON-RPC 2.0 an absent id member denotes a
        /// notification, while an explicitly present id (including <c>null</c>)
        /// denotes a request that expects a response.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Id { get; set; }

        private bool? _hasId;

        /// <summary>
        /// Whether an id member was present on the wire. Defaults to
        /// <c>Id != null</c> for locally constructed messages, and is set
        /// explicitly by the deserializer so that an explicit <c>id: null</c>
        /// is distinguished from an omitted id (a notification).
        /// </summary>
        [JsonIgnore]
        public bool HasId
        {
            get => _hasId ?? (Id != null);
            set => _hasId = value;
        }

        /// <summary>
        /// Gets a value indicating whether this is a notification (no response expected).
        /// A notification is a message with no id member at all.
        /// </summary>
        [JsonIgnore]
        public bool IsNotification => !HasId;
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 response message.
    /// Serialization is handled by <see cref="JsonRpcResponseConverter"/> so that a
    /// successful response always emits a <c>result</c> member (even when the value is
    /// <c>null</c>), an error response emits only an <c>error</c> member, and the two
    /// are mutually exclusive as required by JSON-RPC 2.0.
    /// </summary>
    [JsonConverter(typeof(JsonRpcResponseConverter))]
    public class JsonRpcResponse : JsonRpcMessage
    {
        /// <summary>
        /// Request identifier from the corresponding request
        /// </summary>
        [JsonPropertyName("id")]
        public required object? Id { get; set; }

        /// <summary>
        /// Result of the method execution (present on success)
        /// </summary>
        [JsonPropertyName("result")]
        public object? Result { get; set; }

        /// <summary>
        /// Error information (present on failure)
        /// </summary>
        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonRpcError? Error { get; set; }

        /// <summary>
        /// Gets a value indicating whether this response represents an error
        /// </summary>
        [JsonIgnore]
        public bool IsError => Error != null;

        /// <summary>
        /// Gets a value indicating whether this response represents success
        /// </summary>
        [JsonIgnore]
        public bool IsSuccess => Error == null;
    }

    /// <summary>
    /// Represents a JSON-RPC 2.0 error object
    /// </summary>
    public class JsonRpcError
    {
        /// <summary>
        /// Error code indicating the type of error
        /// </summary>
        [JsonPropertyName("code")]
        public required int Code { get; set; }

        /// <summary>
        /// Short description of the error
        /// </summary>
        [JsonPropertyName("message")]
        public required string Message { get; set; }

        /// <summary>
        /// Additional error information
        /// </summary>
        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Data { get; set; }
    }
}