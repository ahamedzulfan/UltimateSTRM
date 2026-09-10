using System;
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

    /// <summary>Resolve the direct stream URL, prioritizing MP4 formats before falling back.</summary>
    public (string streamUrl, string error) ResolveUrl(string url, string format)
    {
        // 1. Try resolving strictly for MP4 first
        var mp4FormatSelector = "mp4";
        var args = string.Format(CultureInfo.InvariantCulture, "--js-runtimes node -g \"{1}\"", url);
        var (stdout, stderr) = RunYtDlp(args);

        if (!string.IsNullOrEmpty(stdout))
        {
            var lines = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            if (lines.Count > 0)
            {
                return (string.Join("\n", lines), null);
            }
        }

        return (null, stderr ?? "yt-dlp returned no output");
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