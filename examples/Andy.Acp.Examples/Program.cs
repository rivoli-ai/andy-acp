using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.Transport;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Session;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Tools;

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
                // Create a series of JSON-RPC test requests demonstrating complete ACP handshake
                var testRequests = new JsonRpcRequest[]
                {
                    // Step 1: Initialize the ACP session
                    new() {
                        Method = "initialize",
                        Id = 1,
                        Params = new {
                            ProtocolVersion = "1.0",
                            ClientInfo = new {
                                Name = "Andy.Acp.Examples.Client",
                                Version = "1.0.0",
                                Description = "Example ACP client"
                            },
                            Capabilities = new {
                                SupportedTools = new[] { "echo", "calculator" }
                            }
                        }
                    },
                    // Step 2: Send 'initialized' notification to confirm session is ready
                    new() {
                        Method = "initialized",
                        Params = new { }
                    },
                    // Step 3: List available tools
                    new() {
                        Method = "tools/list",
                        Id = 2,
                        Params = new { }
                    },
                    // Step 4: Call echo tool
                    new() {
                        Method = "tools/call",
                        Id = 3,
                        Params = new {
                            Name = "echo",
                            Parameters = new {
                                text = "Hello from ACP tools!"
                            }
                        }
                    },
                    // Step 5: Call calculator tool
                    new() {
                        Method = "tools/call",
                        Id = 4,
                        Params = new {
                            Name = "calculator",
                            Parameters = new {
                                operation = "add",
                                a = 15,
                                b = 27
                            }
                        }
                    },
                    // Step 6: Call get_time tool
                    new() {
                        Method = "tools/call",
                        Id = 5,
                        Params = new {
                            Name = "get_time",
                            Parameters = new {
                                timezone = "UTC"
                            }
                        }
                    },
                    // Step 7: Shutdown the session gracefully
                    new() {
                        Method = "shutdown",
                        Id = 6,
                        Params = new { Reason = "Client demonstration complete" }
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
            // Create tool provider with example tools
            var toolProvider = new SimpleToolProvider();
            var toolsList = toolProvider.ListToolsAsync().Result;
            var toolNames = toolsList.Select(t => t.Name).ToArray();

            logger.LogInformation("Created tool provider with {Count} tools: {Tools}",
                toolNames.Length, string.Join(", ", toolNames));

            // Register ACP protocol methods (initialize, initialized, shutdown)
            var serverInfo = new ServerInfo
            {
                Name = "Andy.Acp.Examples",
                Version = "1.0.0",
                Description = "Example ACP server demonstrating protocol implementation with tools"
            };

            var serverCapabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability
                {
                    Supported = true,
                    Available = toolNames,
                    ListSupported = true,
                    ExecutionSupported = true
                },
                Resources = new ResourcesCapability
                {
                    Supported = true,
                    SupportedSchemes = new[] { "file://" }
                },
                Prompts = new PromptsCapability
                {
                    Supported = false
                },
                Logging = new LoggingCapability
                {
                    Supported = true,
                    SupportedLevels = new[] { "debug", "info", "warning", "error" }
                }
            };

            var protocolHandler = new AcpProtocolHandler(sessionManager, serverInfo, serverCapabilities, logger as ILogger<AcpProtocolHandler>);
            protocolHandler.RegisterMethods(handler);

            // Register tools handler
            var toolsHandler = new AcpToolsHandler(toolProvider, logger as ILogger<AcpToolsHandler>);
            toolsHandler.RegisterMethods(handler);

            logger.LogInformation("Registered ACP protocol methods: initialize, initialized, shutdown, tools/list, tools/call");
            logger.LogInformation("Available tools: {Tools}", string.Join(", ", toolNames));
        }
    }
}