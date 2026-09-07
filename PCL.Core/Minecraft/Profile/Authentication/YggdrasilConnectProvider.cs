using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net;
using PCL.Core.IO.Net.Http;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.IdentityModel.Extensions.JsonWebToken;
using PCL.Core.Minecraft.IdentityModel.Extensions.YggdrasilConnect;
using PCL.Core.Minecraft.IdentityModel.OAuth;
using PCL.Core.Minecraft.IdentityModel.Yggdrasil;
using PCL.Core.Minecraft.Profile.Models;
using YggdrasilProfile = PCL.Core.Minecraft.IdentityModel.Yggdrasil.Profile;

namespace PCL.Core.Minecraft.Profile.Authentication;

public sealed class YggdrasilConnectProvider : IAuthenticateProvider
{
    private static readonly IReadOnlyDictionary<string, string> _BuiltInClientIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["littleskin.cn"] = "1514"
        };

    private static readonly string[] _Scopes =
    [
        "openid",
        "offline_access",
        "Yggdrasil.PlayerProfiles.Select",
        "Yggdrasil.Server.Join"
    ];

    private readonly YggdrasilOptions _options;
    private readonly string? _yggdrasilServer;
    private YggdrasilClient? _client;

    public YggdrasilConnectProvider(string discoveryAddress, string? clientId = null, string? yggdrasilServer = null)
    {
        var resolvedClientId = clientId;
        if (string.IsNullOrWhiteSpace(resolvedClientId) &&
            Uri.TryCreate(yggdrasilServer, UriKind.Absolute, out var serverUri) &&
            TryGetBuiltInClientId(serverUri.Host, out var builtInClientId))
            resolvedClientId = builtInClientId;

        _options = new YggdrasilOptions
        {
            OpenIdDiscoveryAddress = discoveryAddress,
            ClientId = resolvedClientId?.Trim() ?? string.Empty,
            OnlyDeviceAuthorize = true,
            GetClient = () => NetworkService.GetClient()
        };
        _yggdrasilServer = yggdrasilServer;
    }

    public static bool TryGetBuiltInClientId(string host, out string clientId)
    {
        var normalizedHost = host.Trim().TrimEnd('.');
        return _BuiltInClientIds.TryGetValue(normalizedHost, out clientId!);
    }

    public Task InitializeAsync(CancellationToken token)
    {
        _client = new YggdrasilClient(_options);
        return _client.InitializeAsync(token);
    }

    public Task<DeviceCodeData?> GetCodePairAsync(CancellationToken token)
    {
        _EnsureInitialized();
        return _client!.GetCodePairAsync(_Scopes, token);
    }

    public Task<AuthorizeResult?> PollDeviceCodeAsync(DeviceCodeData data, CancellationToken token)
    {
        _EnsureInitialized();
        return _client!.AuthorizeWithDeviceAsync(data, token);
    }

    public async Task<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken token)
    {
        _EnsureInitialized();
        var oauth = request.OAuthResult;
        var isSilentRefresh = false;
        if (oauth is null && !string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            isSilentRefresh = true;
            oauth = await _client!.AuthorizeWithSilentAsync(new AuthorizeResult { RefreshToken = request.RefreshToken }, token)
                .ConfigureAwait(false);
        }
        if (oauth is null && request.DeviceCodeHandler is not null)
        {
            var code = await GetCodePairAsync(token).ConfigureAwait(false)
                       ?? throw new IdentityModelAuthenticationException("device_authorization_failed", "The server returned no device code.");
            oauth = await request.DeviceCodeHandler(
                new DeviceCodeAuthenticationContext(code, pollToken => PollDeviceCodeAsync(code, pollToken)), token)
                .ConfigureAwait(false);
        }
        if (oauth is null)
            throw new IdentityModelConfigurationException("Yggdrasil Connect login requires an OAuth result or device-code handler.");
        return await _CompleteAsync(oauth, request, isSilentRefresh, token).ConfigureAwait(false);
    }

    public Task<AuthenticationResult> RefreshAsync(McProfile profile, CancellationToken token)
        => AuthenticateAsync(new AuthenticationRequest
        {
            RefreshToken = profile.RefreshToken,
            IdToken = profile.IdToken,
            ExistingProfile = profile
        }, token);

    public async Task<bool> ValidateAsync(McProfile profile, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(profile.AccessToken)) return false;
        _EnsureInitialized();
        // 协议要求：使用访问令牌请求用户信息端点以验证其有效性
        // （正常返回用户信息 = 有效；invalid_token 错误 = 无效）
        using var response = await HttpRequest.Create(_options.Meta!.UserInfoEndpoint)
            .WithBearerToken(profile.AccessToken)
            .SendAsync(NetworkService.GetClient(), cancellationToken: token)
            .ConfigureAwait(false);
        return response.IsSuccess;
    }

    private async Task<AuthenticationResult> _CompleteAsync(AuthorizeResult oauth, AuthenticationRequest request,
        bool isSilentRefresh, CancellationToken token)
    {
        if (oauth.IsError) throw new IdentityModelAuthenticationException(oauth.Error, oauth.ErrorDescription);
        oauth.Validate(requireIdToken: true, requireRefreshToken: !isSilentRefresh);

        var claims = await _ReadIdTokenAsync(oauth.IdToken ?? request.IdToken, token).ConfigureAwait(false);
        var profile = claims.SelectedProfile;
        var available = claims.AvailableProfiles ?? [];
        if (profile is null || available.Length == 0)
        {
            var userInfo = await _GetUserInfoAsync(oauth.AccessToken!, token).ConfigureAwait(false);
            profile ??= userInfo?.SelectedProfile;
            if (available.Length == 0) available = userInfo?.AvailableProfiles ?? [];
        }
        if (profile is null && !request.ForceReselectProfile &&
            request.ExistingProfile is { Uuid: { } existingUuid } && !string.IsNullOrWhiteSpace(existingUuid))
        {
            profile = available.FirstOrDefault(p =>
                string.Equals(p.Id, existingUuid, StringComparison.Ordinal));
        }
        if (profile is null && available.Length == 0 && request.ExistingProfile is { } existing &&
            !string.IsNullOrWhiteSpace(existing.Uuid) && !string.IsNullOrWhiteSpace(existing.UserName))
        {
            profile = new YggdrasilProfile { Id = existing.Uuid, Name = existing.UserName };
        }
        if ((request.ForceReselectProfile || profile is null) && available.Length > 1)
        {
            if (request.ProfileSelector is null)
                throw new IdentityModelAuthenticationException("profile_selection_required", "Yggdrasil Connect returned multiple player profiles.");
            var candidates = available.Select(p => new AuthenticationCandidate(p.Id, p.Name)).ToArray();
            var selected = await request.ProfileSelector(candidates, token).ConfigureAwait(false);
            if (selected is null)
                throw new IdentityModelAuthenticationException("access_denied", "Yggdrasil Connect profile selection was cancelled.");
            profile = available.FirstOrDefault(p => p.Id == selected.Id);
        }
        profile ??= available.FirstOrDefault();
        if (profile is null)
            throw new IdentityModelAuthenticationException("invalid_profile", "Yggdrasil Connect returned no player profile.");

        return new AuthenticationResult
        {
            ProfileType = ProfileType.YggdrasilConnect,
            UserName = profile.Name,
            Uuid = profile.Id,
            AccessToken = oauth.AccessToken!,
            RefreshToken = oauth.RefreshToken ?? request.RefreshToken!,
            ClientToken = profile.Id,
            TokenType = oauth.TokenType!,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(oauth.ExpiresIn!.Value),
            Provider = "yggdrasil-connect",
            Server = _GetServerAddress(),
            ServerName = await _GetServerNameAsync(token).ConfigureAwait(false),
            DiscoveryAddress = _options.OpenIdDiscoveryAddress,
            IdToken = oauth.IdToken ?? request.IdToken
        };
    }

    private async Task<YggdrasilConnectClaims> _ReadIdTokenAsync(string? idToken, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new SecurityException("Yggdrasil Connect did not return an ID token.");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
        if (string.IsNullOrWhiteSpace(jwt.Header.Kid))
            throw new SecurityException("Yggdrasil Connect ID token has no key id.");
        var key = await _options.GetSignatureKeyAsync(jwt.Header.Kid, token).ConfigureAwait(false);
        var verifiedToken = new JsonWebToken(idToken, _options.Meta!);
        var verified = verifiedToken.VerifySignature(key, _options.ClientId);
        if (verified is null) throw new SecurityException("Yggdrasil Connect ID token validation failed.");
        return verifiedToken.ReadTokenPayload<YggdrasilConnectClaims>()
               ?? throw new SecurityException("Yggdrasil Connect ID token payload was empty.");
    }

    private async Task<YggdrasilConnectClaims?> _GetUserInfoAsync(string accessToken, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_options.Meta?.UserInfoEndpoint)) return null;
        using var response = await HttpRequest.Create(_options.Meta!.UserInfoEndpoint)
            .WithBearerToken(accessToken)
            .SendAsync(NetworkService.GetClient(), cancellationToken: token)
            .ConfigureAwait(false);
        if (!response.IsSuccess) return null;
        try
        {
            return await response.AsJsonAsync<YggdrasilConnectClaims>(cancellationToken: token).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            // 用户信息端点返回的声明不完整时按“未提供用户信息”处理，交由后续回退逻辑
            return null;
        }
    }

    private async Task<string?> _GetServerNameAsync(CancellationToken token)
    {
        try
        {
            var root = _GetServerAddress().TrimEnd('/');
            if (root.EndsWith("/authserver", StringComparison.OrdinalIgnoreCase))
                root = root[..^"/authserver".Length];
            using var response = await HttpRequest.Create(root)
                .SendAsync(NetworkService.GetClient(), cancellationToken: token)
                .ConfigureAwait(false);
            if (!response.IsSuccess) return null;
            var metadata = await response.AsJsonAsync<JsonObject>(cancellationToken: token).ConfigureAwait(false);
            return metadata?["meta"]?["serverName"]?.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private void _EnsureInitialized()
    {
        if (_client is null || _options.Meta is null)
            throw new IdentityModelConfigurationException("Please initialize Yggdrasil Connect before use.");
    }

    private string _GetServerAddress()
    {
        if (!string.IsNullOrWhiteSpace(_yggdrasilServer))
        {
            var server = _yggdrasilServer.TrimEnd('/');
            return server.EndsWith("/authserver", StringComparison.OrdinalIgnoreCase) ? server : server + "/authserver";
        }
        var value = _options.OpenIdDiscoveryAddress;
        var marker = value.IndexOf("/.well-known/", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? value[..marker].TrimEnd('/') : value.TrimEnd('/');
    }

    // 协议要求：ID 令牌必须包含 iss / sub / aud / iat / exp 声明（iss/iat/exp 由签名验证流程校验）；
    // 用户信息端点响应为 ID 令牌声明的超集，同样必须包含 sub / aud
    private sealed record YggdrasilConnectClaims
    {
        [JsonPropertyName("sub")] public required string? Subject { get; init; }
        [JsonPropertyName("aud")] public required string? Audience { get; init; }
        [JsonPropertyName("selectedProfile")] public YggdrasilProfile? SelectedProfile { get; init; }
        [JsonPropertyName("availableProfiles")] public YggdrasilProfile[]? AvailableProfiles { get; init; }
    }
}
