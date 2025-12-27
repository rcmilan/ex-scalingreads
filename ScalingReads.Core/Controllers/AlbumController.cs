using Microsoft.AspNetCore.Mvc;
using ScalingReads.Core.Data;
using ScalingReads.Core.IO;
using ScalingReads.Core.Models;

namespace ScalingReads.Core.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AlbumController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PostAlbumOutput>> Post([FromServices] AppDbContext dbContext, [FromBody] PostAlbumInput input)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        using var transaction = await dbContext.Database.BeginTransactionAsync();

        var newAlbum = new Album()
        {
            Title = input.Title,
            Songs = [.. input.Songs.Select(s => new Song(s.Title))]
        };

        await dbContext.Albums.AddAsync(newAlbum);

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var result = new PostAlbumOutput(newAlbum.Id);

        return Ok(result);
    }
}
