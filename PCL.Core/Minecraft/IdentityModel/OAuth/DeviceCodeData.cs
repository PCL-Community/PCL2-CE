using System.Text.Json.Serialization;
using PCL.Core.Utils.Exts;

namespace PCL.Core.Minecraft.IdentityModel.OAuth;

public record DeviceCodeData
{
    public bool IsError => !Error.IsNullOrEmpty();

    /// <summary>
    /// 按 Yggdrasil Connect / RFC 8628 规范校验设备授权响应的必须字段。
    /// 错误响应（error 存在时）不在此校验，交由调用方按错误处理。
    /// </summary>
    /// <exception cref="IdentityModelAuthenticationException">缺少协议规定的必须字段</exception>
    public void Validate()
    {
        if (IsError) return;
        if (DeviceCode.IsNullOrEmpty())
            throw new IdentityModelAuthenticationException("invalid_request", "设备授权响应缺少必须字段 device_code");
        if (UserCode.IsNullOrEmpty())
            throw new IdentityModelAuthenticationException("invalid_request", "设备授权响应缺少必须字段 user_code");
        if (VerificationUri.IsNullOrEmpty())
            throw new IdentityModelAuthenticationException("invalid_request", "设备授权响应缺少必须字段 verification_uri");
        if (ExpiresIn is null or < 0)
            throw new IdentityModelAuthenticationException("invalid_request", "设备授权响应缺少必须字段 expires_in");
    }
    /// <summary>
    /// 错误类型
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
    /// <summary>
    /// 错误描述
    /// </summary>
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
    /// <summary>
    /// 用户授权码
    /// </summary>
    [JsonPropertyName("user_code")]
    public string? UserCode { get; init; }
    /// <summary>
    /// 设备授权码
    /// </summary>
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; init; }
    /// <summary>
    /// 验证 Uri
    /// </summary>
    [JsonPropertyName("verification_uri")]
    public string? VerificationUri { get; init; }
    /// <summary>
    /// 验证 Uri （自动填充代码）
    /// </summary>
    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; init; }
    /// <summary>
    /// 轮询间隔
    /// </summary>
    [JsonPropertyName("interval")]
    public int? Interval { get; init; }
    /// <summary>
    /// 过期时间
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; init; }
}