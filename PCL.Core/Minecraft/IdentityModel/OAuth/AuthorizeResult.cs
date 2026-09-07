using System;
using System.Text.Json.Serialization;
using PCL.Core.Utils.Exts;

namespace PCL.Core.Minecraft.IdentityModel.OAuth;

public record AuthorizeResult
{
    public bool IsError => !Error.IsNullOrEmpty();

    /// <summary>
    /// 按 Yggdrasil Connect / OAuth 2.0 规范校验令牌响应的必须字段。
    /// 错误响应（error 存在时）不在此校验，交由调用方按错误处理。
    /// </summary>
    /// <param name="requireIdToken">授权时申请了 openid 权限范围则为 true，此时 id_token 条件必须</param>
    /// <param name="requireRefreshToken">授权时申请了 offline_access 权限范围则为 true，此时 refresh_token 条件必须</param>
    /// <exception cref="IdentityModelAuthenticationException">缺少协议规定的必须字段</exception>
    public void Validate(bool requireIdToken = false, bool requireRefreshToken = false)
    {
        if (IsError) return;
        if (AccessToken.IsNullOrEmpty())
            throw new IdentityModelAuthenticationException("invalid_request", "令牌响应缺少必须字段 access_token");
        if (TokenType.IsNullOrEmpty())
            throw new IdentityModelAuthenticationException("invalid_request", "令牌响应缺少必须字段 token_type");
        if (!string.Equals(TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new IdentityModelAuthenticationException("invalid_request", $"不支持的 token_type：{TokenType}");
        if (ExpiresIn is null or < 0)
            throw new IdentityModelAuthenticationException("invalid_request", "令牌响应缺少必须字段 expires_in");
        if (requireRefreshToken && RefreshToken.IsNullOrEmpty())
            throw new IdentityModelAuthenticationException("invalid_request", "已申请 offline_access 但令牌响应缺少 refresh_token");
        if (requireIdToken && IdToken.IsNullOrEmpty())
            throw new IdentityModelAuthenticationException("invalid_request", "已申请 openid 但令牌响应缺少 id_token");
    }
    /// <summary>
    /// 错误类型 (e.g. invalid_request)
    /// </summary>
    [JsonPropertyName("error")] public string? Error { get; init; }
    /// <summary>
    /// 描述此错误的文本
    /// </summary>
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; init; }

    // 不用 SecureString，因为这东西依赖 DPAPI，不是最佳实践

    /// <summary>
    /// 访问令牌
    /// </summary>
    [JsonPropertyName("access_token")] public string? AccessToken { get; init; }
    /// <summary>
    /// 刷新令牌
    /// </summary>
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }
    /// <summary>
    /// ID Token
    /// </summary>
    [JsonPropertyName("id_token")] public string? IdToken { get; init; }
    /// <summary>
    /// 令牌类型
    /// </summary>
    [JsonPropertyName("token_type")] public string? TokenType { get; init; }
    /// <summary>
    /// 过期时间
    /// </summary>
    [JsonPropertyName("expires_in")] public int? ExpiresIn { get; init; }
    [JsonPropertyName("scope")] public string? Scope { get; init; }
    [JsonPropertyName("error_uri")] public string? ErrorUri { get; init; }
}
