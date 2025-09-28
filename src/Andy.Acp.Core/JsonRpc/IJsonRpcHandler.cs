using System;
using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.JsonRpc
{
    /// <summary>
    /// Interface for handling JSON-RPC 2.0 messages
    /// </summary>
    public interface IJsonRpcHandler
    {
        /// <summary>
        /// Processes a JSON-RPC request and returns a response (if the request expects one)
        /// </summary>
        /// <param name="request">The JSON-RPC request to process</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A response if the request expects one, null for notifications</returns>
        Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the handler supports the specified method
        /// </summary>
        /// <param name="method">The method name to check</param>
        /// <returns>True if the method is supported, false otherwise</returns>
        bool SupportsMethod(string method);

        /// <summary>
        /// Gets the list of supported methods
        /// </summary>
        /// <returns>Array of supported method names</returns>
        string[] GetSupportedMethods();
    }

    /// <summary>
    /// Interface for method-specific JSON-RPC handlers
    /// </summary>
    public interface IJsonRpcMethodHandler
    {
        /// <summary>
        /// The method name this handler supports
        /// </summary>
        string Method { get; }

        /// <summary>
        /// Executes the method with the given parameters
        /// </summary>
        /// <param name="parameters">Method parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The method result</returns>
        Task<object?> ExecuteAsync(object? parameters, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Delegate for handling JSON-RPC method calls
    /// </summary>
    /// <param name="parameters">Method parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The method result</returns>
    public delegate Task<object?> JsonRpcMethodDelegate(object? parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event args for JSON-RPC request events
    /// </summary>
    public class JsonRpcRequestEventArgs : EventArgs
    {
        public JsonRpcRequestEventArgs(JsonRpcRequest request)
        {
            Request = request;
        }

        /// <summary>
        /// The JSON-RPC request
        /// </summary>
        public JsonRpcRequest Request { get; }

        /// <summary>
        /// The response to send (can be set by event handlers)
        /// </summary>
        public JsonRpcResponse? Response { get; set; }

        /// <summary>
        /// Whether the request has been handled
        /// </summary>
        public bool Handled { get; set; }
    }
}