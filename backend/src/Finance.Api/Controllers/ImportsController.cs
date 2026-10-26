using Finance.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize, Route("api/imports/csv"), RequestSizeLimit(4_500_000)]
public sealed class ImportsController(ImportHandler handler) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(ImportCommand command, CancellationToken cancellationToken) =>
        Ok(await handler.PreviewAsync(command, cancellationToken));
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(ImportCommand command, CancellationToken cancellationToken) =>
        StatusCode(201, await handler.ApplyAsync(command, cancellationToken));
}
