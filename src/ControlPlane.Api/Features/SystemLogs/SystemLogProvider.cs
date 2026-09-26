using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.SystemLogs;

public sealed class SystemLogProvider : ILoggerProvider
{
    private readonly ISystemLogBuffer _buffer;
    [ThreadStatic]
    private static bool _isLogging;

    public SystemLogProvider(ISystemLogBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SystemLogger(categoryName, _buffer);
    }

    public void Dispose()
    {
    }

    private sealed class SystemLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ISystemLogBuffer _buffer;

        public SystemLogger(string categoryName, ISystemLogBuffer buffer)
        {
            _categoryName = categoryName;
            _buffer = buffer;
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
            if (!IsEnabled(logLevel)) return;
            if (_isLogging) return;

            try
            {
                _isLogging = true;
                var message = formatter != null ? formatter(state, exception) : state?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(message) && exception == null)
                {
                    return;
                }

                _buffer.Enqueue(logLevel, eventId, _categoryName, message, exception);
            }
            catch
            {
                // Must never throw exceptions from logger
            }
            finally
            {
                _isLogging = false;
            }
        }
    }
}
