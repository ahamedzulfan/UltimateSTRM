using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.UltimateStrm.Strm.Api;

[ApiController]
/// [Authorize(Policy = "RequiresElevation")]
[Route("UltimateStrm/quickstrm")]
public class QuickStrmController : ControllerBase
{
    private readonly ActivityLog _activityLog;

    public QuickStrmController(ActivityLog activityLog)
    {
        _activityLog = activityLog;
    }

    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Create([FromBody] QuickStrmRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Title))
            return BadRequest(new { error = "Title is required." });

        if (string.IsNullOrWhiteSpace(request.FolderPath))
            return BadRequest(new { error = "Folder path is required." });

        if (!Path.IsPathRooted(request.FolderPath))
            return BadRequest(new { error = "Folder path must be an absolute path." });

        try
        {
            Directory.CreateDirectory(request.FolderPath);

            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = new string(request.Title.Trim().Where(c => !invalidChars.Contains(c)).ToArray());
            if (string.IsNullOrWhiteSpace(safeName))
                return BadRequest(new { error = "Title contains only invalid characters." });

            var filePath = Path.Combine(request.FolderPath, safeName + ".strm");
            System.IO.File.WriteAllText(filePath, string.Empty);

            _activityLog.Log($"[QuickSTRM] Created '{safeName}.strm' in {request.FolderPath}", "success");
            return Ok(new { ok = true, filePath });
        }
        catch (Exception ex)
        {
            _activityLog.Log($"[QuickSTRM] Failed to create '{request.Title}': {ex.Message}", "error");
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class QuickStrmRequest
{
    public string Title { get; set; }
    public string FolderPath { get; set; }
}