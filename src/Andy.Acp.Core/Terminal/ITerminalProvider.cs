using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.Terminal
{
    /// <summary>
    /// Interface for terminal/command execution operations that agents can perform.
    /// Implementations should handle security and resource management appropriately.
    /// </summary>
    public interface ITerminalProvider
    {
        /// <summary>
        /// Create a new terminal and execute a command.
        /// Returns a terminal ID that can be used for subsequent operations.
        /// </summary>
        /// <param name="command">The command to execute</param>
        /// <param name="workingDirectory">Working directory (null for current directory)</param>
        /// <param name="env">Environment variables to set (null to inherit)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A unique terminal ID</returns>
        Task<string> CreateTerminalAsync(
            string command,
            string? workingDirectory,
            Dictionary<string, string>? env,
            CancellationToken cancellationToken);

        /// <summary>
        /// Get the current output from a terminal.
        /// Returns the accumulated output since the last call.
        /// </summary>
        /// <param name="terminalId">The terminal ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Terminal output and status</returns>
        Task<TerminalOutputResult> GetTerminalOutputAsync(
            string terminalId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Wait for a terminal command to complete.
        /// Blocks until the command exits.
        /// </summary>
        /// <param name="terminalId">The terminal ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The exit code of the command</returns>
        Task<int> WaitForTerminalExitAsync(
            string terminalId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Kill a running terminal command.
        /// The terminal remains allocated for output retrieval.
        /// </summary>
        /// <param name="terminalId">The terminal ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task KillTerminalAsync(string terminalId, CancellationToken cancellationToken);

        /// <summary>
        /// Release terminal resources.
        /// Should be called after the terminal is no longer needed.
        /// </summary>
        /// <param name="terminalId">The terminal ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task ReleaseTerminalAsync(string terminalId, CancellationToken cancellationToken);
    }
}
