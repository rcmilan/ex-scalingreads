namespace ScalingReads.Core.Models;

public class Album
{
    public required string Title { get; set; }
    public List<Song> Songs { get; set; } = [];
}
