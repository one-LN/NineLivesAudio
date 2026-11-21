namespace NineLivesAudio.Models;

public class ServerProfile
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime? LastConnected { get; set; }
}
