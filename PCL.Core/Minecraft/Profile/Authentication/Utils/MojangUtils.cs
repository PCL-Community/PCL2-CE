using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net.Http;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.Profile.Authentication.Utils.Models;
using PCL.Core.Minecraft.Profile.Models;

namespace PCL.Core.Minecraft.Profile.Authentication.Utils;

public static class MojangUtils
{
    #region "API 定义"

    private const string MojangAuthenticateEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string LicenseEndpoint = "https://api.minecraftservices.com/entitlements/mcstore";
    private const string ProfileEndpont = "https://api.minecraftservices.com/minecraft/profile";

    #endregion

    /// <summary>
    /// 发送一次登录请求
    /// </summary>
    /// <param name="xstsToken">XSTS 令牌</param>
    /// <param name="userHash">Xbox User Hash</param>
    /// <returns><see cref="MojangResponse"/></returns>
    public static async Task<MojangResponse?> AuthenticateAsync(string xstsToken, string userHash, CancellationToken token)
    {
        using var response = await HttpRequest.CreatePost(MojangAuthenticateEndpoint)
            .WithContent(new StringContent(
                JsonSerializer.Serialize(new MojangIdentityToken
                {
                    IdentityToken = $"XBL3.0 x={userHash};{xstsToken}"
                }
                ), Encoding.UTF8, "application/json")
            ).SendAsync(cancellationToken: token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.AsJsonAsync<MojangResponse>(cancellationToken: token).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查玩家是否持有许可
    /// </summary>
    /// <param name="profile"><see cref="SafeProfile"/></param>
    /// <returns>如果没有有效许可，返回 false</returns>
    public static async Task<bool> CheckLicenseAsync(SafeProfile profile, CancellationToken token)
    {
        using var response = await HttpRequest.Create(LicenseEndpoint)
            .WithAuthentication(profile.TokenType, profile.AccessToken)
            .SendAsync(cancellationToken: token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.AsJsonAsync<JsonObject>(cancellationToken: token).ConfigureAwait(false);
        return result?["items"] is JsonArray items && items.Any(item =>
            item?["name"]?.ToString() is "product_minecraft" or "game_minecraft");
    }
    /// <summary>
    /// 获取玩家的完整档案
    /// </summary>
    /// <returns>档案信息</returns>
    public static async Task<MojangProfile> GetProfileAsync(SafeProfile profile, CancellationToken token)
    {
        using var response = await HttpRequest.Create(ProfileEndpont)
            .WithAuthentication(profile.TokenType, profile.AccessToken)
            .SendAsync(cancellationToken: token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new IdentityModelAuthenticationException("profile_not_created",
                "The Microsoft account has not created a Minecraft Java profile yet.");
        response.EnsureSuccessStatusCode();
        return await response.AsJsonAsync<MojangProfile>(cancellationToken: token).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Minecraft profile response was empty.");
    }
}
