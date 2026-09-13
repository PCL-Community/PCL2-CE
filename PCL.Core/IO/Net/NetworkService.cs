using Microsoft.Extensions.DependencyInjection;
using PCL.Core.App;
using PCL.Core.App.IoC;
using PCL.Core.IO.Net.Http;
using PCL.Core.IO.Net.Http.Cache;
using PCL.Core.IO.Storage.Cache;
using PCL.Core.Logging;
using Polly;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PCL.Core.IO.Net;

[LifecycleService(LifecycleState.Loading)]
[LifecycleScope("network", "网络服务")]
public partial class NetworkService
{

    private const int LifeTime = 15;

    #region AddressDefinition

    private const string MicrosoftEntraIdServer = "https://login.microsoftonline.com/";

    private const string MojangPistonMetaServer = "https://piston-meta.mojang.com/";
    
    private const string MojangSessionServer = "https://sessionserver.mojang.com/";

    private const string CurseForgeApiServer = "https://api.curseforge.com/v1/";

    private const string ModrinthApiServer = "https://api.modrinth.com/v2/";

    private const string MinecraftServiceServer = "https://api.minecraftservices.com/";

    #endregion

    #region HttpClientName

    public const string Default = "default";

    public const string MicrosoftEntraId = "microsoft_id";

    public const string MinecraftService = "minecraft_service";

    public const string Cache = "cache";

    public const string MojangPistonMeta = "mojang_piston";

    public const string MojangSession = "mojang_session";

    public const string CurseForgeApi = "curseforge_api";

    public const string ModrinthApi = "modrinth_api";


    #endregion
    
    private static ServiceProvider? _provider;
    private static IHttpClientFactory? _factory;

    [LifecycleStart]
    private static void _Start()
    {
        
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(b => b
            .ConfigurePrimaryHttpMessageHandler(_GetSocketsHttpHandler)
            .ConfigureHttpClient(c => c.DefaultRequestHeaders
                .UserAgent.Add(new ProductInfoHeaderValue("PCL-CE", Basics.VersionName)))
            .SetHandlerLifetime(TimeSpan.FromMinutes(LifeTime)));
        
        // 默认的 HTTP Client
        
        services.AddHttpClient(Default);
        
        // CurseForge

        services.AddHttpClient(CurseForgeApi).ConfigureHttpClient(c =>
        {
            c.DefaultRequestHeaders.Add("x-api-key", Secrets.CurseForgeAPIKey);
            c.BaseAddress = new Uri(CurseForgeApiServer);
        });
        
        // Modrinth

        services.AddHttpClient(ModrinthApi).ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri(ModrinthApiServer);
        });
        
        // Microsoft Entra ID
        
        services.AddHttpClient(MicrosoftEntraId).ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri(MicrosoftEntraIdServer);
        });
        
        // Minecraft Service API

        services.AddHttpClient(MinecraftService).ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri(MinecraftServiceServer);
        });
        
        // Mojang Piston Manifest

        services.AddHttpClient(MojangPistonMeta).ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri(MojangPistonMetaServer);
        });
        
        // Mojang Session Server

        services.AddHttpClient(MojangSession).ConfigureHttpClient(c =>
        {
            c.BaseAddress = new Uri(MojangSessionServer);
        });
        
        // Cache
        services.AddHttpClient(Cache)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpCacheHandler(
            _GetSocketsHttpHandler(),CacheServiceManager.Current
            )).SetHandlerLifetime(TimeSpan.FromMinutes(LifeTime));

        _provider?.Dispose();
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IHttpClientFactory>();
    }

    [LifecycleStop]
    private static void _Stop()
    {
        _provider?.Dispose();
    }

    private static SocketsHttpHandler _GetSocketsHttpHandler() => new SocketsHttpHandler
    {
        UseProxy = true,
        AutomaticDecompression = DecompressionMethods.All,
        Proxy = HttpProxyManager.Instance,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 20,
        UseCookies = false,
        EnableMultipleHttp2Connections = true,
        ConnectCallback = Config.Network.EnableDoH ? HostConnectionHandler.Instance.GetConnectionAsync : null
    };

    /// <summary>
    /// 获取 HttpClient
    /// </summary>
    /// <param name="wantClientType">指定要求的 HttpClient 来源</param>
    /// <returns>HttpClient 实例</returns>
    public static HttpClient GetClient(string wantClientType = "default")
    {
        return _factory?.CreateClient(wantClientType) ??
               throw new InvalidOperationException("在初始化完成前的意外调用");
    }

    private const int BaseRetryDelayMs = 1000;
    private const int MaxRetryDelayMs = 30000;

    private static TimeSpan _DefaultSleepDurationProvider(int attempt)
    {
        var delayMs = Math.Pow(2, attempt - 1) * BaseRetryDelayMs;
        delayMs = Math.Min(delayMs, MaxRetryDelayMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }
    /// <summary>
    /// 获取重试策略
    /// </summary>
    /// <param name="retry">最大重试次数</param>
    /// <param name="retryPolicy">定义重试器行为</param>
    /// <returns>AsyncPolicy</returns>
    public static AsyncPolicy GetRetryPolicy(int retry = 3, Func<int, TimeSpan>? retryPolicy = null)
    {
        retryPolicy ??= _DefaultSleepDurationProvider;

        return Policy
            .Handle<HttpRequestException>()
            .WaitAndRetryAsync(
                retry,
                attempt => retryPolicy.Invoke(attempt),
                onRetry: (exception, timeSpan, retryAttempt, _) =>
                {
                    LogWrapper.Debug(
                        exception,
                        "Network",
                        $"HTTP 请求失败，正在进行第 {retryAttempt} 次重试，等待 {timeSpan.TotalMilliseconds} 毫秒。"
                        );
                });
    }

}
