using System;
using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Profile.Models;

/// <summary>
/// 启动器档案的完整持久化模型。该类型是引用类型，档案 ID 是其稳定身份。
/// </summary>
public sealed class McProfile : SafeProfile
{
    [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("clientToken")] public string ClientToken { get; set; } = string.Empty;
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("skinHeadId")] public string SkinHeadId { get; set; } = string.Empty;
    [JsonPropertyName("rawJson")] public string RawJson { get; set; } = string.Empty;

    // Authlib/Yggdrasil
    [JsonPropertyName("server")] public string? Server { get; set; }
    [JsonPropertyName("serverName")] public string? ServerName { get; set; }
    [JsonPropertyName("name")] public string? LoginName { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }

    // OAuth/OpenID provider metadata
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
    [JsonPropertyName("discoveryAddress")]
    public string? DiscoveryAddress { get; set; }
    [JsonPropertyName("idToken")]
    public string? IdToken { get; set; }

    [JsonIgnore]
    public bool IsExpired => ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow.AddMinutes(1);

    public McProfile Clone() => new()
    {
        ProfileId = ProfileId,
        UserName = UserName,
        AccessToken = AccessToken,
        Uuid = Uuid,
        TokenType = TokenType,
        ProfileType = ProfileType,
        RefreshToken = RefreshToken,
        ClientToken = ClientToken,
        ExpiresAt = ExpiresAt,
        Description = Description,
        SkinHeadId = SkinHeadId,
        RawJson = RawJson,
        Server = Server,
        ServerName = ServerName,
        LoginName = LoginName,
        Password = Password,
        Provider = Provider,
        DiscoveryAddress = DiscoveryAddress,
        IdToken = IdToken
    };
}
