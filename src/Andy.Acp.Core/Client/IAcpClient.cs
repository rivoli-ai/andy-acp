using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Andy.Acp.Core.Agent;

namespace Andy.Acp.Core.Client
{
    /// <summary>
    /// Agent-facing handle for issuing ACP requests to the client (editor) over the same
    /// connection: filesystem, terminal, and permission requests. These are ACP
    /// agent → client requests. Each capability is only usable when the client advertised
    /// it during initialize; otherwise the call fails locally without emitting a request.
    /// </summary>
    public interface IAcpClient
    {
        /// <summary>Filesystem operations served by the client.</summary>
        IClientFileSystem FileSystem { get; }

        /// <summary>Terminal operations served by the client.</summary>
        IClientTerminal Terminal { get; }

        /// <summary>Whether the client advertised <c>fs.readTextFile</c>.</summary>
        bool CanReadFiles { get; }

        /// <summary>Whether the client advertised <c>fs.writeTextFile</c>.</summary>
        bool CanWriteFiles { get; }

        /// <summary>Whether the client advertised terminal support.</summary>
        bool CanUseTerminal { get; }

        /// <summary>
        /// Requests user permission for a tool call (ACP <c>session/request_permission</c>).
        /// Returns the selected option or a cancellation outcome.
        /// </summary>
        Task<PermissionOutcome> RequestPermissionAsync(
            string sessionId,
            PermissionToolCall toolCall,
            IReadOnlyList<PermissionOption> options,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Client-served filesystem operations (ACP <c>fs/*</c>).</summary>
    public interface IClientFileSystem
    {
        /// <summary>Reads a text file via the client (ACP <c>fs/read_text_file</c>).</summary>
        Task<string> ReadTextFileAsync(
            string sessionId,
            string path,
            int? line = null,
            int? limit = null,
            CancellationToken cancellationToken = default);

        /// <summary>Writes a text file via the client (ACP <c>fs/write_text_file</c>).</summary>
        Task WriteTextFileAsync(
            string sessionId,
            string path,
            string content,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Client-served terminal operations (ACP <c>terminal/*</c>).</summary>
    public interface IClientTerminal
    {
        /// <summary>Creates a terminal running a command; returns the terminal id.</summary>
        Task<string> CreateAsync(
            string sessionId,
            string command,
            IReadOnlyList<string>? args = null,
            IReadOnlyList<EnvVariable>? env = null,
            string? cwd = null,
            long? outputByteLimit = null,
            CancellationToken cancellationToken = default);

        /// <summary>Gets current terminal output.</summary>
        Task<TerminalOutput> GetOutputAsync(string sessionId, string terminalId, CancellationToken cancellationToken = default);

        /// <summary>Waits for the command to exit.</summary>
        Task<TerminalExit> WaitForExitAsync(string sessionId, string terminalId, CancellationToken cancellationToken = default);

        /// <summary>Kills the running command.</summary>
        Task KillAsync(string sessionId, string terminalId, CancellationToken cancellationToken = default);

        /// <summary>Releases the terminal resources.</summary>
        Task ReleaseAsync(string sessionId, string terminalId, CancellationToken cancellationToken = default);
    }
}
