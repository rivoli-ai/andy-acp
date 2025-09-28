using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.Transport;

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
            // Configure minimal logging for server mode
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

            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var transport = serviceProvider.GetRequiredService<ITransport>();

            Console.Error.WriteLine("[SERVER] Andy ACP Examples - Server Mode");
            Console.Error.WriteLine("[SERVER] Listening for messages on stdin...");
            Console.Error.WriteLine("[SERVER] Send messages in ACP format, or press Ctrl+C to exit");
            Console.Error.WriteLine();

            var cancellationTokenSource = new CancellationTokenSource();

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
                        logger.LogInformation("Waiting for incoming message...");
                        var message = await transport.ReadMessageAsync(cancellationTokenSource.Token);

                        logger.LogInformation("Received message ({Length} chars): {Message}", message.Length, message);
                        Console.Error.WriteLine($"[SERVER] Received message ({message.Length} chars): {message}");

                        // Echo response
                        var response = $"{{\"id\":\"example-{DateTime.Now.Ticks}\",\"result\":{{\"echo\":\"{message}\",\"timestamp\":\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}\"}},\"jsonrpc\":\"2.0\"}}";

                        await transport.WriteMessageAsync(response, cancellationTokenSource.Token);

                        logger.LogInformation("Sent response ({Length} chars)", response.Length);
                        Console.Error.WriteLine($"[SERVER] Sent response ({response.Length} chars)");
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
                await transport.CloseAsync(cancellationTokenSource.Token);
                Console.Error.WriteLine("[SERVER] Stopped");
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
                // Send a series of test messages
                var testMessages = new[]
                {
                    "{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"Andy.Acp.Examples\",\"version\":\"1.0.0\"}}}",
                    "{\"method\":\"ping\",\"id\":2,\"params\":{}}",
                    "{\"method\":\"echo\",\"id\":3,\"params\":{\"text\":\"Hello, ACP World!\"}}",
                    "{\"method\":\"test\",\"id\":4,\"params\":{\"data\":[1,2,3,4,5]}}"
                };

                for (int i = 0; i < testMessages.Length && !cancellationTokenSource.Token.IsCancellationRequested; i++)
                {
                    var message = testMessages[i];

                    Console.Error.WriteLine($"[CLIENT] Sending message {i + 1}/{testMessages.Length}: {message.Substring(0, Math.Min(50, message.Length))}...");

                    // Send the ACP message to stdout (which can be piped to server)
                    await transport.WriteMessageAsync(message, cancellationTokenSource.Token);

                    // Small delay between messages
                    if (i < testMessages.Length - 1)
                    {
                        await Task.Delay(100, cancellationTokenSource.Token);
                    }
                }

                Console.Error.WriteLine("[CLIENT] All messages sent successfully!");
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
    }
}