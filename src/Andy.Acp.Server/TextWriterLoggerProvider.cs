using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Andy.Acp.Server
{
    /// <summary>
    /// Minimal <see cref="ILoggerProvider"/> that writes single-line, timestamped log
    /// entries to a supplied <see cref="TextWriter"/> (stderr or a log file). Keeping
    /// this dependency-free guarantees that no diagnostics are ever written to stdout,
    /// which is reserved for the ACP protocol stream.
    /// </summary>
    internal sealed class TextWriterLoggerProvider : ILoggerProvider
    {
        private readonly TextWriter _writer;
        private readonly object _gate = new();

        public TextWriterLoggerProvider(TextWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public ILogger CreateLogger(string categoryName) => new TextWriterLogger(this, categoryName);

        public void Dispose()
        {
            // The provider does not own stderr; a file writer is disposed by the caller.
        }

        private void Write(string line)
        {
            lock (_gate)
            {
                _writer.WriteLine(line);
            }
        }

        private sealed class TextWriterLogger : ILogger
        {
            private readonly TextWriterLoggerProvider _provider;
            private readonly string _category;

            public TextWriterLogger(TextWriterLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                var message = formatter(state, exception);
                var shortCategory = _category.Substring(_category.LastIndexOf('.') + 1);
                var line = $"{DateTime.Now:HH:mm:ss} [{Short(logLevel)}] {shortCategory}: {message}";
                _provider.Write(line);

                if (exception != null)
                    _provider.Write(exception.ToString());
            }

            private static string Short(LogLevel level) => level switch
            {
                LogLevel.Trace => "trce",
                LogLevel.Debug => "dbug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "fail",
                LogLevel.Critical => "crit",
                _ => "none"
            };
        }
    }
}
