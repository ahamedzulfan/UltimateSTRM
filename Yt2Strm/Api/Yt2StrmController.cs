using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Data;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.UltimateStrm.Yt2Strm.Api;

/// <summary>
/// API endpoints for yt2strm — add, update, delete and refresh yt-dlp–backed links.
/// </summary>
[ApiController]
/// [Authorize(Policy = "RequiresElevation")]
[Route("UltimateStrm/yt2strm")]
public class Yt2StrmController : ControllerBase
{
    private readonly LinkRepository _linkRepository;
    private readonly LinkRefreshService _refreshService;
    private readonly ActivityLog _activityLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="Yt2StrmController"/> class.
    /// </summary>
    public Yt2StrmController(LinkRepository linkRepository, LinkRefreshService refreshService, ActivityLog activityLog)
    {
        _linkRepository = linkRepository;
        _refreshService = refreshService;
        _activityLog = activityLog;
    }

    [HttpGet("links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<LinkDto>> GetLinks()
        => Ok(_linkRepository.GetAll().Select(LinkDto.FromRecord).ToList());

    [HttpPost("links")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult CreateLink([FromBody] CreateLinkRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.Url))
        {
            return BadRequest(new { error = "URL is required." });
        }

        if (string.IsNullOrWhiteSpace(req.SavePath))
        {
            return BadRequest(new { error = "Save folder is required." });
        }

        if (!Path.IsPathRooted(req.SavePath))
        {
            return BadRequest(new { error = "Save folder must be an absolute path." });
        }

        var url = req.Url.Trim();
        var title = (req.Title ?? string.Empty).Trim();
        var savePath = req.SavePath.Trim();
        var format = (req.Format ?? "best").Trim();

        if (_linkRepository.UrlExists(url))
        {
            return BadRequest(new { error = "This link has already been added." });
        }

        if (!string.IsNullOrEmpty(title) && _linkRepository.TitleExists(title))
        {
            return BadRequest(new { error = "A link with this title already exists." });
        }

        var record = new LinkRecord
        {
            Title = string.IsNullOrEmpty(title) ? null : title,
            Url = url,
            SavePath = savePath,
            Format = format,
            Enabled = true,
            Status = LinkStatus.Queued
        };

        var id = _linkRepository.Insert(record);
        _activityLog.Log($"[yt2strm] Added link #{id}: {title ?? url}", "info");
        _refreshService.RefreshLink(id);

        return CreatedAtAction(nameof(GetLinks), new { id }, new { id });
    }

    [HttpPut("links/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult UpdateLink(int id, [FromBody] UpdateLinkRequest req)
    {
        var record = _linkRepository.GetById(id);
        if (record == null)
        {
            return NotFound();
        }

        var newUrl = req?.Url?.Trim() ?? record.Url;
        var newTitle = req?.Title?.Trim() ?? record.Title;
        var newPath = req?.SavePath?.Trim() ?? record.SavePath;
        var newFormat = req?.Format?.Trim() ?? record.Format;
        var newEnabled = req?.Enabled ?? record.Enabled;

        if (string.IsNullOrEmpty(newUrl))
        {
            return BadRequest(new { error = "URL cannot be empty." });
        }

        if (string.IsNullOrEmpty(newPath) || !Path.IsPathRooted(newPath))
        {
            return BadRequest(new { error = "Save folder must be an absolute path." });
        }

        if (newUrl != record.Url && _linkRepository.UrlExists(newUrl, id))
        {
            return BadRequest(new { error = "This link has already been added." });
        }

        if (!string.IsNullOrEmpty(newTitle) && newTitle != record.Title && _linkRepository.TitleExists(newTitle, id))
        {
            return BadRequest(new { error = "A link with this title already exists." });
        }

        _linkRepository.UpdateDetails(id, newTitle, newUrl, newPath, newFormat, newEnabled);
        _activityLog.Log($"[yt2strm] Updated link #{id}", "info");
        return Ok(new { ok = true });
    }

    [HttpDelete("links/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteLink(int id)
    {
        var record = _linkRepository.GetById(id);
        if (record == null)
        {
            return NotFound();
        }

        try
        {
            string targetFilePath = null;
            if (!string.IsNullOrWhiteSpace(record.StrmPath) && System.IO.File.Exists(record.StrmPath))
            {
                targetFilePath = record.StrmPath;
            }
            else if (!string.IsNullOrWhiteSpace(record.SavePath))
            {
                var fileTitle = record.ResolvedTitle ?? record.Title;
                if (!string.IsNullOrWhiteSpace(fileTitle))
                {
                    foreach (char c in Path.GetInvalidFileNameChars())
                    {
                        fileTitle = fileTitle.Replace(c, '_');
                    }

                    var guessedPath = Path.Combine(record.SavePath, fileTitle + ".strm");
                    if (System.IO.File.Exists(guessedPath))
                    {
                        targetFilePath = guessedPath;
                    }
                }
            }

            if (targetFilePath != null)
            {
                System.IO.File.Delete(targetFilePath);
                _activityLog.Log($"[yt2strm] Deleted file: {targetFilePath}", "info");
            }
        }
        catch (Exception ex)
        {
            _activityLog.Log($"[yt2strm] Could not delete file for link #{id}: {ex.Message}", "error");
        }

        _linkRepository.Delete(id);
        _activityLog.Log($"[yt2strm] Deleted link #{id} from database", "info");
        return Ok(new { ok = true });
    }

    [HttpPost("links/{id:int}/refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RefreshLink(int id)
    {
        var record = _linkRepository.GetById(id);
        if (record == null)
        {
            return NotFound();
        }

        _refreshService.RefreshLink(id);
        return Ok(new { ok = true });
    }

    [HttpPost("refresh-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult RefreshAll()
    {
        _refreshService.RefreshAll();
        return Ok(new { ok = true });
    }

    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetStats()
    {
        var links = _linkRepository.GetAll();
        return Ok(new
        {
            total = links.Count,
            active = links.Count(l => l.Status == LinkStatus.Active),
            queued = links.Count(l => l.Status == LinkStatus.Queued || l.Status == LinkStatus.Running),
            dead = links.Count(l => l.Status == LinkStatus.Dead)
        });
    }

    [HttpGet("browse")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult Browse(string path = "/")
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
            {
                return BadRequest(new { error = "Path does not exist." });
            }

            var dirs = dir.GetDirectories()
                .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                .Select(d => d.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(new { path = dir.FullName, parent = dir.Parent?.FullName, dirs });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("activity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetActivity()
    {
        var entries = _activityLog.GetAll();
        return Ok(entries.Select(e => new { time = e.Time, level = e.Level, message = e.Message }));
    }
}

/// <summary>DTO for reading a link record back to the client.</summary>
public class LinkDto
{
    /// <summary>Gets or sets the row id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the source URL.</summary>
    public string Url { get; set; }

    /// <summary>Gets or sets the save folder.</summary>
    public string SavePath { get; set; }

    /// <summary>Gets or sets the yt-dlp format.</summary>
    public string Format { get; set; }

    /// <summary>Gets or sets a value indicating whether this link is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the auto-detected or overridden title.</summary>
    public string ResolvedTitle { get; set; }

    /// <summary>Gets or sets the status string.</summary>
    public string Status { get; set; }

    /// <summary>Gets or sets the .strm file path on disk.</summary>
    public string StrmPath { get; set; }

    /// <summary>Gets or sets the last error message.</summary>
    public string LastError { get; set; }

    /// <summary>Gets or sets when the link was last checked.</summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>Maps a <see cref="LinkRecord"/> to a <see cref="LinkDto"/>.</summary>
    public static LinkDto FromRecord(LinkRecord record) => new()
    {
        Id = record.Id,
        Title = record.Title,
        Url = record.Url,
        SavePath = record.SavePath,
        Format = record.Format,
        Enabled = record.Enabled,
        ResolvedTitle = record.ResolvedTitle,
        Status = record.Status.ToString().ToLowerInvariant(),
        StrmPath = record.StrmPath,
        LastError = record.LastError,
        LastChecked = record.LastChecked
    };
}

/// <summary>Request body for creating a link.</summary>
public class CreateLinkRequest
{
    /// <summary>Gets or sets the URL.</summary>
    public string Url { get; set; }

    /// <summary>Gets or sets the optional title.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the save folder path.</summary>
    public string SavePath { get; set; }

    /// <summary>Gets or sets the yt-dlp format selector.</summary>
    public string Format { get; set; }
}

/// <summary>Request body for updating a link.</summary>
public class UpdateLinkRequest
{
    /// <summary>Gets or sets the URL.</summary>
    public string Url { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the save folder path.</summary>
    public string SavePath { get; set; }

    /// <summary>Gets or sets the yt-dlp format selector.</summary>
    public string Format { get; set; }

    /// <summary>Gets or sets a value indicating whether this link should be refreshed on schedule.</summary>
    public bool? Enabled { get; set; }
}
