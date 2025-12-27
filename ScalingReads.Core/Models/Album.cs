namespace ScalingReads.Core.Models;

public class Album
{
    public int Id { get; }
    public required string Title { get; set; }
    public List<Song> Songs { get; set; } = [];
}
