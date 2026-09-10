using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.UltimateStrm.Configuration;

/// <summary>
/// Plugin-wide settings. The .strm entries, yt2strm links, and custom iframe
/// mappings all live in the shared SQLite database (see Data/UltimateStrmDb.cs)
/// — this class only holds the handful of knobs that don't fit a per-row record.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        YtDlpPath = "yt-dlp";
        DefaultFormat = "b[ext=mp4]";
    }

    /// <summary>
    /// Gets or sets the command used to invoke yt-dlp. Defaults to relying on PATH,
    /// but systemd services don't always inherit the same PATH as an interactive
    /// shell — if "yt-dlp" alone doesn't work, set this to the absolute path
    /// (e.g. <c>/usr/local/bin/yt-dlp</c>, find it with <c>which yt-dlp</c>).
    /// </summary>
    public string YtDlpPath { get; set; }

    /// <summary>
    /// Gets or sets the default yt-dlp format selector used for links that don't
    /// specify their own. "best" works for the vast majority of sites.
    /// </summary>
    public string DefaultFormat { get; set; }
}
