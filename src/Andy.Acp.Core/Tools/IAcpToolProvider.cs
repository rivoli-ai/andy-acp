using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.Tools
{
    /// <summary>
    /// Interface for providing tools to ACP clients.
    /// This is a minimal, protocol-aligned interface for tool registration and execution.
    /// Applications implementing ACP can provide their own implementations.
    /// </summary>
    /// <remarks>
    /// For rich tool frameworks, see Andy.Tools library. This interface is designed
    /// to be simple and lightweight for ACP protocol compliance.
    /// Applications can create adapters to bridge between IAcpToolProvider and
    /// more sophisticated tool frameworks.
    /// </remarks>
    public interface IAcpToolProvider
    {
        /// <summary>
        /// Gets the list of available tools.
        /// Called by the ACP protocol handler to respond to tools/list requests.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that returns the list of tool definitions.</returns>
        Task<IEnumerable<AcpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a tool with the given parameters.
        /// Called by the ACP protocol handler to respond to tools/call requests.
        /// </summary>
        /// <param name="name">The name of the tool to execute.</param>
        /// <param name="parameters">The parameters for tool execution.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that returns the tool execution result.</returns>
        Task<AcpToolResult> ExecuteToolAsync(
            string name,
            Dictionary<string, object?>? parameters,
            CancellationToken cancellationToken = default);
    }
}
