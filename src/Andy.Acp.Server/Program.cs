using System;
using System.CommandLine;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Andy.Acp.Core.Transport;

namespace Andy.Acp.Server
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            bool acpServerMode = false;
            bool verbose = false;
            string? logFile = null;

            // Parse command line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--acp-server":
                        acpServerMode = true;
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--log-file":
                        if (i + 1 < args.Length)
                        {
                            logFile = args[++i];
                        }
                        else
                        {
                            Console.Error.WriteLine("Error: --log-file requires a file path");
                            return 1;
                        }
                        break;
                    case "--help":
                    case "-h":
                        ShowHelp();
                        return 0;
                    default:
                        Console.Error.WriteLine($"Unknown argument: {args[i]}");
                        ShowHelp();
                        return 1;
                }
            }

            if (!acpServerMode)
            {
                Console.WriteLine("Andy ACP Server");
                Console.WriteLine("Use --acp-server to start in ACP mode");
                ShowHelp();
                return 0;
            }

            try
            {
                await RunAcpServerAsync(verbose, logFile);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: Andy.Acp.Server [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --acp-server         Start in ACP server mode");
            Console.WriteLine("  --verbose           Enable verbose logging");
            Console.WriteLine("  --log-file <path>   Log to file instead of stderr");
            Console.WriteLine("  --help, -h          Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Andy.Acp.Server --acp-server");
            Console.WriteLine("  Andy.Acp.Server --acp-server --verbose");
            Console.WriteLine("  Andy.Acp.Server --acp-server --log-file /path/to/log.txt");
        }

        static async Task RunAcpServerAsync(bool verbose, string? logFile)
        {
            // Set up logging
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                if (verbose)
                {
                    builder.SetMinimumLevel(LogLevel.Debug);
                }
                else
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                }

                // In ACP mode, console logging goes to stderr
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });
            });

            var logger = loggerFactory.CreateLogger<Program>();
            var transportLogger = loggerFactory.CreateLogger<StdioTransport>();

            // Redirect console output to stderr when in ACP mode
            Console.SetOut(Console.Error);

            using var transport = new StdioTransport(transportLogger);

            logger.LogInformation("Andy ACP Server starting...");
            logger.LogInformation("Listening for ACP messages on stdin");

            var cancellationTokenSource = new CancellationTokenSource();

            // Handle Ctrl+C gracefully
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                logger.LogInformation("Shutdown requested...");
                cancellationTokenSource.Cancel();
            };

            try
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested && transport.IsConnected)
                {
                    try
                    {
                        logger.LogDebug("Waiting for incoming message...");

                        var message = await transport.ReadMessageAsync(cancellationTokenSource.Token);

                        logger.LogInformation("Received message: {MessageLength} characters", message.Length);
                        logger.LogDebug("Message content: {Message}", message);

                        // Echo the message back for now (placeholder for actual ACP message handling)
                        var response = $"{{\"id\":null,\"result\":{{\"echo\":\"{message}\"}},\"jsonrpc\":\"2.0\"}}";

                        await transport.WriteMessageAsync(response, cancellationTokenSource.Token);

                        logger.LogDebug("Response sent");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing message");

                        // Send error response
                        var errorResponse = $"{{\"id\":null,\"error\":{{\"code\":-32603,\"message\":\"Internal error: {ex.Message}\"}},\"jsonrpc\":\"2.0\"}}";

                        try
                        {
                            await transport.WriteMessageAsync(errorResponse, cancellationTokenSource.Token);
                        }
                        catch (Exception writeEx)
                        {
                            logger.LogError(writeEx, "Failed to send error response");
                        }
                    }
                }
            }
            finally
            {
                logger.LogInformation("Closing transport...");
                await transport.CloseAsync();
                logger.LogInformation("Andy ACP Server stopped");
            }
        }
    }
}