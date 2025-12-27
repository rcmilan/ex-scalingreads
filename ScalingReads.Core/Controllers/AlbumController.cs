using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScalingReads.Core.Configurations;
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

    [HttpGet("{id}")]
    [Cache(ttlSeconds: 120)]
    public async Task<ActionResult<GetAlbumOutput>> Get([FromServices] ReadOnlyDbContext dbContext, [FromRoute] int id)
    {
        var album = await dbContext.Albums
            .Where(a => a.Id == id)
            .Select(a => new GetAlbumOutput(
                a.Id,
                a.Title,
                a.Songs.Select(s => new GetAlbumSongOutput(s.Title)).ToList()
            )).FirstOrDefaultAsync();

        return Ok(album);
    }
}
