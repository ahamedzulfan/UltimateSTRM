namespace Jellyfin.Plugin.UltimateStrm.CustomIframe.Data;

/// <summary>Maps a Jellyfin library item ID to an external iframe embed URL.</summary>
public class IframeMapping
{
    /// <summary>Gets or sets the database row id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Jellyfin library item ID (the GUID shown in the item's detail URL).</summary>
    public string JellyfinId { get; set; } = string.Empty;

    /// <summary>Gets or sets the full embed URL opened in the iframe overlay.</summary>
    public string EmbedUrl { get; set; } = string.Empty;
}
