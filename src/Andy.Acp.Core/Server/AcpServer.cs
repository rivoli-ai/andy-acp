using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;
using Andy.Acp.Core.Client;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Session;
using Andy.Acp.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Server
{
    /// <summary>
    /// Unified ACP server that handles the complete Agent Client Protocol.
    /// This is the main entry point for implementing an ACP-compatible agent.
    /// </summary>
    public class AcpServer
    {
        private readonly IAgentProvider _agentProvider;
        private readonly ServerInfo _serverInfo;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly ILogger<AcpServer>? _logger;

        // In-flight inbound requests by canonical id, so $/cancel_request can cancel the
        // request it targets. Cancelled requests respond with -32800 (request cancelled).
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlightRequests = new();

        /// <summary>
        /// Create a new ACP server for the given agent. Filesystem and terminal operations
        /// are performed as agent → client requests via <see cref="Client.IAcpClient"/>
        /// (exposed to the agent through the response streamer), not as inbound server methods.
        /// </summary>
        /// <param name="agentProvider">Required: The agent that handles prompts and reasoning</param>
        /// <param name="serverInfo">Optional: Server identification information</param>
        /// <param name="loggerFactory">Optional: Logger factory for diagnostics</param>
        public AcpServer(
            IAgentProvider agentProvider,
            ServerInfo? serverInfo = null,
            ILoggerFactory? loggerFactory = null)
        {
            _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger<AcpServer>();

            _serverInfo = serverInfo ?? new ServerInfo
            {
                Name = "ACP Server",
                Version = "1.0.0",
                Description = "Agent Client Protocol Server"
            };
        }

        /// <summary>
        /// Run the ACP server in stdio mode.
        /// This method blocks until the server is stopped (via shutdown or cancellation).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop the server</param>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            var transport = new StdioTransport(_loggerFactory?.CreateLogger<StdioTransport>());
            try
            {
                await RunAsync(transport, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                transport.Dispose();
            }
        }

        /// <summary>
        /// Run the ACP server over the supplied transport. This overload exists so the
        /// full server stack can be driven over an in-memory duplex transport in tests.
        /// The caller owns the transport's lifetime.
        /// </summary>
        public async Task RunAsync(ITransport transport, CancellationToken cancellationToken = default)
        {
            if (transport == null)
                throw new ArgumentNullException(nameof(transport));

            _logger?.LogInformation("Starting ACP server: {Name} v{Version}", _serverInfo.Name, _serverInfo.Version);

            try
            {
                // Create JSON-RPC handler
                var jsonRpcHandler = new JsonRpcHandler(_loggerFactory?.CreateLogger<JsonRpcHandler>());

                // Create session manager
                var sessionManager = new SessionManager(_loggerFactory?.CreateLogger<SessionManager>());

                // Get agent capabilities and advertise them in the ACP shape. Optional
                // surface (session catalog, auth) is advertised from the optional
                // provider interfaces the agent implements.
                var agentCapabilities = _agentProvider.GetCapabilities();
                var authProvider = _agentProvider as IAuthenticationProvider;
                var acpAgentCapabilities = new AcpAgentCapabilities
                {
                    LoadSession = agentCapabilities.LoadSession,
                    PromptCapabilities = new AcpPromptCapabilities
                    {
                        Image = agentCapabilities.ImagePrompts,
                        Audio = agentCapabilities.AudioPrompts,
                        EmbeddedContext = agentCapabilities.EmbeddedContext
                    },
                    McpCapabilities = new AcpMcpCapabilities { Http = false, Sse = false },
                    SessionCapabilities = _agentProvider is ISessionCatalogProvider
                        ? new AcpSessionCapabilities
                        {
                            List = new CapabilityMarker(),
                            Delete = new CapabilityMarker(),
                            Resume = new CapabilityMarker(),
                            Close = new CapabilityMarker()
                        }
                        : null,
                    Auth = authProvider?.SupportsLogout == true
                        ? new AcpAgentAuthCapabilities { Logout = new CapabilityMarker() }
                        : null
                };

                // Shared connection state enforces initialize-before-session ordering and
                // carries the negotiated client capabilities.
                var connectionState = new AcpConnectionState();

                // Register the initialize handshake handler (plus authenticate/logout when
                // the agent implements IAuthenticationProvider).
                var protocolHandler = new AcpProtocolHandler(
                    connectionState,
                    _serverInfo,
                    acpAgentCapabilities,
                    _loggerFactory?.CreateLogger<AcpProtocolHandler>(),
                    authProvider);
                protocolHandler.RegisterMethods(jsonRpcHandler);

                // Agent → client request channel (fs/*, terminal/*, session/request_permission).
                // Client responses are routed back to this proxy by the read loop below.
                var acpClient = new AcpClient(transport, connectionState, _loggerFactory?.CreateLogger<AcpClient>());

                // Register the session handler (session/new, load, prompt, set_mode, cancel).
                var sessionHandler = new AcpSessionHandler(
                    _agentProvider,
                    jsonRpcHandler,
                    connectionState,
                    _loggerFactory?.CreateLogger<AcpSessionHandler>());
                sessionHandler.SetTransport(transport);  // Enable sending session/update notifications
                sessionHandler.SetClient(acpClient);     // Enable agent-to-client requests
                sessionHandler.RegisterMethods();

                // Filesystem and terminal are agent → client requests, so no inbound fs/*
                // or terminal/* handlers are registered here.

                // Start session manager
                await sessionManager.StartAsync();

                var supportedMethods = jsonRpcHandler.GetSupportedMethods();
                _logger?.LogInformation("ACP server initialized with {MethodCount} methods: {Methods}",
                    supportedMethods.Count(),
                    string.Join(", ", supportedMethods));

                // Main server loop. The transport read loop stays responsive while
                // request handlers execute so that control messages (notably
                // session/cancel) can be read and processed while a prompt is in flight.
                var inFlight = new ConcurrentDictionary<Task, byte>();
                try
                {
                    while (!cancellationToken.IsCancellationRequested && transport.IsConnected)
                    {
                        string messageJson;
                        try
                        {
                            messageJson = await transport.ReadMessageAsync(cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger?.LogInformation("Server read loop cancelled");
                            break;
                        }
                        catch (EndOfStreamException)
                        {
                            _logger?.LogInformation("Input stream closed");
                            break;
                        }
                        catch (Exception ex) when (ex.InnerException is EndOfStreamException)
                        {
                            _logger?.LogInformation("Input stream closed");
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error reading message; stopping read loop");
                            break;
                        }

                        if (string.IsNullOrEmpty(messageJson))
                        {
                            _logger?.LogDebug("Received empty message, connection may be closed");
                            break;
                        }

                        // Dispatch handling without blocking the read loop. Failures are
                        // observed in the continuation and never terminate the loop silently.
                        var handlerTask = Task.Run(
                            () => DispatchMessageAsync(messageJson, jsonRpcHandler, transport, acpClient, cancellationToken),
                            CancellationToken.None);

                        inFlight.TryAdd(handlerTask, 0);
                        _ = handlerTask.ContinueWith(t =>
                        {
                            inFlight.TryRemove(t, out _);
                            if (t.IsFaulted)
                                _logger?.LogError(t.Exception, "Unhandled error in message handler");
                        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                }
                finally
                {
                    // Allow in-flight handlers to flush their final updates/responses,
                    // then clean up. Cleanup runs on a non-cancelled path so resources are
                    // always released even when cancellation triggered the shutdown.
                    try
                    {
                        if (!inFlight.IsEmpty)
                            await Task.WhenAll(inFlight.Keys).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Timed out or errored waiting for in-flight handlers during shutdown");
                    }

                    // Fail any still-pending agent-to-client requests now that the
                    // connection is closing, so awaiting agent operations complete.
                    acpClient.FailAllPending(new AcpClientDisconnectedException("The ACP connection was closed"));

                    await sessionManager.StopAsync(CancellationToken.None);
                    await transport.CloseAsync(CancellationToken.None);
                }

                _logger?.LogInformation("ACP server stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fatal error in ACP server");
                throw;
            }
        }

        /// <summary>
        /// Deserializes a single inbound message, routes it through the JSON-RPC handler,
        /// and writes any response. Ordering of a prompt's session/update notifications
        /// relative to its final response is preserved because the streamer writes those
        /// updates while <c>HandleRequestAsync</c> is awaited, before the response is written.
        /// </summary>
        private async Task DispatchMessageAsync(
            string messageJson,
            JsonRpcHandler jsonRpcHandler,
            ITransport transport,
            AcpClient acpClient,
            CancellationToken cancellationToken)
        {
            JsonRpcResponse? response = null;

            try
            {
                var jsonRpcMessage = JsonRpcSerializer.Deserialize(messageJson);

                if (jsonRpcMessage is JsonRpcRequest request)
                {
                    _logger?.LogDebug("Processing {Kind}: {Method} (ID: {Id})",
                        request.IsNotification ? "notification" : "request", request.Method, request.Id);

                    if (request.Method == "$/cancel_request")
                    {
                        // ACP protocol-level cancellation: cancel the in-flight request with
                        // the given id. This is a notification; no response is sent.
                        HandleCancelRequest(request);
                        return;
                    }

                    if (!request.IsNotification)
                    {
                        // Track cancellable in-flight requests by canonical id.
                        var key = CanonicalId(request.Id);
                        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        _inFlightRequests[key] = requestCts;
                        try
                        {
                            response = await jsonRpcHandler.HandleRequestAsync(request, requestCts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _inFlightRequests.TryRemove(key, out _);
                        }
                    }
                    else
                    {
                        response = await jsonRpcHandler.HandleRequestAsync(request, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (jsonRpcMessage is JsonRpcResponse responseMessage)
                {
                    // Inbound responses correspond to agent-to-client requests; route them to
                    // the awaiting outbound operation.
                    acpClient.HandleResponse(responseMessage);
                }
            }
            catch (JsonRpcException ex)
            {
                _logger?.LogWarning(ex, "JSON-RPC error: {Message}", ex.Message);
                var errorCode = ex is JsonRpcParseException
                    ? JsonRpcErrorCodes.ParseError
                    : JsonRpcErrorCodes.InvalidRequest;
                response = JsonRpcSerializer.CreateErrorResponse(
                    requestId: null,
                    JsonRpcErrorCodes.CreateError(errorCode, ex.Message));
            }

            if (response != null)
            {
                try
                {
                    var responseJson = JsonRpcSerializer.Serialize(response);
                    await transport.WriteMessageAsync(responseJson, cancellationToken).ConfigureAwait(false);
                    _logger?.LogTrace("Sent response: {ResponseLength} bytes", responseJson.Length);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down; nothing to write.
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to write response");
                }
            }
        }

        /// <summary>
        /// Handles a <c>$/cancel_request</c> notification by cancelling the matching
        /// in-flight request, if any. Unknown or already-completed ids are ignored.
        /// </summary>
        private void HandleCancelRequest(JsonRpcRequest request)
        {
            string? key = null;
            if (request.Params is System.Text.Json.JsonElement el &&
                el.ValueKind == System.Text.Json.JsonValueKind.Object &&
                el.TryGetProperty("requestId", out var idEl))
            {
                key = idEl.GetRawText();
            }

            if (key == null)
            {
                _logger?.LogWarning("$/cancel_request without a requestId; ignoring");
                return;
            }

            if (_inFlightRequests.TryGetValue(key, out var cts))
            {
                _logger?.LogInformation("Cancelling in-flight request {RequestId}", key);
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
            else
            {
                _logger?.LogDebug("$/cancel_request for unknown/completed request {RequestId}", key);
            }
        }

        /// <summary>
        /// Canonicalizes a request id for in-flight tracking so that the id in a
        /// $/cancel_request matches regardless of representation. Uses raw JSON text
        /// (e.g. <c>5</c> vs <c>"5"</c> stay distinct, as JSON-RPC requires).
        /// </summary>
        private static string CanonicalId(object? id) => id switch
        {
            null => "null",
            System.Text.Json.JsonElement el => el.GetRawText(),
            string s => "\"" + s + "\"",
            _ => Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture) ?? "null"
        };
    }
}
