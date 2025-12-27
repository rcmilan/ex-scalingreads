namespace ScalingReads.Core.IO;

public record GetAlbumOutput(int Id, string Title, List<GetAlbumSongOutput> Songs);
public record GetAlbumSongOutput(string Title);
