using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.SystemLogs;

public interface ISystemLogBuffer
{
    void Enqueue(LogLevel logLevel, EventId eventId, string category, string message, Exception? exception);
    SystemLogResponse Query(string? level = null, string? minLevel = null, string? category = null, string? search = null, long? sinceId = null, int limit = 200, bool tail = true);
    SystemLogStats GetStats();
    void Clear();
}
