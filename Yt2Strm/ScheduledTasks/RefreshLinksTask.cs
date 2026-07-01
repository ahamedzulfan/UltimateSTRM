using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.UltimateStrm.Yt2Strm.ScheduledTasks;

/// <summary>Scheduled task that refreshes all enabled yt2strm links on demand or on a timer.</summary>
public class RefreshLinksTask : IScheduledTask
{
    private readonly LinkRefreshService _refreshService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshLinksTask"/> class.
    /// </summary>
    public RefreshLinksTask(LinkRefreshService refreshService)
    {
        _refreshService = refreshService;
    }

    /// <inheritdoc />
    public string Name => "Ultimate STRM: Refresh all yt2strm links";

    /// <inheritdoc />
    public string Description => "Resolves all enabled yt2strm links and refreshes their .strm files.";

    /// <inheritdoc />
    public string Category => "Ultimate STRM";

    /// <inheritdoc />
    public string Key => "UltimateStrmRefreshYt2StrmLinks";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _refreshService.RefreshAll();
        progress?.Report(100);
        return Task.CompletedTask;
    }
}
