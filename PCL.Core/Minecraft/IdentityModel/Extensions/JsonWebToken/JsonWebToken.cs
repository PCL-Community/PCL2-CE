using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using PCL.Core.Minecraft.IdentityModel.Extensions.OpenId;
using PCL.Core.Utils;

namespace PCL.Core.Minecraft.IdentityModel.Extensions.JsonWebToken;

/// <summary>
/// Json Web Token 类
/// </summary>
/// <param name="token">JWT 令牌字符串</param>
/// <param name="meta">OpenID 元数据</param>
public class JsonWebToken(string token, OpenIdMetadata meta)
{
    public delegate SecurityToken? TokenValidateCallback(OpenIdMetadata metadata, string token, JsonWebKey? key, string? clientId);

    /// <summary>
    /// 安全令牌验证回调函数，默认验证签名、发行者、nbf 和 exp
    /// </summary>
    public TokenValidateCallback SecurityTokenValidateCallback { get; set; } = static (meta, token, key, clientId) =>
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            
            var parameter = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = meta.Issuer,
                ValidateAudience = !string.IsNullOrEmpty(clientId),
                ValidAudience = clientId,
                ValidateIssuerSigningKey = key is not null,
                IssuerSigningKey = key is not null ? new JsonWebKeySet { Keys = { key } }.Keys[0] : null,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(60),
                ValidAlgorithms = meta.IdTokenSigningAlgValuesSupported.Count == 0 ? null : meta.IdTokenSigningAlgValuesSupported
            };

            handler.ValidateToken(token, parameter, out var secToken);
            if (secToken is JwtSecurityToken jwt && jwt.IssuedAt != DateTime.MinValue &&
                jwt.IssuedAt > DateTime.UtcNow.Add(parameter.ClockSkew))
                throw new SecurityTokenInvalidLifetimeException("Token issued-at time is in the future.");
            return secToken;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"令牌验证失败：{ex.Message}", ex);
        }
    };

    private bool _verified;
    private JwtSecurityToken? _parsedToken;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    /// <summary>
    /// 解析令牌（不验证签名）
    /// </summary>
    /// <returns>解析后的 JWT 令牌对象</returns>
    /// <exception cref="SecurityException">令牌格式无效</exception>
    private JwtSecurityToken _ParseToken()
    {
        if (_parsedToken is not null)
            return _parsedToken;

        try
        {
            if (!_tokenHandler.CanReadToken(token))
                throw new SecurityException("无法读取令牌：格式无效");

            _parsedToken = _tokenHandler.ReadJwtToken(token);
            return _parsedToken;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"令牌解析失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 尝试读取 Token 中的字段
    /// </summary>
    /// <param name="allowUnverifyToken">是否允许在未验证的情况下读取字段，若为 false，当 Token 未验证时将抛出异常</param>
    /// <typeparam name="T">声明值的目标类型</typeparam>
    /// <returns>解析后的声明对象</returns>
    /// <exception cref="SecurityException">未调用 VerifySignature() 且 allowUnverifyToken 为 false</exception>
    /// <exception cref="InvalidOperationException">令牌中不存在 payload 数据</exception>
    public T? ReadTokenPayload<T>(bool allowUnverifyToken = false)
    {
        if (!allowUnverifyToken && !_verified)
            throw new SecurityException("不安全的令牌");

        try
        {
            var jwtToken = _ParseToken();

            if (jwtToken.Payload is null || jwtToken.Payload.Count == 0)
                throw new InvalidOperationException("令牌 Payload 无效");

            if (typeof(T).IsAssignableFrom(typeof(Dictionary<string, object>)))
                return (T)(object)jwtToken.Payload;

            if (typeof(T) == typeof(JwtPayload))
                return (T)(object)jwtToken.Payload;

            var payloadJson = JsonSerializer.Serialize(jwtToken.Payload, JsonCompat.SerializerOptions);
            var result = JsonSerializer.Deserialize<T>(payloadJson, JsonCompat.SerializerOptions);

            return result;
        }
        catch (SecurityException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"读取令牌 payload 失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 读取 Token 头
    /// </summary>
    /// <typeparam name="T">声明值的目标类型</typeparam>
    /// <returns>解析后的头对象</returns>
    /// <exception cref="InvalidOperationException">令牌中不存在 header 数据</exception>
    public T? ReadTokenHeader<T>()
    {
        try
        {
            var jwtToken = _ParseToken();

            if (jwtToken.Header is null || jwtToken.Header.Count == 0)
                throw new InvalidOperationException("令牌中不存在 header 数据");

            if (typeof(T).IsAssignableFrom(typeof(Dictionary<string, object>)))
                return (T)(object)jwtToken.Header;

            if (typeof(T) == typeof(JwtHeader))
                return (T)(object)jwtToken.Header;

            var headerJson = JsonSerializer.Serialize(jwtToken.Header, JsonCompat.SerializerOptions);
            var result = JsonSerializer.Deserialize<T>(headerJson, JsonCompat.SerializerOptions);

            return result;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"读取令牌 header 失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 对 Token 进行签名验证 <br/>
    /// 默认情况下仅对签名、iss、nbf、exp 进行验证，如果需要更细粒度验证，请设置 <see cref="SecurityTokenValidateCallback"/>
    /// </summary>
    /// <param name="key">用于验证签名的 JSON Web Key</param>
    /// <param name="clientId">预期的受众（audience），可选</param>
    /// <returns>验证成功返回 SecurityToken 对象，否则返回 null</returns>
    public SecurityToken? VerifySignature(JsonWebKey key, string? clientId = null)
    {
        try
        {
            var result = SecurityTokenValidateCallback.Invoke(meta, token, key, clientId);
            if (result is not null)
                _verified = true;
            return result;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"令牌签名验证失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 重载方法，用于无参调用验证（仅验证基本声明）
    /// </summary>
    /// <returns>验证成功返回 SecurityToken 对象，否则返回 null</returns>
    public SecurityToken? VerifySignature()
    {
        try
        {
            var result = SecurityTokenValidateCallback.Invoke(meta, token, null, null);
            if (result is not null)
                _verified = true;
            return result;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"令牌验证失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取令牌的过期时间
    /// </summary>
    /// <returns>过期时间，若不存在则返回 null</returns>
    public DateTime? GetExpirationTime()
    {
        try
        {
            var jwtToken = _ParseToken();
            return jwtToken.ValidTo != DateTime.MinValue ? jwtToken.ValidTo : null;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"获取令牌过期时间失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取令牌的签发时间
    /// </summary>
    /// <returns>签发时间，若不存在则返回 null</returns>
    public DateTime? GetIssuedAtTime()
    {
        try
        {
            var jwtToken = _ParseToken();
            return jwtToken.ValidFrom != DateTime.MinValue ? jwtToken.ValidFrom : null;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"获取令牌签发时间失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 检查令牌是否已过期
    /// </summary>
    /// <returns>若已过期返回 true，否则返回 false</returns>
    public bool IsExpired()
    {
        try
        {
            var expTime = GetExpirationTime();
            return expTime.HasValue && DateTime.UtcNow > expTime.Value;
        }
        catch
        {
            return true; // 如果无法解析，视为已过期
        }
    }

    /// <summary>
    /// 获取特定声明的值
    /// </summary>
    /// <param name="claimType">声明类型</param>
    /// <param name="allowUnverifyToken">是否允许在未验证的情况下读取</param>
    /// <returns>声明值，若不存在则返回 null</returns>
    public string? GetClaimValue(string claimType, bool allowUnverifyToken = false)
    {
        try
        {
            var payload = ReadTokenPayload<Dictionary<string, object>>(allowUnverifyToken);
            return payload?.TryGetValue(claimType, out var value) ?? false ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"获取声明值失败（{claimType}）：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取原始令牌字符串
    /// </summary>
    /// <returns>JWT 令牌字符串</returns>
    public string GetTokenString() => token;

    /// <summary>
    /// 检查令牌验证状态
    /// </summary>
    public bool IsVerified => _verified;
}
