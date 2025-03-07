using ArgonFetch.Application.Models;
using MediatR;

namespace ArgonFetch.Application.Queries
{
    public class ProxyRangeQuery : IRequest<ProxyRangeResponse>
    {
        public string Url { get; set; }
        public int Start { get; set; }
        public int End { get; set; }

        public ProxyRangeQuery(string url, int start, int end)
        {
            Url = url;
            Start = start;
            End = end;
        }
    }

    public class ProxyRangeQueryHandler : IRequestHandler<ProxyRangeQuery, ProxyRangeResponse>
    {
        private readonly HttpClient _httpClient;

        public ProxyRangeQueryHandler(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ProxyRangeResponse> Handle(ProxyRangeQuery request, CancellationToken cancellationToken)
        {
            try
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.Url);
                httpRequest.Headers.Add("Range", $"bytes={request.Start}-{request.End}");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                response.EnsureSuccessStatusCode();

                return new ProxyRangeResponse
                {
                    Data = await response.Content.ReadAsByteArrayAsync(cancellationToken),
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                return new ProxyRangeResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Failed to fetch range from {request.Url}: {ex.Message}"
                };
            }
        }
    }
}