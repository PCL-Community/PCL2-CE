using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Profile.Authentication.Utils.Models;

public record MojangResponse
{
    [JsonPropertyName("username")]
    public required string UserName { get; set; }
    [JsonPropertyName("roles")]
    public required IEnumerable<object> Roles { get; set; }
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; set; }
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; set; }
    [JsonPropertyName("token_type")]
    public required string TokenType { get; set; }
}
