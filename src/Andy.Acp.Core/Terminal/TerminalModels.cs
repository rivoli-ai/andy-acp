namespace Andy.Acp.Core.Terminal
{
    /// <summary>
    /// Result of getting terminal output
    /// </summary>
    public class TerminalOutputResult
    {
        /// <summary>
        /// The terminal ID
        /// </summary>
        public string TerminalId { get; set; } = string.Empty;

        /// <summary>
        /// Output text (stdout)
        /// </summary>
        public string Output { get; set; } = string.Empty;

        /// <summary>
        /// Error output (stderr)
        /// </summary>
        public string? ErrorOutput { get; set; }

        /// <summary>
        /// Whether the command has exited
        /// </summary>
        public bool HasExited { get; set; }

        /// <summary>
        /// Exit code (if has exited)
        /// </summary>
        public int? ExitCode { get; set; }
    }
}
