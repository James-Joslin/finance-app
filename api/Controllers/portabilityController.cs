using financesApi.models;
using financesApi.services;
using Microsoft.AspNetCore.Mvc;

namespace financesApi.controllers;

[ApiController]
[Route("portability")]
public sealed class PortabilityController : ControllerBase
{
    [HttpGet("export/archive")]
    public async Task<IActionResult> ExportArchive()
    {
        var result = await PortabilityService.ExportArchiveAsync();
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpGet("export/{entity}")]
    public async Task<IActionResult> ExportEntity(string entity)
    {
        var result = await PortabilityService.ExportEntityAsync(entity);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpPost("import")]
    [RequestSizeLimit(PortabilityService.MaxArchiveRequestBytes)]
    public async Task<ActionResult<PortableImportSummary>> Import([FromForm] IFormFile archive)
    {
        if (archive is null || archive.Length == 0)
            throw new ArgumentException("The uploaded archive is empty.");
        await using var stream = archive.OpenReadStream();
        return Ok(await PortabilityService.ImportArchiveAsync(stream));
    }
}
