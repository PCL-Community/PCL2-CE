using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Profile.Authentication.Utils.Models;

public record XboxUserHash
{
    [JsonPropertyName("uhs")]
    public required string UserHash { get; set; }
}