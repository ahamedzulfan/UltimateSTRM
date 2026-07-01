using System.Collections.Generic;
using Jellyfin.Plugin.UltimateStrm.CustomIframe.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.UltimateStrm.CustomIframe.Api;

/// <summary>
/// API endpoints for Custom Iframe Player — list, add and delete Jellyfin-ID → embed-URL mappings.
/// </summary>
[ApiController]
/// [Authorize(Policy = "RequiresElevation")]
[Route("UltimateStrm/iframe")]
public class IframeController : ControllerBase
{
    private readonly IframeMappingRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="IframeController"/> class.
    /// </summary>
    public IframeController(IframeMappingRepository repository)
    {
        _repository = repository;
    }

    /// <summary>Returns all iframe mappings (also used by the globalIntercept.js client script).</summary>
    [HttpGet("mappings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<IframeMapping>> GetMappings()
        => Ok(_repository.GetAll());

    /// <summary>Adds a new mapping.</summary>
    [HttpPost("mappings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult AddMapping([FromBody] IframeMappingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.JellyfinId))
        {
            return BadRequest(new { error = "JellyfinId is required." });
        }

        if (string.IsNullOrWhiteSpace(request.EmbedUrl))
        {
            return BadRequest(new { error = "EmbedUrl is required." });
        }

        var id = _repository.Add(request.JellyfinId.Trim(), request.EmbedUrl.Trim());
        return Ok(new { id });
    }

    /// <summary>Deletes a mapping by its database id.</summary>
    [HttpDelete("mappings/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteMapping([FromRoute] int id)
        => _repository.Delete(id) ? NoContent() : NotFound();
}

/// <summary>Request body for creating an iframe mapping.</summary>
public class IframeMappingRequest
{
    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public string JellyfinId { get; set; }

    /// <summary>Gets or sets the embed URL.</summary>
    public string EmbedUrl { get; set; }
}
