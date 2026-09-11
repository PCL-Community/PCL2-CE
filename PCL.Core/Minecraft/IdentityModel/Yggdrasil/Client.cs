using System;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net.Http;
using PCL.Core.Utils;

namespace PCL.Core.Minecraft.IdentityModel.Yggdrasil;

/// <summary>
///     提供 Yggdrasil 传统认证支持
/// </summary>
/// <param name="options"><see cref="YggdrasilLegacyAuthenticateOptions" /> 认证参数</param>
public sealed class YggdrasilLegacyClient(YggdrasilLegacyAuthenticateOptions options)
{
    /// <summary>
    ///     异步向服务器发送一次登录请求
    /// </summary>
    /// <param name="token"></param>
    /// <returns><see cref="YggdrasilAuthenticateResult" />  认证结果</returns>
    /// <exception cref="ArgumentException">用户名或密码无效</exception>
    public async Task<YggdrasilAuthenticateResult?> AuthenticateAsync(CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(options.Username);
        ArgumentException.ThrowIfNullOrEmpty(options.Password);

        var credential = new YggdrasilCredential
        {
            User = options.Username,
            Password = options.Password,
            ClientToken = options.ClientToken
        };
        var address = $"{options.YggdrasilApiLocation}/authserver/authenticate";

        using var response = await HttpRequest
            .CreatePost(address)
            .WithHeaders(options.Headers ?? [])
            .WithJsonContent(credential)
            .SendAsync(options.GetClient.Invoke(), cancellationToken: token)
            .ConfigureAwait(false);

        return await response
            .AsJsonAsync<YggdrasilAuthenticateResult>(cancellationToken: token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     异步向服务器发送一次刷新请求
    /// </summary>
    /// <param name="token"></param>
    /// <param name="seleectedProfile">如果需要选择角色，请填写此参数</param>
    public async Task<YggdrasilAuthenticateResult?> RefreshAsync(CancellationToken token, Profile? seleectedProfile)
    {
        ArgumentException.ThrowIfNullOrEmpty(options.AccessToken);

        var refreshData = new YggdrasilRefresh
        {
            AccessToken = options.AccessToken,
            ClientToken = options.ClientToken
        };
        if (seleectedProfile is not null) refreshData.SelectedProfile = seleectedProfile;

        var address = $"{options.YggdrasilApiLocation}/authserver/refresh";

        using var response = await HttpRequest
            .CreatePost(address)
            .WithJsonContent(refreshData)
            .WithHeaders(options.Headers ?? [])
            .SendAsync(options.GetClient.Invoke(), cancellationToken: token)
            .ConfigureAwait(false);

        return await response
            .AsJsonAsync<YggdrasilAuthenticateResult>(cancellationToken: token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     异步向服务器发送一次验证请求
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<bool> ValidateAsync(CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(options.AccessToken);

        var validateData = new YggdrasilRefresh
        {
            AccessToken = options.AccessToken,
            ClientToken = options.ClientToken
        };
        var address = $"{options.YggdrasilApiLocation}/authserver/validate";

        using var response = await HttpRequest
            .CreatePost(address)
            .WithHeaders(options.Headers ?? [])
            .WithJsonContent(validateData)
            .SendAsync(options.GetClient.Invoke(), cancellationToken: token)
            .ConfigureAwait(false);

        return response.StatusCode == HttpStatusCode.NoContent;
    }

    /// <summary>
    ///     异步向服务器发送一次注销请求
    /// </summary>
    /// <param name="token"></param>
    public async Task InvalidateAsync(CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(options.AccessToken);

        var validateData = new YggdrasilRefresh
        {
            AccessToken = options.AccessToken,
            ClientToken = options.ClientToken
        };
        var address = $"{options.YggdrasilApiLocation}/authserver/invalidate";

        using var _ = await HttpRequest
            .CreatePost(address)
            .WithHeaders(options.Headers ?? [])
            .WithJsonContent(validateData)
            .SendAsync(options.GetClient.Invoke(), cancellationToken: token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     异步向服务器发送登出请求 <br />
    ///     这会立刻注销所有会话，无论当前会话是否属于调用方
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<(bool IsSuccess, string ErrorDescription)> SignOutAsync(CancellationToken token)
    {
        // 不想写 Model 了，就这样吧（趴
        var signoutData = new JsonObject
        {
            ["username"] = options.Username,
            ["password"] = options.Password
        };
        var address = $"{options.YggdrasilApiLocation}/authserver/signout";

        using var response = await HttpRequest
            .CreatePost(address)
            .WithHeaders(options.Headers ?? [])
            .WithJsonContent(signoutData)
            .SendAsync(options.GetClient.Invoke(), cancellationToken: token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
            return (true, string.Empty);

        var content = await response.AsStringAsync(token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return (false, string.Empty);

        JsonNode? data;
        try
        {
            data = JsonCompat.ParseNode(content);
        }
        catch
        {
            // 认证服务器偶尔会返回纯文本或 HTML 错误页，保留原始响应便于上层展示和诊断。
            return (false, content);
        }

        var error = data?["errorMessage"]?.ToString() ?? string.Empty;
        return (false, error);
    }
}
