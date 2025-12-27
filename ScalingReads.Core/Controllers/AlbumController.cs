using Microsoft.AspNetCore.Mvc;

namespace ScalingReads.Core.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AlbumController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post()
    {
        return Ok();
    }
}
