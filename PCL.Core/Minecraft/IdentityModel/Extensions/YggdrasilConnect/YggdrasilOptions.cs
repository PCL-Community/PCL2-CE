using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.IdentityModel.Extensions.OpenId;
using PCL.Core.Minecraft.IdentityModel.OAuth;
using PCL.Core.Utils.Exts;
using PCL.Core.IO.Net.Http;

namespace PCL.Core.Minecraft.IdentityModel.Extensions.YggdrasilConnect;

public record YggdrasilOptions:OpenIdOptions
{
    private readonly string[] _scopesRequired =
    [
        "openid",
        "Yggdrasil.PlayerProfiles.Select",
        "Yggdrasil.Server.Join"
    ];

    // 重写这个鬼方法是因为 Yggdrasil Connect 有要求（

    /// <summary>
    /// 拉取 Yggdrasil 配置
    /// </summary>
    /// <param name="token"></param>
    /// <exception cref="IdentityModelConfigurationException">无法加载元数据或缺少必要 scope</exception>
    public override async Task InitializeAsync(CancellationToken token)
    {
        if (!Uri.TryCreate(OpenIdDiscoveryAddress, UriKind.Absolute, out var discoveryUri) || discoveryUri.Scheme != Uri.UriSchemeHttps)
            throw new IdentityModelConfigurationException("Yggdrasil Connect discovery address must use HTTPS.");
        using var response = await HttpRequest
            .Create(OpenIdDiscoveryAddress)
            .WithHeaders(Headers ?? [])
            .SendAsync(GetClient.Invoke(), cancellationToken: token)
            .ConfigureAwait(false);

        var metadata = (await response.AsJsonAsync<YggdrasilConnectMetaData>(cancellationToken: token).ConfigureAwait(false))
            ?? throw new IdentityModelConfigurationException("无法加载 Yggdrasil Connect 元数据");
        Meta = metadata;

        _ValidateMetadata(discoveryUri, metadata);

        var missingScopes = _scopesRequired.Except(Meta.ScopesSupported).ToArray();
        if (missingScopes.Length > 0)
            throw new IdentityModelConfigurationException($"Yggdrasil Connect 元数据缺少必要 scope：{string.Join(", ", missingScopes)}");
    }
    /// <summary>
    /// 构建 OAuth 客户端选项
    /// </summary>
    /// <returns><see cerf="OAuthClientOptions"> OAuth 客户端选项</returns>
    /// <exception cref="IdentityModelConfigurationException">未调用 <see cref="InitializeAsync"/> 或缺少必要的客户端配置</exception>
    public override OAuthClientOptions BuildOAuthOptions()
    {
        if (Meta is YggdrasilConnectMetaData meta)
        {
            if (ClientId.IsNullOrEmpty() && !meta.SharedClientId.IsNullOrEmpty())
                ClientId = meta.SharedClientId;
            var options = base.BuildOAuthOptions();
            if (options.ClientId.IsNullOrEmpty())
                throw new IdentityModelConfigurationException("Yggdrasil Connect 需要设置 ClientId，或由元数据提供 sharedClient_id");
            return options;
        }

        throw new IdentityModelConfigurationException("请先调用 InitializeAsync() 加载 Yggdrasil Connect 元数据");
    }

    private static void _ValidateMetadata(Uri discoveryUri, YggdrasilConnectMetaData metadata)
    {
        if (!Uri.TryCreate(metadata.Issuer, UriKind.Absolute, out var issuerUri) || issuerUri.Scheme != Uri.UriSchemeHttps)
            throw new IdentityModelConfigurationException("Yggdrasil Connect issuer must use HTTPS.");
        if (!string.Equals(discoveryUri.GetLeftPart(UriPartial.Authority), issuerUri.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
            throw new IdentityModelConfigurationException("Yggdrasil Connect discovery address must belong to the issuer.");

        if (!string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint))
            _ValidateHttpsEndpoint(metadata.AuthorizationEndpoint, "authorization");
        _ValidateHttpsEndpoint(metadata.TokenEndpoint, "token");
        _ValidateHttpsEndpoint(metadata.DeviceAuthorizationEndpoint, "device authorization");
        _ValidateHttpsEndpoint(metadata.JwksUri, "JWKS");
        if (!string.IsNullOrWhiteSpace(metadata.UserInfoEndpoint))
            _ValidateHttpsEndpoint(metadata.UserInfoEndpoint, "userinfo");
    }

    private static void _ValidateHttpsEndpoint(string? value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new IdentityModelConfigurationException($"Yggdrasil Connect {name} endpoint must use HTTPS.");
    }
}
