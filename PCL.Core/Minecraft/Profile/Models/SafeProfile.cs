using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Profile.Models;

/// <summary>
/// 对外暴露的档案最小信息。敏感凭据只存在于内部的 <see cref="McProfile" /> 中。
/// </summary>
public class SafeProfile
{
    [JsonPropertyName("profileId")] public string ProfileId { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string UserName { get; set; } = string.Empty;
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("uuid")] public string Uuid { get; set; } = string.Empty;
    [JsonPropertyName("tokenType")] public string TokenType { get; set; } = "Bearer";
    [JsonPropertyName("profileType")] public ProfileType ProfileType { get; set; } = ProfileType.Offline;
}
