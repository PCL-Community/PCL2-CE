namespace PCL.Core.Minecraft.Profile.Authentication.Utils.Models;

public record XboxProperty
{
    public string AuthMethod { get; set; } = "RPS";
    public string SiteName { get; set; } = "user.auth.xboxlive.com";
    public required string RpsTicket { get; set; }
}