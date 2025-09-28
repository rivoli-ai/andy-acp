using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andy.Acp.Core.JsonRpc
{
    /// <summary>
    /// Provides serialization and deserialization for JSON-RPC 2.0 messages
    /// </summary>
    public static class JsonRpcSerializer
    {
        /// <summary>
        /// JSON serializer options configured for JSON-RPC 2.0
        /// </summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Serializes a JSON-RPC message to JSON string
        /// </summary>
        /// <param name="message">The message to serialize</param>
        /// <returns>JSON string representation</returns>
        /// <exception cref="ArgumentNullException">When message is null</exception>
        /// <exception cref="JsonException">When serialization fails</exception>
        public static string Serialize(JsonRpcMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            try
            {
                return JsonSerializer.Serialize(message, message.GetType(), Options);
            }
            catch (Exception ex) when (!(ex is JsonException))
            {
                throw new JsonException($"Failed to serialize JSON-RPC message: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deserializes a JSON string to determine message type and parse accordingly
        /// </summary>
        /// <param name="json">JSON string to deserialize</param>
        /// <returns>Parsed JsonRpcMessage (Request or Response)</returns>
        /// <exception cref="ArgumentNullException">When json is null or empty</exception>
        /// <exception cref="JsonRpcParseException">When JSON parsing fails</exception>
        /// <exception cref="JsonRpcInvalidRequestException">When the request format is invalid</exception>
        public static JsonRpcMessage Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentNullException(nameof(json));

            try
            {
                // First, parse as a generic JsonElement to determine message type
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                // Validate jsonrpc version
                if (root.TryGetProperty("jsonrpc", out var jsonRpcElement))
                {
                    var version = jsonRpcElement.GetString();
                    if (version != "2.0")
                    {
                        throw new JsonRpcInvalidRequestException("Invalid jsonrpc version. Must be '2.0'");
                    }
                }
                else
                {
                    throw new JsonRpcInvalidRequestException("Missing 'jsonrpc' property");
                }

                // Check if it's a response (has 'result' or 'error' and 'id')
                bool hasResult = root.TryGetProperty("result", out _);
                bool hasError = root.TryGetProperty("error", out _);
                bool hasId = root.TryGetProperty("id", out _);
                bool hasMethod = root.TryGetProperty("method", out _);

                if ((hasResult || hasError) && hasId)
                {
                    // This is a response
                    var response = JsonSerializer.Deserialize<JsonRpcResponse>(json, Options);
                    if (response == null)
                        throw new JsonRpcInvalidRequestException("Failed to parse JSON-RPC response");

                    // Validate response structure
                    if (hasResult && hasError)
                        throw new JsonRpcInvalidRequestException("Response cannot have both 'result' and 'error'");
                    if (!hasResult && !hasError)
                        throw new JsonRpcInvalidRequestException("Response must have either 'result' or 'error'");

                    return response;
                }
                else if (hasMethod)
                {
                    // This is a request
                    var request = JsonSerializer.Deserialize<JsonRpcRequest>(json, Options);
                    if (request == null)
                        throw new JsonRpcInvalidRequestException("Failed to parse JSON-RPC request");

                    // Validate method name
                    if (string.IsNullOrWhiteSpace(request.Method))
                        throw new JsonRpcInvalidRequestException("Method name cannot be null or empty");

                    // Method names starting with "rpc." are reserved
                    if (request.Method.StartsWith("rpc.", StringComparison.OrdinalIgnoreCase))
                        throw new JsonRpcInvalidRequestException("Method names starting with 'rpc.' are reserved");

                    return request;
                }
                else
                {
                    throw new JsonRpcInvalidRequestException("Invalid JSON-RPC message structure");
                }
            }
            catch (JsonException ex)
            {
                throw new JsonRpcParseException($"Invalid JSON: {ex.Message}", ex);
            }
            catch (JsonRpcException)
            {
                // Re-throw our custom exceptions
                throw;
            }
            catch (Exception ex)
            {
                throw new JsonRpcInvalidRequestException($"Failed to parse JSON-RPC message: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Attempts to deserialize a JSON string, returning null if parsing fails
        /// </summary>
        /// <param name="json">JSON string to deserialize</param>
        /// <param name="message">The parsed message, or null if parsing failed</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryDeserialize(string json, out JsonRpcMessage? message)
        {
            message = null;
            try
            {
                message = Deserialize(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a success response for the given request
        /// </summary>
        /// <param name="request">The request to respond to</param>
        /// <param name="result">The result value</param>
        /// <returns>A JsonRpcResponse with the result</returns>
        public static JsonRpcResponse CreateSuccessResponse(JsonRpcRequest request, object? result = null)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = result
            };
        }

        /// <summary>
        /// Creates an error response for the given request
        /// </summary>
        /// <param name="request">The request to respond to</param>
        /// <param name="error">The error information</param>
        /// <returns>A JsonRpcResponse with the error</returns>
        public static JsonRpcResponse CreateErrorResponse(JsonRpcRequest request, JsonRpcError error)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = error
            };
        }

        /// <summary>
        /// Creates an error response for the given request ID
        /// </summary>
        /// <param name="requestId">The request ID to respond to</param>
        /// <param name="error">The error information</param>
        /// <returns>A JsonRpcResponse with the error</returns>
        public static JsonRpcResponse CreateErrorResponse(object? requestId, JsonRpcError error)
        {
            return new JsonRpcResponse
            {
                Id = requestId,
                Error = error
            };
        }

        /// <summary>
        /// Creates an error response with the specified error code
        /// </summary>
        /// <param name="request">The request to respond to</param>
        /// <param name="errorCode">The error code</param>
        /// <param name="data">Additional error data</param>
        /// <returns>A JsonRpcResponse with the error</returns>
        public static JsonRpcResponse CreateErrorResponse(JsonRpcRequest request, int errorCode, object? data = null)
        {
            return CreateErrorResponse(request, JsonRpcErrorCodes.CreateError(errorCode, data));
        }

        /// <summary>
        /// Creates an error response with the specified error code and custom message
        /// </summary>
        /// <param name="request">The request to respond to</param>
        /// <param name="errorCode">The error code</param>
        /// <param name="message">Custom error message</param>
        /// <param name="data">Additional error data</param>
        /// <returns>A JsonRpcResponse with the error</returns>
        public static JsonRpcResponse CreateErrorResponse(JsonRpcRequest request, int errorCode, string message, object? data = null)
        {
            return CreateErrorResponse(request, JsonRpcErrorCodes.CreateError(errorCode, message, data));
        }
    }

    /// <summary>
    /// Base exception for JSON-RPC related errors
    /// </summary>
    public abstract class JsonRpcException : Exception
    {
        protected JsonRpcException(string message) : base(message) { }
        protected JsonRpcException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception thrown when JSON parsing fails
    /// </summary>
    public class JsonRpcParseException : JsonRpcException
    {
        public JsonRpcParseException(string message) : base(message) { }
        public JsonRpcParseException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception thrown when request structure is invalid
    /// </summary>
    public class JsonRpcInvalidRequestException : JsonRpcException
    {
        public JsonRpcInvalidRequestException(string message) : base(message) { }
        public JsonRpcInvalidRequestException(string message, Exception innerException) : base(message, innerException) { }
    }
}