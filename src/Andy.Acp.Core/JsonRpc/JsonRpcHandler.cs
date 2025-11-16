using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.JsonRpc
{
    /// <summary>
    /// Default implementation of IJsonRpcHandler that supports method registration
    /// </summary>
    public class JsonRpcHandler : IJsonRpcHandler
    {
        private readonly ILogger<JsonRpcHandler>? _logger;
        private readonly ConcurrentDictionary<string, IJsonRpcMethodHandler> _methodHandlers = new();
        private readonly ConcurrentDictionary<string, JsonRpcMethodDelegate> _methodDelegates = new();

        /// <summary>
        /// Event raised when a request is received (before processing)
        /// </summary>
        public event EventHandler<JsonRpcRequestEventArgs>? RequestReceived;

        /// <summary>
        /// Event raised when a request has been processed
        /// </summary>
        public event EventHandler<JsonRpcRequestEventArgs>? RequestProcessed;

        /// <summary>
        /// Initializes a new instance of the JsonRpcHandler class
        /// </summary>
        /// <param name="logger">Optional logger instance</param>
        public JsonRpcHandler(ILogger<JsonRpcHandler>? logger = null)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            _logger?.LogInformation("Processing JSON-RPC request: {Method} (ID: {Id})", request.Method, request.Id);

            var eventArgs = new JsonRpcRequestEventArgs(request);

            try
            {
                // Raise RequestReceived event
                RequestReceived?.Invoke(this, eventArgs);

                // If event handler already set a response, use that
                if (eventArgs.Handled && eventArgs.Response != null)
                {
                    _logger?.LogDebug("Request handled by event handler: {Method}", request.Method);
                    return request.IsNotification ? null : eventArgs.Response;
                }

                // Try to find a registered handler
                JsonRpcResponse? response = null;

                if (_methodHandlers.TryGetValue(request.Method, out var methodHandler))
                {
                    response = await ExecuteMethodHandler(request, methodHandler, cancellationToken);
                }
                else if (_methodDelegates.TryGetValue(request.Method, out var methodDelegate))
                {
                    response = await ExecuteMethodDelegate(request, methodDelegate, cancellationToken);
                }
                else
                {
                    _logger?.LogWarning("Method not found: {Method}", request.Method);

                    if (!request.IsNotification)
                    {
                        response = JsonRpcSerializer.CreateErrorResponse(
                            request,
                            JsonRpcErrorCodes.MethodNotFound,
                            $"Method '{request.Method}' not found"
                        );
                    }
                }

                eventArgs.Response = response;
                eventArgs.Handled = true;

                // Raise RequestProcessed event
                RequestProcessed?.Invoke(this, eventArgs);

                _logger?.LogInformation("Completed JSON-RPC request: {Method} (Success: {Success})",
                    request.Method, response?.IsSuccess ?? true);

                return request.IsNotification ? null : response;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing JSON-RPC request: {Method}", request.Method);

                if (!request.IsNotification)
                {
                    var errorResponse = JsonRpcSerializer.CreateErrorResponse(
                        request,
                        JsonRpcErrorCodes.InternalError,
                        ex.Message,
                        _logger?.IsEnabled(LogLevel.Debug) == true ? ex.ToString() : null
                    );

                    eventArgs.Response = errorResponse;
                    RequestProcessed?.Invoke(this, eventArgs);

                    return errorResponse;
                }

                return null;
            }
        }

        /// <inheritdoc/>
        public bool SupportsMethod(string method)
        {
            if (string.IsNullOrWhiteSpace(method))
                return false;

            return _methodHandlers.ContainsKey(method) || _methodDelegates.ContainsKey(method);
        }

        /// <inheritdoc/>
        public string[] GetSupportedMethods()
        {
            var methods = new HashSet<string>();

            foreach (var key in _methodHandlers.Keys)
                methods.Add(key);

            foreach (var key in _methodDelegates.Keys)
                methods.Add(key);

            return methods.OrderBy(m => m).ToArray();
        }

        /// <summary>
        /// Registers a method handler
        /// </summary>
        /// <param name="handler">The method handler to register</param>
        /// <exception cref="ArgumentNullException">When handler is null</exception>
        /// <exception cref="ArgumentException">When method name is null or empty</exception>
        public void RegisterMethodHandler(IJsonRpcMethodHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (string.IsNullOrWhiteSpace(handler.Method))
                throw new ArgumentException("Method name cannot be null or empty", nameof(handler));

            _methodHandlers.AddOrUpdate(handler.Method, handler, (_, _) => handler);
            _logger?.LogDebug("Registered method handler: {Method}", handler.Method);
        }

        /// <summary>
        /// Registers a method delegate
        /// </summary>
        /// <param name="method">The method name</param>
        /// <param name="methodDelegate">The delegate to handle the method</param>
        /// <exception cref="ArgumentNullException">When methodDelegate is null</exception>
        /// <exception cref="ArgumentException">When method name is null or empty</exception>
        public void RegisterMethod(string method, JsonRpcMethodDelegate methodDelegate)
        {
            if (string.IsNullOrWhiteSpace(method))
                throw new ArgumentException("Method name cannot be null or empty", nameof(method));

            if (methodDelegate == null)
                throw new ArgumentNullException(nameof(methodDelegate));

            _methodDelegates.AddOrUpdate(method, methodDelegate, (_, _) => methodDelegate);
            _logger?.LogDebug("Registered method delegate: {Method}", method);
        }

        /// <summary>
        /// Unregisters a method handler or delegate
        /// </summary>
        /// <param name="method">The method name to unregister</param>
        /// <returns>True if a handler was removed, false otherwise</returns>
        public bool UnregisterMethod(string method)
        {
            if (string.IsNullOrWhiteSpace(method))
                return false;

            bool removed = _methodHandlers.TryRemove(method, out _);
            removed |= _methodDelegates.TryRemove(method, out _);

            if (removed)
                _logger?.LogDebug("Unregistered method: {Method}", method);

            return removed;
        }

        /// <summary>
        /// Clears all registered method handlers and delegates
        /// </summary>
        public void Clear()
        {
            _methodHandlers.Clear();
            _methodDelegates.Clear();
            _logger?.LogDebug("Cleared all method handlers");
        }

        private async Task<JsonRpcResponse?> ExecuteMethodHandler(JsonRpcRequest request, IJsonRpcMethodHandler handler, CancellationToken cancellationToken)
        {
            try
            {
                var result = await handler.ExecuteAsync(request.Params, cancellationToken);
                return JsonRpcSerializer.CreateSuccessResponse(request, result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Method execution cancelled: {Method}", request.Method);
                return JsonRpcSerializer.CreateErrorResponse(request, JsonRpcErrorCodes.Cancelled);
            }
            catch (JsonRpcProtocolException ex)
            {
                _logger?.LogWarning(ex, "Protocol error in method: {Method}", request.Method);
                return JsonRpcSerializer.CreateErrorResponse(request, ex.ErrorCode, ex.Message, ex.Data);
            }
            catch (ArgumentException ex)
            {
                _logger?.LogWarning(ex, "Invalid parameters for method: {Method}", request.Method);
                return JsonRpcSerializer.CreateErrorResponse(request, JsonRpcErrorCodes.InvalidParams, ex.Message);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Method execution failed: {Method}", request.Method);
                return JsonRpcSerializer.CreateErrorResponse(request, JsonRpcErrorCodes.InternalError, ex.Message);
            }
        }

        private async Task<JsonRpcResponse?> ExecuteMethodDelegate(JsonRpcRequest request, JsonRpcMethodDelegate methodDelegate, CancellationToken cancellationToken)
        {
            try
            {
                var result = await methodDelegate(request.Params, cancellationToken);
                return JsonRpcSerializer.CreateSuccessResponse(request, result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Method execution cancelled: {Method}", request.Method);
                return JsonRpcSerializer.CreateErrorResponse(request, JsonRpcErrorCodes.Cancelled);
            }
            catch (JsonRpcProtocolException ex)
            {
                _logger?.LogWarning(ex, "Protocol error in method: {Method}", request.Method);
                return JsonRpcSerializer.CreateErrorResponse(request, ex.ErrorCode, ex.Message, ex.Data);
            }
            catch (ArgumentException ex)
            {
                _logger?.LogWarning(ex, "Invalid parameters for method: {Method}", request.Method);
                return JsonRpcSerializer.CreateErrorResponse(request, JsonRpcErrorCodes.InvalidParams, ex.Message);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Method execution failed: {Method}", request.Method);
                return JsonRpcSerializer.CreateErrorResponse(request, JsonRpcErrorCodes.InternalError, ex.Message);
            }
        }
    }
}