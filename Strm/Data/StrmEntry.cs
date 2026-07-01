using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.UltimateStrm.Strm.Data;

/// <summary>
/// Represents a single managed .strm file.
/// </summary>
public class StrmEntry
{
    /// <summary>
    /// Gets or sets the database row id.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the title. This is also used (sanitized) as the file name, e.g. "My Movie" -> "My Movie.strm".
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the destination folder where the .strm file is written.
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL/content path that is written inside the .strm file.
    /// </summary>
    public string ContentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full resolved path of the .strm file on disk (FolderPath + sanitized title + ".strm").
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC creation timestamp.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC last-updated timestamp.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// Result of a batch-add operation: the entries that were created successfully,
/// plus any per-row errors for entries that failed validation or could not be written.
/// </summary>
public class BatchAddResult
{
    /// <summary>
    /// Gets or sets the successfully created entries.
    /// </summary>
    public List<StrmEntry> Created { get; set; } = new();

    /// <summary>
    /// Gets or sets the errors for rows that failed.
    /// </summary>
    public List<BatchAddError> Errors { get; set; } = new();
}

/// <summary>
/// Describes a single failed row in a batch-add operation.
/// </summary>
public class BatchAddError
{
    /// <summary>
    /// Gets or sets the zero-based index of the failed row in the submitted batch.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the title of the failed row (for display purposes).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
