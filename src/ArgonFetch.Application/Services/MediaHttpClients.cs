using System.Collections.Concurrent;
using System.Net;

namespace ArgonFetch.Application.Services
{
    public interface IMediaHttpClients
    {
        HttpClient For(string? proxy);
    }

    /// <summary>
    /// Hands out the HTTP client a media URL has to be fetched with.
    /// <para>
    /// Media URLs are signed for the IP that requested them, so one extracted through a proxy
    /// is only downloadable through that same proxy - fetching it directly answers 403. Each
    /// proxy therefore gets its own client, kept for the process lifetime because a handler per
    /// request exhausts sockets.
    /// </para>
    /// </summary>
    public class MediaHttpClients : IMediaHttpClients
    {
        private readonly ConcurrentDictionary<string, HttpClient> _proxiedClients = new();
        private readonly IHttpClientFactory _httpClientFactory;

        public MediaHttpClients(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public HttpClient For(string? proxy)
        {
            if (string.IsNullOrWhiteSpace(proxy))
                return _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

            return _proxiedClients.GetOrAdd(proxy, CreateProxiedClient);
        }

        public static string Describe(string? proxy)
        {
            if (string.IsNullOrWhiteSpace(proxy))
                return "no proxy";

            return Uri.TryCreate(proxy, UriKind.Absolute, out var uri)
                ? $"{uri.Host}:{uri.Port}"
                : "proxy";
        }

        private static HttpClient CreateProxiedClient(string proxy)
        {
            var handler = new SocketsHttpHandler
            {
                Proxy = BuildWebProxy(proxy),
                UseProxy = true,
            };

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(MediaHttpClientDefaults.UserAgent);

            return client;
        }

        private static WebProxy BuildWebProxy(string proxy)
        {
            var uri = new Uri(proxy);
            var webProxy = new WebProxy(uri.GetLeftPart(UriPartial.Authority).Replace(uri.UserInfo + "@", string.Empty));

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                webProxy.Credentials = new NetworkCredential(
                    Uri.UnescapeDataString(parts[0]),
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
            }

            return webProxy;
        }
    }
}
