using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.IdentityModel.Yggdrasil;

/// <summary>
/// Yggdrasil Agent
/// </summary>
public record Agent
{
    [JsonPropertyName("name")] public string Name { get; init; } = "Minecraft";
    [JsonPropertyName("version")] public int Version { get; init; } = 1;
}
