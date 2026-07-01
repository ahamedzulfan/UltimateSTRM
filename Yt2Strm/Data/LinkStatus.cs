namespace Jellyfin.Plugin.UltimateStrm.Yt2Strm.Data;

/// <summary>
/// Queued   - just added, hasn't completed a first resolution attempt yet.
/// Running  - actively being resolved right now (transient).
/// Active   - last attempt succeeded; a .strm file exists on disk.
/// Dead     - last attempt failed; retried automatically on every scheduled run.
/// </summary>
public enum LinkStatus
{
    Queued = 0,
    Running = 1,
    Active = 2,
    Dead = 3
}
