using System;
using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.Transport
{
    /// <summary>
    /// Interface for transport layer communication
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether the transport is connected
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Reads a message asynchronously from the transport
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The received message as a string</returns>
        Task<string> ReadMessageAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a message asynchronously to the transport
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task WriteMessageAsync(string message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the transport connection asynchronously
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task CloseAsync(CancellationToken cancellationToken = default);
    }
}