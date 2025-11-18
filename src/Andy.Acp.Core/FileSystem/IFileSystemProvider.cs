using System.Threading;
using System.Threading.Tasks;

namespace Andy.Acp.Core.FileSystem
{
    /// <summary>
    /// Interface for file system operations that agents can perform.
    /// Implementations should handle security and permissions appropriately.
    /// </summary>
    public interface IFileSystemProvider
    {
        /// <summary>
        /// Read the contents of a text file.
        /// </summary>
        /// <param name="path">Absolute or relative path to the file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The file contents as a string</returns>
        /// <exception cref="System.IO.FileNotFoundException">If the file doesn't exist</exception>
        /// <exception cref="System.UnauthorizedAccessException">If access is denied</exception>
        Task<string> ReadTextFileAsync(string path, CancellationToken cancellationToken);

        /// <summary>
        /// Write text content to a file, creating it if it doesn't exist.
        /// </summary>
        /// <param name="path">Absolute or relative path to the file</param>
        /// <param name="content">The content to write</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="System.UnauthorizedAccessException">If access is denied</exception>
        Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken);
    }
}
