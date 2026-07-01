using System;

namespace Jellyfin.Plugin.UltimateStrm.Yt2Strm.Data;

/// <summary>Represents a single yt-dlp–managed link and its resolution state.</summary>
public class LinkRecord
{
    /// <summary>Gets or sets the database row id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the user-supplied title. Null/empty means "auto-detect".</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the source URL.</summary>
    public string Url { get; set; }

    /// <summary>Gets or sets the destination folder path.</summary>
    public string SavePath { get; set; }

    /// <summary>Gets or sets the yt-dlp format selector.</summary>
    public string Format { get; set; }

    /// <summary>Gets or sets a value indicating whether this link is active for scheduled refresh.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the title actually used for the filename — either a copy of
    /// <see cref="Title"/>, or whatever yt-dlp last reported when
    /// <see cref="Title"/> was left blank.
    /// </summary>
    public string ResolvedTitle { get; set; }

    /// <summary>Gets or sets the current resolution status.</summary>
    public LinkStatus Status { get; set; }

    /// <summary>Gets or sets the full path of the .strm file currently on disk, if any.</summary>
    public string StrmPath { get; set; }

    /// <summary>Gets or sets the last error message, if the most recent resolution attempt failed.</summary>
    public string LastError { get; set; }

    /// <summary>Gets or sets when the link was last resolved.</summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>Gets or sets when the link was first added.</summary>
    public DateTime CreatedAt { get; set; }
}
