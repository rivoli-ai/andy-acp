using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.JsonRpc;
using Andy.Acp.Core.Terminal;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Core.Protocol
{
    /// <summary>
    /// Handles ACP terminal protocol methods (terminal/create, terminal/output, etc.)
    /// </summary>
    public class AcpTerminalHandler
    {
        private readonly ITerminalProvider _terminalProvider;
        private readonly ILogger<AcpTerminalHandler>? _logger;
        private readonly JsonRpcHandler _jsonRpcHandler;

        public AcpTerminalHandler(
            ITerminalProvider terminalProvider,
            JsonRpcHandler jsonRpcHandler,
            ILogger<AcpTerminalHandler>? logger = null)
        {
            _terminalProvider = terminalProvider ?? throw new ArgumentNullException(nameof(terminalProvider));
            _jsonRpcHandler = jsonRpcHandler ?? throw new ArgumentNullException(nameof(jsonRpcHandler));
            _logger = logger;
        }

        /// <summary>
        /// Register all terminal/* methods with the JSON-RPC handler
        /// </summary>
        public void RegisterMethods()
        {
            _jsonRpcHandler.RegisterMethod("terminal/create", HandleCreateTerminalAsync);
            _jsonRpcHandler.RegisterMethod("terminal/output", HandleTerminalOutputAsync);
            _jsonRpcHandler.RegisterMethod("terminal/wait_for_exit", HandleWaitForExitAsync);
            _jsonRpcHandler.RegisterMethod("terminal/kill", HandleKillTerminalAsync);
            _jsonRpcHandler.RegisterMethod("terminal/release", HandleReleaseTerminalAsync);

            _logger?.LogInformation("Registered ACP terminal methods: terminal/create, terminal/output, terminal/wait_for_exit, terminal/kill, terminal/release");
        }

        private async Task<object?> HandleCreateTerminalAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling terminal/create request");

            try
            {
                var createParams = DeserializeParams<CreateTerminalRequest>(parameters);

                if (string.IsNullOrEmpty(createParams?.Command))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Command is required");
                }

                _logger?.LogInformation("Creating terminal for command: {Command}", createParams.Command);

                var terminalId = await _terminalProvider.CreateTerminalAsync(
                    createParams.Command,
                    createParams.WorkingDirectory,
                    createParams.Env,
                    cancellationToken);

                return new
                {
                    terminalId
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating terminal");
                throw;
            }
        }

        private async Task<object?> HandleTerminalOutputAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling terminal/output request");

            try
            {
                var outputParams = DeserializeParams<TerminalOutputRequest>(parameters);

                if (string.IsNullOrEmpty(outputParams?.TerminalId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Terminal ID is required");
                }

                var result = await _terminalProvider.GetTerminalOutputAsync(
                    outputParams.TerminalId,
                    cancellationToken);

                return new
                {
                    terminalId = result.TerminalId,
                    output = result.Output,
                    errorOutput = result.ErrorOutput,
                    hasExited = result.HasExited,
                    exitCode = result.ExitCode
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting terminal output");
                throw;
            }
        }

        private async Task<object?> HandleWaitForExitAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling terminal/wait_for_exit request");

            try
            {
                var waitParams = DeserializeParams<WaitForExitRequest>(parameters);

                if (string.IsNullOrEmpty(waitParams?.TerminalId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Terminal ID is required");
                }

                _logger?.LogInformation("Waiting for terminal {TerminalId} to exit", waitParams.TerminalId);

                var exitCode = await _terminalProvider.WaitForTerminalExitAsync(
                    waitParams.TerminalId,
                    cancellationToken);

                return new
                {
                    exitCode
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error waiting for terminal exit");
                throw;
            }
        }

        private async Task<object?> HandleKillTerminalAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling terminal/kill request");

            try
            {
                var killParams = DeserializeParams<KillTerminalRequest>(parameters);

                if (string.IsNullOrEmpty(killParams?.TerminalId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Terminal ID is required");
                }

                _logger?.LogInformation("Killing terminal {TerminalId}", killParams.TerminalId);

                await _terminalProvider.KillTerminalAsync(killParams.TerminalId, cancellationToken);

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error killing terminal");
                throw;
            }
        }

        private async Task<object?> HandleReleaseTerminalAsync(object? parameters, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Handling terminal/release request");

            try
            {
                var releaseParams = DeserializeParams<ReleaseTerminalRequest>(parameters);

                if (string.IsNullOrEmpty(releaseParams?.TerminalId))
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcErrorCodes.InvalidParams,
                        "Terminal ID is required");
                }

                _logger?.LogInformation("Releasing terminal {TerminalId}", releaseParams.TerminalId);

                await _terminalProvider.ReleaseTerminalAsync(releaseParams.TerminalId, cancellationToken);

                return new { success = true };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error releasing terminal");
                throw;
            }
        }

        private T? DeserializeParams<T>(object? parameters) where T : class
        {
            if (parameters == null)
                return null;

            if (parameters is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }

            var json = JsonSerializer.Serialize(parameters);
            return JsonSerializer.Deserialize<T>(json);
        }

        private class CreateTerminalRequest
        {
            public string Command { get; set; } = string.Empty;
            public string? WorkingDirectory { get; set; }
            public Dictionary<string, string>? Env { get; set; }
        }

        private class TerminalOutputRequest
        {
            public string TerminalId { get; set; } = string.Empty;
        }

        private class WaitForExitRequest
        {
            public string TerminalId { get; set; } = string.Empty;
        }

        private class KillTerminalRequest
        {
            public string TerminalId { get; set; } = string.Empty;
        }

        private class ReleaseTerminalRequest
        {
            public string TerminalId { get; set; } = string.Empty;
        }
    }
}
