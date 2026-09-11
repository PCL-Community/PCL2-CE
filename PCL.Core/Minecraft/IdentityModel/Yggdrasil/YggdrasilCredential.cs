using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.IdentityModel.Yggdrasil;

public record YggdrasilCredential
{
    [JsonPropertyName("username")] public required string User { get; init; }
    [JsonPropertyName("password")] public required string Password { get; init; }
    [JsonPropertyName("agent")] public Agent Agent { get; init; } = new();
    [JsonPropertyName("requestUser")] public bool RequestUser { get; set; }
    [JsonPropertyName("clientToken")] public string? ClientToken { get; set; }
}

public record YggdrasilAuthenticateResult
{
    /// <summary>
    /// 错误类型
    /// </summary>
    [JsonPropertyName("error")] public string? Error { get; init; }
    /// <summary>
    /// 错误消息
    /// </summary>
    [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; init; }
    /// <summary>
    /// 访问令牌
    /// </summary>
    [JsonPropertyName("accessToken")] public string? AccessToken { get; init; }
    /// <summary>
    /// 客户端令牌，基本没用
    /// </summary>
    [JsonPropertyName("clientToken")] public string? ClientToken { get; init; }
    /// <summary>
    /// 选择的档案
    /// </summary>
    [JsonPropertyName("selectedProfile")] public Profile? SelectedProfile { get; init; }
    /// <summary>
    /// 可用档案
    /// </summary>
    [JsonPropertyName("availableProfiles")] public Profile[]? AvailableProfiles { get; init; }
    /// <summary>
    /// 用户信息
    /// </summary>
    [JsonPropertyName("user")] public YggdrasilLegacyUser? User { get; init; }
}

public record YggdrasilRefresh
{
    [JsonPropertyName("accessToken")] public required string AccessToken { get; set; }
    [JsonPropertyName("selectedProfile")] public Profile? SelectedProfile { get; set; }
    [JsonPropertyName("clientToken")] public string? ClientToken { get; set; }
    [JsonPropertyName("requestUser")] public bool RequestUser { get; set; }
}
