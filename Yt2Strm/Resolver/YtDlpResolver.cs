using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.UltimateStrm.Yt2Strm.Resolver;

/// <summary>Wraps yt-dlp process invocations for title fetching and URL resolution.</summary>
public class YtDlpResolver
{
    private readonly string _ytDlpPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="YtDlpResolver"/> class.
    /// </summary>
    public YtDlpResolver()
    {
        _ytDlpPath = Plugin.Instance?.Configuration?.YtDlpPath ?? "yt-dlp";
    }

    /// <summary>Fetch the video title from yt-dlp's --print metadata.</summary>
    public (string title, string error) FetchTitle(string url)
    {
        var args = string.Format(CultureInfo.InvariantCulture,
            "--print \"%(title).200B\" --skip-download \"{0}\"", url);
        var (stdout, stderr) = RunYtDlp(args);
        if (!string.IsNullOrEmpty(stdout))
        {
            var lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length > 0 && !string.IsNullOrEmpty(lines[0]))
            {
                return (lines[0].Trim(), null);
            }
        }

        return (null, stderr ?? "yt-dlp returned no output");
    }

    /// <summary>
    /// Resolve a single direct stream URL that has BOTH audio and video muxed together.
    /// This is required because .strm files can only reference one URL, and Jellyfin
    /// cannot merge separate audio-only/video-only DASH streams the way a local
    /// download + ffmpeg merge would.
    /// </summary>
    public (string streamUrl, string error) ResolveUrl(string url, string format)
    {
        // Selector priority:
        // 1. Best progressive mp4 with both audio+video present
        // 2. Best of any container with both audio+video present
        // 3. Best HLS (m3u8) stream - single URL, ffmpeg can play these with sync'd audio
        // 4. itag 18 - the near-universal legacy 360p muxed fallback
        var effectiveFormat = string.IsNullOrWhiteSpace(format)
            ? "best[ext=mp4][vcodec!=none][acodec!=none]/best[vcodec!=none][acodec!=none]/best[protocol*=m3u8]/18"
            : format;

        var (streamUrl, error) = TryResolve(url, effectiveFormat);
        if (streamUrl != null)
        {
            return (streamUrl, null);
        }

        // If the caller passed a custom format and it failed, fall back to our
        // safe default chain rather than giving up immediately.
        if (!string.IsNullOrWhiteSpace(format))
        {
            (streamUrl, error) = TryResolve(url,
                "best[ext=mp4][vcodec!=none][acodec!=none]/best[vcodec!=none][acodec!=none]/best[protocol*=m3u8]/18");
            if (streamUrl != null)
            {
                return (streamUrl, null);
            }
        }

        return (null, error ?? "yt-dlp could not resolve a muxed audio+video stream for this URL.");
    }

    private (string streamUrl, string error) TryResolve(string url, string format)
    {
        var args = string.Format(CultureInfo.InvariantCulture, "-f \"{0}\" -g \"{1}\"", format, url);
        var (stdout, stderr) = RunYtDlp(args);
        var lines = ParseUrlLines(stdout);

        if (lines.Count == 1)
        {
            return (lines[0], null);
        }

        if (lines.Count > 1)
        {
            // yt-dlp gave us separate video/audio URLs (DASH split). A .strm file
            // can't play these together, so treat this as a failure and let the
            // caller fall back to a safer format rather than silently returning
            // a video-only (or audio-only) link.
            return (null, "yt-dlp returned multiple separate streams (split audio/video) - no single muxed URL available for this format.");
        }

        return (null, stderr);
    }

    private static List<string> ParseUrlLines(string stdout)
    {
        if (string.IsNullOrEmpty(stdout))
        {
            return new List<string>();
        }

        return stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
    }

    private (string stdout, string stderr) RunYtDlp(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    return (null, "Failed to start yt-dlp process");
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    return (null, "yt-dlp timed out");
                }

                return (stdout, stderr);
            }
        }
        catch (Exception ex)
        {
            return (null, string.Format(CultureInfo.InvariantCulture, "Error running yt-dlp: {0}", ex.Message));
        }
    }
}