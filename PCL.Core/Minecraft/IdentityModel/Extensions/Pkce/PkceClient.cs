using System;
using PCL.Core.Utils.Exts;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Minecraft.IdentityModel.OAuth;
using PCL.Core.Utils.Hash;

namespace PCL.Core.Minecraft.IdentityModel.Extensions.Pkce;

/// <summary>
/// 带 PKCE 支持的客户端 <br/>
/// 此客户端并非线程安全，请勿在多个线程间共享示例
/// </summary>
/// <param name="options"></param>
public class PkceClient(OAuthClientOptions options):IOAuthClient
{
    private byte[] _ChallengeCode { get; set; } = new byte[32];
    private bool _isCallGetAuthorizeUrl;
    /// <summary>
    /// 设置验证方法，支持 PlainText 和 SHA256
    /// </summary>
    public PkceChallengeOptions ChallengeMethod { get; private set; } = PkceChallengeOptions.Sha256;
    private readonly SimpleOAuthClient _client = new(options);
    /// <summary>
    /// 获取授权地址
    /// </summary>
    /// <param name="scopes"></param>
    /// <param name="state"></param>
    /// <param name="extData"></param>
    /// <returns></returns>
    public string GetAuthorizeUrl(string[] scopes, string state, Dictionary<string, string>? extData)
    {
        RandomNumberGenerator.Fill(_ChallengeCode);
        extData ??= [];
        extData["code_challenge"] = ChallengeMethod == PkceChallengeOptions.Sha256
            ? SHA256Provider.Instance.ComputeHash(_ChallengeCode).ToHexString()
            : _ChallengeCode.FromBytesToB64UrlSafe();
        extData["code_challenge_method"] = ChallengeMethod == PkceChallengeOptions.Sha256 ? "S256":"plain";
        _isCallGetAuthorizeUrl = true;
        return _client.GetAuthorizeUrl(scopes, state, extData);
    }
    /// <summary>
    /// 使用授权代码兑换令牌
    /// </summary>
    /// <param name="code"></param>
    /// <param name="token"></param>
    /// <param name="extData"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<AuthorizeResult?> AuthorizeWithCodeAsync(string code, CancellationToken token, Dictionary<string, string>? extData = null)
    {
        if (!_isCallGetAuthorizeUrl) throw new InvalidOperationException("Challenge code is invalid");
        var pkce = _ChallengeCode.FromBytesToB64UrlSafe();
        extData ??= [];
        extData["code_verifier"] = pkce;
        _isCallGetAuthorizeUrl = false;
        return await _client.AuthorizeWithCodeAsync(code, token, extData);
    }
    /// <summary>
    /// 获取代码对
    /// </summary>
    /// <param name="scopes"></param>
    /// <param name="token"></param>
    /// <param name="extData"></param>
    /// <returns></returns>
    public async Task<DeviceCodeData?> GetCodePairAsync(string[] scopes, CancellationToken token, Dictionary<string, string>? extData = null)
    {
        return await _client.GetCodePairAsync(scopes, token, extData);
    }
    /// <summary>
    /// 发起一次验证，以检查认证是否成功
    /// </summary>
    /// <param name="data"></param>
    /// <param name="token"></param>
    /// <param name="extData"></param>
    /// <returns></returns>
    public async Task<AuthorizeResult?> AuthorizeWithDeviceAsync(DeviceCodeData data, CancellationToken token, Dictionary<string, string>? extData = null)
    {
        return await _client.AuthorizeWithDeviceAsync(data, token, extData);
    }

    public async Task<AuthorizeResult?> AuthorizeWithSilentAsync(AuthorizeResult data, CancellationToken token, Dictionary<string, string>? extData = null)
    {
        return await _client.AuthorizeWithSilentAsync(data, token, extData);
    }
}
