using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Core.Minecraft.IdentityModel.Yggdrasil;

namespace PCL.Core.Minecraft.Profile.Authentication.Utils.Models;

public record MojangProfile
{
    [JsonPropertyName("id")]
    public required string Uuid { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("properties")]
    public List<PlayerProperty>? Properties { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
