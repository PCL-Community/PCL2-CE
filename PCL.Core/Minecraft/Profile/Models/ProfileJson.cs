using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Profile.Models;

public sealed class ProfileJson<T>
{
    [JsonPropertyName("lastUsed")]
    public int LastUsed { get; set; } = -1;

    [JsonPropertyName("profiles")]
    public List<T> Profiles { get; set; } = [];
}
