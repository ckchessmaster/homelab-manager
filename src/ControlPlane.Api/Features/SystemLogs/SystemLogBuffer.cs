using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.SystemLogs;

public class SystemLogBuffer : ISystemLogBuffer
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly LinkedList<SystemLogEntry> _entries = new();
    private long _nextId = 0;

    public SystemLogBuffer(int capacity = 2500)
    {
        _capacity = capacity > 0 ? capacity : 2500;
    }

    public void Enqueue(LogLevel logLevel, EventId eventId, string category, string message, Exception? exception)
    {
        var formattedException = exception?.ToString();
        var levelString = logLevel switch
        {
            LogLevel.Trace => "Trace",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => "Information"
        };

        lock (_lock)
        {
            _nextId++;
            var entry = new SystemLogEntry(
                Id: _nextId,
                Timestamp: DateTimeOffset.UtcNow,
                LogLevel: levelString,
                Category: category,
                Message: message,
                Exception: formattedException,
                EventId: eventId.Id != 0 ? eventId.Id : null,
                EventName: !string.IsNullOrEmpty(eventId.Name) ? eventId.Name : null
            );

            if (_entries.Count >= _capacity)
            {
                _entries.RemoveFirst();
            }

            _entries.AddLast(entry);
        }
    }

    public SystemLogResponse Query(
        string? level = null,
        string? minLevel = null,
        string? category = null,
        string? search = null,
        long? sinceId = null,
        int limit = 200,
        bool tail = true)
    {
        lock (_lock)
        {
            var query = _entries.AsEnumerable();

            if (sinceId.HasValue)
            {
                query = query.Where(e => e.Id > sinceId.Value);
            }

            if (!string.IsNullOrWhiteSpace(level))
            {
                query = query.Where(e => string.Equals(e.LogLevel, level, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(minLevel) && Enum.TryParse<LogLevel>(minLevel, true, out var parsedMinLevel))
            {
                query = query.Where(e =>
                {
                    if (Enum.TryParse<LogLevel>(e.LogLevel, true, out var entryLevel))
                    {
                        return entryLevel >= parsedMinLevel;
                    }
                    return false;
                });
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(e => e.Category.Contains(category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(e =>
                    e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (e.Exception != null && e.Exception.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    e.Category.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = query.ToList();
            var totalAvailable = filteredList.Count;

            var effectiveLimit = Math.Clamp(limit, 1, 1000);
            var paged = tail
                ? filteredList.TakeLast(effectiveLimit).ToList()
                : filteredList.Take(effectiveLimit).ToList();

            var stats = GetStatsInternal();

            return new SystemLogResponse(paged, stats, totalAvailable);
        }
    }

    public SystemLogStats GetStats()
    {
        lock (_lock)
        {
            return GetStatsInternal();
        }
    }

    private SystemLogStats GetStatsInternal()
    {
        int total = _entries.Count;
        int error = 0;
        int warning = 0;
        int info = 0;
        int debug = 0;

        foreach (var entry in _entries)
        {
            switch (entry.LogLevel)
            {
                case "Critical":
                case "Error":
                    error++;
                    break;
                case "Warning":
                    warning++;
                    break;
                case "Information":
                    info++;
                    break;
                case "Debug":
                case "Trace":
                    debug++;
                    break;
            }
        }

        return new SystemLogStats(
            TotalCount: total,
            ErrorCount: error,
            WarningCount: warning,
            InfoCount: info,
            DebugCount: debug,
            Capacity: _capacity
        );
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
