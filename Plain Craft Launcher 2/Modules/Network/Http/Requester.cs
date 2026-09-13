using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using Downloader;
using System.Text.Json.Nodes;
using PCL.Core.IO.Net;
using PCL.Core.IO.Net.Http;

namespace PCL.Network;

public static class Requester
{
    public static void EnsureSuccess(HttpResponseMessage? response)
    {
        if (!response?.IsSuccessStatusCode ?? true)
            throw new HttpResponseException(response);
    }

    public static async Task<string> FetchStringAsync(string url, RequestParam param = default)
    {
        return await FetchAsync(url, new FetchParam
        {
            Method = "GET",
            Accept = param.Accept,
            FallbackUrl = param.FallbackUrl,
            UseBrowserUserAgent = param.UseBrowserUserAgent,
            Timeout = param.Timeout,
            Encoding = param.Encoding,
            MakeLog = true
        }, Math.Max(1, param.Retries == 0 ? 1 : param.Retries)).ConfigureAwait(false);
    }

    public static string FetchString(string url, RequestParam param = default)
    {
        return FetchStringAsync(url, param).GetAwaiter().GetResult();
    }

    public static async Task<JsonNode> FetchJsonAsync(string url, RequestParam param = default)
    {
        return ModBase.GetJson(await FetchStringAsync(url, param).ConfigureAwait(false));
    }

    public static async Task<T> FetchJsonAsync<T>(string url, RequestParam param = default) where T : JsonNode
    {
        return (T)await FetchJsonAsync(url, param).ConfigureAwait(false);
    }

    public static JsonNode FetchJson(string url, RequestParam param = default)
    {
        return FetchJsonAsync(url, param).GetAwaiter().GetResult();
    }

    public static T FetchJson<T>(string url, RequestParam param = default) where T : JsonNode
    {
        return FetchJsonAsync<T>(url, param).GetAwaiter().GetResult();
    }

    public static async Task<string> FetchAsync(string url, FetchParam param)
    {
        return await FetchAsync(url, param, 3).ConfigureAwait(false);
    }

    public static string Fetch(string url, FetchParam param)
    {
        return FetchAsync(url, param).GetAwaiter().GetResult();
    }

    public static string Fetch(string url)
    {
        return Fetch(url, new FetchParam { Method = "GET", Timeout = 30000, MakeLog = true });
    }

    private static async Task<string> FetchAsync(string url, FetchParam param, int retries)
    {
        var urls = new[] { url, param.FallbackUrl }.Where(u => !string.IsNullOrWhiteSpace(u)).Cast<string>().ToList();
        Exception? lastException = null;
        foreach (var currentUrl in urls)
        {
            for (var attempt = 0; attempt < Math.Max(1, retries); attempt++)
            {
                try
                {
                    return await FetchOnceAsync(currentUrl, param).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }
        }

        throw lastException ?? new HttpRequestException("请求失败");
    }

    private static async Task<string> FetchOnceAsync(string url, FetchParam param)
    {
        HttpResponseMessage? response = null;
        var request = new HttpRequestMessage(ParseMethod(param.Method), RequestSigning.SecretCdnSign(url));
        RequestSigning.SecretHeadersSign(url, ref request, param.UseBrowserUserAgent);
        try
        {
            if (!string.IsNullOrWhiteSpace(param.Accept))
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(param.Accept));
            if (param.Headers is not null)
                foreach (var header in param.Headers)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (SupportBody(request.Method) && param.Content is not null)
            {
                if (param.Content is HttpContent httpContent)
                {
                    request.Content = httpContent;
                }
                else
                {
                    var content = param.Content is string text ? text : param.Content.ToString() ?? "";
                    request.Content = new StringContent(content, param.Encoding ?? Encoding.UTF8,
                        param.ContentType ?? "application/json");
                }
            }

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(param.Timeout <= 0 ? 30000 : param.Timeout);
            response = await NetworkService.GetClient().SendAsync(request, cts.Token).ConfigureAwait(false);
            EnsureSuccess(response);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        finally
        {
            if(!param.RequireContent) response?.Dispose();
            request.Dispose();
        }
    }

    public static HttpMethod ParseMethod(string? method)
    {
        return (method ?? "GET").ToUpperInvariant() switch
        {
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            "HEAD" => HttpMethod.Head,
            _ => HttpMethod.Get
        };
    }

    public static bool SupportBody(HttpMethod method)
    {
        return method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch ||
               method == HttpMethod.Delete;
    }

    public static int Ping(string ip)
    {
        try
        {
            var result = new Ping().Send(ip);
            return result.Status == IPStatus.Success ? (int)result.RoundtripTime : -1;
        }
        catch
        {
            return -1;
        }
    }
}
