namespace ScalingReads.Core.IO;

public record PostAlbumInput(string Title, List<PostAlbumSongInput> Songs);
public record PostAlbumSongInput(string Title);
