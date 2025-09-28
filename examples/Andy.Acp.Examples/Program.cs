using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.Transport;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Session;

namespace Andy.Acp.Examples
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            bool serverMode = false;
            bool clientMode = false;

            // Parse command line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--server":
                        serverMode = true;
                        break;
                    case "--client":
                        clientMode = true;
                        break;
                    case "--help":
                    case "-h":
                        ShowHelp();
                        return 0;
                    default:
                        Console.WriteLine($"Unknown argument: {args[i]}");
                        ShowHelp();
                        return 1;
                }
            }

            if (!serverMode && !clientMode)
            {
                Console.WriteLine("Andy ACP Examples");
                Console.WriteLine("Demonstrates ACP transport layer communication");
                ShowHelp();
                return 0;
            }

            if (serverMode && clientMode)
            {
                Console.WriteLine("Error: Cannot specify both --server and --client modes");
                return 1;
            }

            try
            {
                if (serverMode)
                {
                    await RunServerAsync();
                }
                else
                {
                    await RunClientAsync();
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: Andy.Acp.Examples [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --server    Run as server (receives messages)");
            Console.WriteLine("  --client    Run as client (sends messages)");
            Console.WriteLine("  --help, -h  Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  # Terminal 1 - Start server");
            Console.WriteLine("  Andy.Acp.Examples --server");
            Console.WriteLine();
            Console.WriteLine("  # Terminal 2 - Start client");
            Console.WriteLine("  Andy.Acp.Examples --client");
        }

        static async Task RunServerAsync()
        {
            // Configure services for server mode
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddConsole(options =>
                {
                    // Ensure all console output goes to stderr to avoid interfering with ACP protocol on stdout
                    options.LogToStandardErrorThreshold = LogLevel.Trace;
                })
                .SetMinimumLevel(LogLevel.Information);
            });
            services.AddSingleton<ITransport, StdioTransport>();
            services.AddSingleton<IJsonRpcHandler, JsonRpcHandler>();
            services.AddSingleton<ISessionManager, SessionManager>();

            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var transport = serviceProvider.GetRequiredService<ITransport>();
            var jsonRpcHandler = serviceProvider.GetRequiredService<IJsonRpcHandler>() as JsonRpcHandler;
            var sessionManager = serviceProvider.GetRequiredService<ISessionManager>();

            // Register example JSON-RPC methods with session management
            RegisterExampleMethods(jsonRpcHandler!, logger, sessionManager);

            // Start session manager
            await sessionManager.StartAsync();

            Console.Error.WriteLine("[SERVER] Andy ACP Examples - JSON-RPC Server Mode with Session Management");
            Console.Error.WriteLine("[SERVER] Listening for JSON-RPC messages on stdin...");
            Console.Error.WriteLine("[SERVER] Supported methods: " + string.Join(", ", jsonRpcHandler!.GetSupportedMethods()));
            Console.Error.WriteLine("[SERVER] Session management enabled with timeout: " + sessionManager.DefaultSessionTimeout);
            Console.Error.WriteLine("[SERVER] Press Ctrl+C to exit");
            Console.Error.WriteLine();

            var cancellationTokenSource = new CancellationTokenSource();
            AcpSession? currentSession = null;

            // Handle Ctrl+C in interactive mode
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.Error.WriteLine();
                Console.Error.WriteLine("[SERVER] Shutdown requested... (Ctrl+C pressed)");
                cancellationTokenSource.Cancel();
            };

            // Handle SIGTERM/SIGINT when running non-interactively
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Console.Error.WriteLine("[SERVER] Process exit signal received");
                cancellationTokenSource.Cancel();
            };

            // Additional signal handling for Unix systems
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var sigintReceived = false;
                PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
                {
                    if (!sigintReceived)
                    {
                        sigintReceived = true;
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("[SERVER] SIGINT received, shutting down gracefully...");
                        cancellationTokenSource.Cancel();
                    }
                });

                PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                {
                    Console.Error.WriteLine("[SERVER] SIGTERM received, shutting down...");
                    cancellationTokenSource.Cancel();
                });
            }

            try
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested && transport.IsConnected)
                {
                    try
                    {
                        logger.LogInformation("Waiting for incoming JSON-RPC message...");
                        var messageJson = await transport.ReadMessageAsync(cancellationTokenSource.Token);

                        logger.LogInformation("Received JSON-RPC message ({Length} chars): {Message}", messageJson.Length, messageJson);
                        Console.Error.WriteLine($"[SERVER] Received JSON-RPC message ({messageJson.Length} chars): {messageJson}");

                        // Parse and handle JSON-RPC message
                        JsonRpcMessage? jsonRpcMessage = null;
                        JsonRpcResponse? response = null;

                        try
                        {
                            jsonRpcMessage = JsonRpcSerializer.Deserialize(messageJson);

                            if (jsonRpcMessage is JsonRpcRequest request)
                            {
                                logger.LogInformation("Processing JSON-RPC request: {Method} (ID: {Id})", request.Method, request.Id);

                                // Handle session initialization
                                if (request.Method == "initialize" && currentSession == null)
                                {
                                    currentSession = sessionManager.CreateSession();
                                    Console.Error.WriteLine($"[SERVER] Created new session: {currentSession.SessionId}");
                                }

                                // Track pending request in current session
                                if (currentSession != null && !request.IsNotification)
                                {
                                    currentSession.AddPendingRequest(request);
                                    currentSession.MarkActive();
                                }

                                response = await jsonRpcHandler.HandleRequestAsync(request, cancellationTokenSource.Token);

                                // Complete pending request
                                if (currentSession != null && !request.IsNotification && request.Id != null)
                                {
                                    currentSession.CompletePendingRequest(request.Id);
                                }
                            }
                            else if (jsonRpcMessage is JsonRpcResponse responseMessage)
                            {
                                logger.LogInformation("Received JSON-RPC response (ID: {Id}), ignoring in server mode", responseMessage.Id);
                                Console.Error.WriteLine($"[SERVER] Received response message, ignoring (ID: {responseMessage.Id})");
                            }
                        }
                        catch (JsonRpcException ex)
                        {
                            logger.LogWarning("Invalid JSON-RPC message: {Error}", ex.Message);
                            Console.Error.WriteLine($"[SERVER] Invalid JSON-RPC message: {ex.Message}");

                            // Try to extract ID for error response
                            object? requestId = null;
                            try
                            {
                                using var doc = JsonDocument.Parse(messageJson);
                                if (doc.RootElement.TryGetProperty("id", out var idElement))
                                {
                                    requestId = idElement.ValueKind switch
                                    {
                                        JsonValueKind.String => idElement.GetString(),
                                        JsonValueKind.Number => idElement.GetInt32(),
                                        _ => null
                                    };
                                }
                            }
                            catch { /* Ignore parsing errors */ }

                            var errorCode = ex is JsonRpcParseException ? JsonRpcErrorCodes.ParseError : JsonRpcErrorCodes.InvalidRequest;
                            response = JsonRpcSerializer.CreateErrorResponse(requestId, JsonRpcErrorCodes.CreateError(errorCode, ex.Message));
                        }

                        // Send response if we have one
                        if (response != null)
                        {
                            var responseJson = JsonRpcSerializer.Serialize(response);
                            await transport.WriteMessageAsync(responseJson, cancellationTokenSource.Token);

                            logger.LogInformation("Sent JSON-RPC response ({Length} chars) for ID: {Id}", responseJson.Length, response.Id);
                            Console.Error.WriteLine($"[SERVER] Sent JSON-RPC response ({responseJson.Length} chars) for ID: {response.Id}");
                        }

                        Console.Error.WriteLine();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (EndOfStreamException)
                    {
                        Console.Error.WriteLine("[SERVER] Input stream closed");
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing message");
                        Console.Error.WriteLine($"[SERVER] Error processing message: {ex.Message}");

                        // If we keep getting errors, break out to avoid infinite loop
                        if (ex.InnerException is EndOfStreamException)
                        {
                            Console.Error.WriteLine("[SERVER] Input stream closed");
                            break;
                        }
                    }
                }
            }
            finally
            {
                // Stop session manager
                await sessionManager.StopAsync(cancellationTokenSource.Token);
                await transport.CloseAsync(cancellationTokenSource.Token);
                Console.Error.WriteLine($"[SERVER] Stopped (Sessions terminated: {(currentSession != null ? 1 : 0)})");
            }
        }

        static async Task RunClientAsync()
        {
            // Configure minimal logging for clean client output
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                // Only log errors to avoid cluttering the client presentation
                builder.AddConsole(options =>
                {
                    options.LogToStandardErrorThreshold = LogLevel.Error;
                })
                .SetMinimumLevel(LogLevel.Error);
            });
            services.AddSingleton<ITransport, StdioTransport>();

            var serviceProvider = services.BuildServiceProvider();
            var transport = serviceProvider.GetRequiredService<ITransport>();

            Console.Error.WriteLine("[CLIENT] Andy ACP Examples - Client Mode");
            Console.Error.WriteLine("[CLIENT] Sending ACP messages to stdout...");
            Console.Error.WriteLine("[CLIENT] Pipe this to a server: dotnet run -- --client | dotnet run -- --server");
            Console.Error.WriteLine();

            var cancellationTokenSource = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine();
                Console.WriteLine("[CLIENT] Stopping...");
                cancellationTokenSource.Cancel();
            };

            try
            {
                // Create a series of JSON-RPC test requests
                var testRequests = new JsonRpcRequest[]
                {
                    new() {
                        Method = "initialize",
                        Id = 1,
                        Params = new { clientInfo = new { name = "Andy.Acp.Examples", version = "1.0.0" } }
                    },
                    new() {
                        Method = "ping",
                        Id = 2,
                        Params = new { }
                    },
                    new() {
                        Method = "echo",
                        Id = 3,
                        Params = new { text = "Hello, JSON-RPC World!" }
                    },
                    new() {
                        Method = "test",
                        Id = 4,
                        Params = new { data = new[] { 1, 2, 3, 4, 5 }, message = "Test data" }
                    },
                    new() {
                        Method = "notify",
                        // No Id = notification
                        Params = new { notification = "This is a notification (no response expected)" }
                    },
                    new() {
                        Method = "error",
                        Id = 6,
                        Params = new { trigger = "test error" }
                    }
                };

                for (int i = 0; i < testRequests.Length && !cancellationTokenSource.Token.IsCancellationRequested; i++)
                {
                    var request = testRequests[i];
                    var requestJson = JsonRpcSerializer.Serialize(request);

                    var messageType = request.IsNotification ? "notification" : "request";
                    Console.Error.WriteLine($"[CLIENT] Sending JSON-RPC {messageType} {i + 1}/{testRequests.Length}: {request.Method} (ID: {request.Id})");
                    Console.Error.WriteLine($"[CLIENT] JSON: {requestJson.Substring(0, Math.Min(100, requestJson.Length))}...");

                    // Send the JSON-RPC message to stdout (which can be piped to server)
                    await transport.WriteMessageAsync(requestJson, cancellationTokenSource.Token);

                    // Small delay between messages
                    if (i < testRequests.Length - 1)
                    {
                        await Task.Delay(200, cancellationTokenSource.Token);
                    }
                }

                Console.Error.WriteLine("[CLIENT] All JSON-RPC messages sent successfully!");
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("[CLIENT] Operation cancelled");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CLIENT] Error: {ex.Message}");
            }
            finally
            {
                await transport.CloseAsync(cancellationTokenSource.Token);
                Console.Error.WriteLine("[CLIENT] Done");
            }
        }

        static void RegisterExampleMethods(JsonRpcHandler handler, ILogger logger, ISessionManager sessionManager)
        {
            // Initialize method - typical ACP initialization with session management
            handler.RegisterMethod("initialize", (parameters, ct) =>
            {
                logger.LogInformation("Initialize method called with session management");

                // Extract client capabilities from parameters if provided
                ClientCapabilities? clientCapabilities = null;
                try
                {
                    if (parameters is JsonElement jsonElement && jsonElement.TryGetProperty("clientInfo", out var clientInfoElement))
                    {
                        var clientInfo = new ClientInfo
                        {
                            Name = "Unknown Client",
                            Version = "Unknown Version"
                        };
                        if (clientInfoElement.TryGetProperty("name", out var nameElement))
                            clientInfo.Name = nameElement.GetString() ?? "Unknown Client";
                        if (clientInfoElement.TryGetProperty("version", out var versionElement))
                            clientInfo.Version = versionElement.GetString() ?? "Unknown Version";

                        clientCapabilities = new ClientCapabilities { ClientInfo = clientInfo };
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse client capabilities from initialize request");
                }

                var result = new
                {
                    protocolVersion = "1.0",
                    serverInfo = new
                    {
                        name = "Andy.Acp.Examples",
                        version = "1.0.0"
                    },
                    capabilities = new
                    {
                        tools = new[] { "echo", "ping", "test" },
                        resources = new[] { "file://" },
                        sessionManagement = true
                    },
                    sessionInfo = new
                    {
                        totalSessions = sessionManager.ActiveSessions.Count,
                        sessionTimeout = sessionManager.DefaultSessionTimeout.TotalMinutes + " minutes"
                    }
                };
                return Task.FromResult<object?>(result);
            });

            // Ping method - simple health check
            handler.RegisterMethod("ping", (parameters, ct) =>
            {
                logger.LogInformation("Ping method called");
                return Task.FromResult<object?>(new
                {
                    status = "pong",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                });
            });

            // Echo method - echoes back the input
            handler.RegisterMethod("echo", (parameters, ct) =>
            {
                logger.LogInformation("Echo method called with parameters: {Parameters}", parameters);
                var result = new
                {
                    echo = parameters,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };
                return Task.FromResult<object?>(result);
            });

            // Test method - demonstrates parameter handling
            handler.RegisterMethod("test", (parameters, ct) =>
            {
                logger.LogInformation("Test method called with parameters: {Parameters}", parameters);
                var result = new
                {
                    received = parameters,
                    processed = true,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    server = "Andy.Acp.Examples"
                };
                return Task.FromResult<object?>(result);
            });

            // Notification method - demonstrates notifications (no response)
            handler.RegisterMethod("notify", (parameters, ct) =>
            {
                logger.LogInformation("Notification received: {Parameters}", parameters);
                // Notifications don't return responses
                return Task.FromResult<object?>(null);
            });

            // Error method - demonstrates error handling
            handler.RegisterMethod("error", (parameters, ct) =>
            {
                logger.LogWarning("Error method called, throwing test exception");
                throw new InvalidOperationException("This is a test error to demonstrate error handling");
            });

            logger.LogInformation("Registered {Count} example JSON-RPC methods", handler.GetSupportedMethods().Length);
        }
    }
}