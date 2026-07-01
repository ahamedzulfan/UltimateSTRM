using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Data;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Resolver;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.UltimateStrm.Yt2Strm.Services;

/// <summary>
/// Background hosted service that owns the yt-dlp resolution worker.
/// Also exposes synchronous trigger methods used by the API controller
/// and the scheduled task.
/// </summary>
public class LinkRefreshService : IHostedService
{
    private readonly LinkRepository _linkRepository;
    private readonly YtDlpResolver _ytDlpResolver;
    private readonly ActivityLog _activityLog;
    private CancellationTokenSource _cancellationTokenSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRefreshService"/> class.
    /// </summary>
    public LinkRefreshService(LinkRepository linkRepository, YtDlpResolver ytDlpResolver, ActivityLog activityLog)
    {
        _linkRepository = linkRepository;
        _ytDlpResolver = ytDlpResolver;
        _activityLog = activityLog;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _activityLog.Log("[yt2strm] Plugin loaded and ready", "info");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>Called by the API to refresh a specific link immediately.</summary>
    public void RefreshLink(int linkId)
    {
        var link = _linkRepository.GetById(linkId);
        if (link == null || !link.Enabled)
        {
            return;
        }

        Task.Run(() => DoRefresh(link), _cancellationTokenSource.Token);
    }

    /// <summary>Called by the scheduled task to refresh all enabled links.</summary>
    public void RefreshAll()
    {
        var links = _linkRepository.GetAll().Where(l => l.Enabled).ToList();
        _activityLog.Log($"[yt2strm] Scheduled refresh triggered — processing {links.Count} link(s)", "info");
        foreach (var link in links)
        {
            Task.Run(() => DoRefresh(link), _cancellationTokenSource.Token);
        }
    }

    private void DoRefresh(LinkRecord link)
    {
        var label = link.Title ?? link.Url;
        try
        {
            _linkRepository.SetStatusRunning(link.Id);
            _activityLog.Log($"[yt2strm] Refreshing '{label}'", "info");

            var displayTitle = link.Title;
            if (string.IsNullOrWhiteSpace(displayTitle))
            {
                var (fetchedTitle, _) = _ytDlpResolver.FetchTitle(link.Url);
                displayTitle = !string.IsNullOrWhiteSpace(fetchedTitle)
                    ? fetchedTitle
                    : link.ResolvedTitle ?? $"video_{link.Id}";
            }

            if (string.IsNullOrWhiteSpace(link.Title) && _linkRepository.TitleExists(displayTitle, link.Id))
            {
                displayTitle = AppendDisambiguator(displayTitle);
            }

            var saveDir = new DirectoryInfo(link.SavePath);
            try
            {
                saveDir.Create();
            }
            catch (Exception ex)
            {
                RemoveStrmFile(link.StrmPath, displayTitle);
                _linkRepository.SetStatusFailed(link.Id, $"Could not create folder: {ex.Message}", displayTitle);
                _activityLog.Log($"[yt2strm] '{displayTitle}': could not create folder ({ex.Message})", "error");
                return;
            }

            var (streamUrl, resolveErr) = _ytDlpResolver.ResolveUrl(link.Url, link.Format);
            if (string.IsNullOrEmpty(streamUrl))
            {
                RemoveStrmFile(link.StrmPath, displayTitle);
                _linkRepository.SetStatusFailed(link.Id, resolveErr, displayTitle);
                _activityLog.Log($"[yt2strm] '{displayTitle}': {resolveErr}", "error");
                return;
            }

            var sanitized = SanitizeFilename(displayTitle);
            var strmPath = Path.Combine(link.SavePath, $"{sanitized}.strm");

            try
            {
                File.WriteAllText(strmPath, streamUrl + Environment.NewLine);
            }
            catch (Exception ex)
            {
                RemoveStrmFile(link.StrmPath, displayTitle);
                _linkRepository.SetStatusFailed(link.Id, $"Could not write file: {ex.Message}", displayTitle);
                _activityLog.Log($"[yt2strm] '{displayTitle}': could not write file ({ex.Message})", "error");
                return;
            }

            _linkRepository.SetStatusSuccess(link.Id, displayTitle, strmPath);
            _activityLog.Log($"[yt2strm] '{displayTitle}' → {strmPath}", "success");

            if (!string.IsNullOrEmpty(link.StrmPath) && link.StrmPath != strmPath)
            {
                RemoveStrmFile(link.StrmPath, displayTitle);
            }
        }
        catch (Exception ex)
        {
            _linkRepository.SetStatusFailed(link.Id, ex.Message, link.ResolvedTitle ?? label);
            _activityLog.Log($"[yt2strm] Unexpected error refreshing '{label}': {ex.Message}", "error");
        }
    }

    private void RemoveStrmFile(string strmPath, string label)
    {
        if (string.IsNullOrEmpty(strmPath))
        {
            return;
        }

        try
        {
            var file = new FileInfo(strmPath);
            if (file.Exists)
            {
                file.Delete();
                _activityLog.Log($"[yt2strm] Removed old .strm for '{label}' ({strmPath})", "info");
            }
        }
        catch (Exception ex)
        {
            _activityLog.Log($"[yt2strm] Could not remove old .strm for '{label}': {ex.Message}", "error");
        }
    }

    private static string SanitizeFilename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "untitled";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name
            .Replace("\\", "_").Replace("/", "_").Replace(":", "_")
            .Replace("*", "_").Replace("?", "_").Replace("\"", "_")
            .Replace("<", "_").Replace(">", "_").Replace("|", "_")
            .Where(c => !invalid.Contains(c))
            .ToArray());

        result = Regex.Replace(result, @"\s+", "_");
        return string.IsNullOrWhiteSpace(result) ? "untitled" : result;
    }

    private string AppendDisambiguator(string title)
    {
        for (int i = 2; i <= 999; i++)
        {
            var candidate = $"{title} ({i})";
            if (!_linkRepository.TitleExists(candidate))
            {
                return candidate;
            }
        }

        return title;
    }
}
