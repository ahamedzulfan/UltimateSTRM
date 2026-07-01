using System;
using System.Collections.Generic;
using Jellyfin.Plugin.UltimateStrm.Strm.Api.Models;
using Jellyfin.Plugin.UltimateStrm.Strm.Data;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.UltimateStrm.Strm.Api;

/// <summary>
/// API endpoints for Strm Manager — create, list, edit and delete managed .strm files.
/// </summary>
[ApiController]
/// [Authorize(Policy = "RequiresElevation")]
[Route("UltimateStrm/strm")]
public class StrmController : ControllerBase
{
    private readonly StrmRepository _repository;
    private readonly ActivityLog _activityLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrmController"/> class.
    /// </summary>
    public StrmController(StrmRepository repository, ActivityLog activityLog)
    {
        _repository = repository;
        _activityLog = activityLog;
    }

    /// <summary>Gets every managed .strm entry.</summary>
    [HttpGet("entries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<StrmEntry>> GetEntries()
        => Ok(_repository.GetAll());

    /// <summary>Creates a batch of new .strm files and their metadata.</summary>
    [HttpPost("entries/batch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<BatchAddResult> CreateEntries([FromBody] List<StrmEntryRequest> requests)
    {
        var rows = requests.ConvertAll(r => (r.Title, r.FolderPath, r.ContentUrl));
        var result = _repository.AddMany(rows);
        foreach (var e in result.Created)
        {
            _activityLog.Log($"[StrmManager] Created '{e.Title}' → {e.FilePath}", "success");
        }

        foreach (var err in result.Errors)
        {
            _activityLog.Log($"[StrmManager] Failed to create row {err.Index + 1} '{err.Title}': {err.Message}", "error");
        }

        return Ok(result);
    }

    /// <summary>Updates an existing .strm entry, moving/renaming the file on disk if needed.</summary>
    [HttpPut("entries/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<StrmEntry> UpdateEntry([FromRoute] int id, [FromBody] StrmEntryRequest request)
    {
        try
        {
            var entry = _repository.Update(id, request.Title, request.FolderPath, request.ContentUrl);
            if (entry is null)
            {
                return NotFound();
            }

            _activityLog.Log($"[StrmManager] Updated '{entry.Title}' → {entry.FilePath}", "info");
            return Ok(entry);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Deletes a .strm entry and its file on disk.</summary>
    [HttpDelete("entries/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteEntry([FromRoute] int id)
    {
        var entry = _repository.GetById(id);
        if (entry is null)
        {
            return NotFound();
        }

        _repository.Delete(id);
        _activityLog.Log($"[StrmManager] Deleted '{entry.Title}'", "info");
        return NoContent();
    }
}
