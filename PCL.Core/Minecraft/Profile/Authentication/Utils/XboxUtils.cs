using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.IO.Net.Http;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.Profile.Authentication.Utils.Models;

namespace PCL.Core.Minecraft.Profile.Authentication.Utils;

public static class XboxUtils
{
    private const string XboxLiveAuthServer = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsAuthorizeServer = "https://xsts.auth.xboxlive.com/xsts/authorize";
    public static async Task<XboxLiveResponse?> AuthenticateAsync<T>(XboxAuthenticate<T> authData, bool isXsts,
        CancellationToken token)
    {
        using var response = await HttpRequest.CreatePost(
                isXsts ? XstsAuthorizeServer:XboxLiveAuthServer
                ).WithJsonContent(authData).SendAsync(cancellationToken: token)
            .ConfigureAwait(false);
        if (!response.IsSuccess && isXsts)
        {
            var content = await response.AsStringAsync(token).ConfigureAwait(false);
            try
            {
                var error = JsonNode.Parse(content);
                if (long.TryParse(error?["XErr"]?.ToString(), out var xErr))
                    throw new IdentityModelAuthenticationException($"xsts_{xErr}",
                        error?["Message"]?.ToString() ?? $"XSTS authorization failed with XErr {xErr}.");
            }
            catch (JsonException)
            {
            }
        }
        response.EnsureSuccessStatusCode();
        return await response.AsJsonAsync<XboxLiveResponse>(cancellationToken: token);
    }
}
