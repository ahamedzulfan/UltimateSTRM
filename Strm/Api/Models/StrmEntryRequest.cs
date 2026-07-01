namespace Jellyfin.Plugin.UltimateStrm.Strm.Api.Models;

/// <summary>
/// Request body used for both creating and updating a .strm entry.
/// </summary>
public class StrmEntryRequest
{
    /// <summary>Gets or sets the title (used as the file name).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the destination folder for the .strm file.</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the URL/content written inside the .strm file.</summary>
    public string ContentUrl { get; set; } = string.Empty;
}
