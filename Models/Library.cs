namespace NineLivesAudio.Models;

public class Library
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<Folder> Folders { get; set; } = new();
    public int DisplayOrder { get; set; }
    public string Icon { get; set; } = "audiobook";
    public string MediaType { get; set; } = "book";
}

public class Folder
{
    public string Id { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string LibraryId { get; set; } = string.Empty;
}
