using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.UltimateStrm.Yt2Strm.Services;

/// <summary>Represents a single activity log entry.</summary>
public class ActivityLogEntry
{
    /// <summary>Gets or sets the UTC timestamp.</summary>
    public DateTime Time { get; set; }

    /// <summary>Gets or sets the log level: "info", "success", or "error".</summary>
    public string Level { get; set; }

    /// <summary>Gets or sets the message text.</summary>
    public string Message { get; set; }
}

/// <summary>
/// In-memory ring buffer that collects log entries from all plugin sub-modules
/// (StrmManager, yt2strm, CustomIframe, QuickSTRM) into a single combined stream.
/// Registered as a singleton in DI so every service shares the same instance.
/// </summary>
public class ActivityLog
{
    private readonly Queue<ActivityLogEntry> _entries = new();
    private readonly object _lock = new();
    private const int MaxEntries = 300;

    /// <summary>Appends a new log entry.</summary>
    /// <param name="message">The message text.</param>
    /// <param name="level">"info", "success", or "error".</param>
    public void Log(string message, string level = "info")
    {
        var entry = new ActivityLogEntry
        {
            Time = DateTime.UtcNow,
            Level = level,
            Message = message
        };

        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.Dequeue();
            }
        }
    }

    /// <summary>Returns all entries as a list (oldest first).</summary>
    public List<ActivityLogEntry> GetAll()
    {
        lock (_lock)
        {
            return new List<ActivityLogEntry>(_entries);
        }
    }
}
