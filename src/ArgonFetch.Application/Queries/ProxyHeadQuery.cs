using ArgonFetch.Application.Models;
using MediatR;

namespace ArgonFetch.Application.Queries
{
    public class ProxyHeadQuery : IRequest<ProxyHeadResponse>
    {
        public string Url { get; set; }

        public ProxyHeadQuery(string url)
        {
            Url = url;
        }
    }

    public class ProxyHeadQueryHandler : IRequestHandler<ProxyHeadQuery, ProxyHeadResponse>
    {
        private readonly HttpClient _httpClient;

        public ProxyHeadQueryHandler(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ProxyHeadResponse> Handle(ProxyHeadQuery request, CancellationToken cancellationToken)
        {
            try
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Head, request.Url);
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.EnsureSuccessStatusCode();

                var headResponse = new ProxyHeadResponse
                {
                    IsSuccess = true,
                    Headers = new Dictionary<string, IEnumerable<string>>(),
                    ContentLength = response.Content.Headers.ContentLength
                };

                foreach (var header in response.Headers)
                {
                    headResponse.Headers[header.Key] = header.Value;
                }

                foreach (var header in response.Content.Headers)
                {
                    headResponse.Headers[header.Key] = header.Value;
                }

                return headResponse;
            }
            catch (HttpRequestException ex)
            {
                return new ProxyHeadResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Failed to fetch resource from {request.Url}: {ex.Message}"
                };
            }
        }
    }
}