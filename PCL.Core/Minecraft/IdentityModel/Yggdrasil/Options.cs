using System;
using System.Collections.Generic;
using System.Net.Http;

namespace PCL.Core.Minecraft.IdentityModel.Yggdrasil;

public record YggdrasilLegacyAuthenticateOptions
{
    /// <summary>
    /// API 基地址 (e.g. https://api.example.com/api/yggdrasil)
    /// </summary>
    public required string YggdrasilApiLocation { get; set; }
    /// <summary>
    /// 用户名
    /// </summary>
    public string? Username { get; set; }
    /// <summary>
    /// 密码
    /// </summary>
    public string? Password { get; set; }
    /// <summary>
    /// 访问令牌
    /// </summary>
    public string? AccessToken { get; set; }
    public string? ClientToken { get; set; }
    public required Func<HttpClient> GetClient { get; set; }
    /// <summary>
    /// 请求头
    /// </summary>
    public Dictionary<string,string>? Headers { get; set; }
}
