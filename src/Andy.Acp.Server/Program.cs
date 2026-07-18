using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Protocol;
using Andy.Acp.Core.Server;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Server
{
    /// <summary>
    /// Entry point for the Andy ACP server executable. It hosts the real
    /// <see cref="AcpServer"/> stack over stdio with a bundled echo agent so the
    /// binary is a working, self-contained ACP agent out of the box.
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            bool verbose = false;
            string? logFile = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--acp-server":
                        // Retained for backward compatibility; stdio server mode is the
                        // only mode this executable runs.
                        break;
                    case "--verbose":
                    case "-v":
                        verbose = true;
                        break;
                    case "--log-file":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Error: --log-file requires a file path");
                            return 1;
                        }
                        logFile = args[++i];
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

            try
            {
                return await RunAsync(verbose, logFile);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal: {ex.Message}");
                return 1;
            }
        }

        private static void ShowHelp()
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Andy ACP Server - an Agent Client Protocol agent over stdio");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage: Andy.Acp.Server [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --acp-server        Run as an ACP stdio server (default behavior)");
            Console.Error.WriteLine("  --verbose, -v       Enable debug-level logging");
            Console.Error.WriteLine("  --log-file <path>   Write diagnostics to a file instead of stderr");
            Console.Error.WriteLine("  --help, -h          Show this help message");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Diagnostics are written to stderr (or --log-file); stdout carries only");
            Console.Error.WriteLine("the JSON-RPC/ACP protocol stream.");
        }

        private static async Task<int> RunAsync(bool verbose, string? logFile)
        {
            StreamWriter? fileWriter = null;
            try
            {
                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);

                    // Diagnostics must never touch stdout, which is the protocol channel.
                    if (logFile != null)
                    {
                        fileWriter = new StreamWriter(
                            new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        {
                            AutoFlush = true
                        };
                        builder.AddProvider(new TextWriterLoggerProvider(fileWriter));
                    }
                    else
                    {
                        builder.AddProvider(new TextWriterLoggerProvider(Console.Error));
                    }
                });

                // Redirect any stray Console.Out writes to stderr so they cannot corrupt
                // the protocol stream (the transport uses the raw stdout handle directly).
                Console.SetOut(Console.Error);

                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                var serverInfo = new ServerInfo
                {
                    Name = "Andy ACP Server",
                    Version = version,
                    Description = "Andy Agent Client Protocol server (bundled echo agent)"
                };

                var agent = new BundledEchoAgentProvider();
                var server = new AcpServer(agent, serverInfo: serverInfo, loggerFactory: loggerFactory);

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                await server.RunAsync(cts.Token);
                return 0;
            }
            finally
            {
                fileWriter?.Dispose();
            }
        }
    }
}
