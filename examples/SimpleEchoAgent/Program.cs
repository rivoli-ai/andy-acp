using System;
using System.Threading.Tasks;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Server;
using Microsoft.Extensions.Logging;

namespace SimpleEchoAgent
{
    /// <summary>
    /// Simple Echo Agent Example
    ///
    /// This example demonstrates how to create a minimal ACP-compatible agent
    /// that echoes back user messages. Perfect for testing Zed integration.
    ///
    /// Usage:
    ///   dotnet run --project examples/SimpleEchoAgent
    ///
    /// Or build and run the executable:
    ///   dotnet build examples/SimpleEchoAgent -c Release
    ///   ./examples/SimpleEchoAgent/bin/Release/net8.0/SimpleEchoAgent --acp
    ///
    /// Configure in Zed settings.json:
    /// {
    ///   "agent": {
    ///     "provider": {
    ///       "name": "custom",
    ///       "command": "/path/to/SimpleEchoAgent",
    ///       "args": ["--acp"]
    ///     }
    ///   }
    /// }
    /// </summary>
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            try
            {
                // Parse command line arguments
                bool acpMode = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--acp")
                    {
                        acpMode = true;
                    }
                    else if (args[i] == "--help" || args[i] == "-h")
                    {
                        ShowHelp();
                        return 0;
                    }
                }

                if (!acpMode)
                {
                    Console.WriteLine("Simple Echo Agent - ACP Protocol Example");
                    Console.WriteLine();
                    ShowHelp();
                    return 0;
                }

                // Run ACP server mode
                await RunAcpServerAsync();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine("Usage: SimpleEchoAgent [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --acp       Run in ACP server mode (for Zed integration)");
            Console.WriteLine("  --help, -h  Show this help message");
            Console.WriteLine();
            Console.WriteLine("Example Zed Configuration:");
            Console.WriteLine("  Add to ~/.config/zed/settings.json:");
            Console.WriteLine("  {");
            Console.WriteLine("    \"agent\": {");
            Console.WriteLine("      \"provider\": {");
            Console.WriteLine("        \"name\": \"custom\",");
            Console.WriteLine("        \"command\": \"/path/to/SimpleEchoAgent\",");
            Console.WriteLine("        \"args\": [\"--acp\"]");
            Console.WriteLine("      }");
            Console.WriteLine("    }");
            Console.WriteLine("  }");
        }

        static async Task RunAcpServerAsync()
        {
            // Configure logging (all logs go to stderr to avoid interfering with ACP protocol on stdout)
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    })
                    .SetMinimumLevel(LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger<Program>();
            logger.LogInformation("Starting Simple Echo Agent in ACP mode...");

            // Create the echo agent provider
            var agentProvider = new SimpleEchoAgentProvider();

            // Create server info
            var serverInfo = new ServerInfo
            {
                Name = "Simple Echo Agent",
                Version = "1.0.0",
                Description = "A minimal ACP agent example that echoes back user messages"
            };

            // Create and run the ACP server
            var acpServer = new AcpServer(
                agentProvider: agentProvider,
                fileSystemProvider: null,  // Not needed for this simple example
                terminalProvider: null,    // Not needed for this simple example
                serverInfo: serverInfo,
                loggerFactory: loggerFactory
            );

            logger.LogInformation("ACP Server initialized. Waiting for client connection...");

            // Run the server (blocks until shutdown)
            await acpServer.RunAsync();

            logger.LogInformation("ACP Server stopped.");
        }
    }
}
