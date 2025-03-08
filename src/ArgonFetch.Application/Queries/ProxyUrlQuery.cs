using MediatR;

namespace ArgonFetch.Application.Queries
{
    public class ProxyUrlQuery : IRequest<byte[]>
    {
        public string Url { get; set; }

        public ProxyUrlQuery(string url)
        {
            Url = url;
        }
    }

    public class ProxyUrlQueryHandler : IRequestHandler<ProxyUrlQuery, byte[]>
    {
        private readonly HttpClient _httpClient;

        public ProxyUrlQueryHandler(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<byte[]> Handle(ProxyUrlQuery request, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync(request.Url, cancellationToken);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new ApplicationException($"Failed to fetch resource from {request.Url}", ex);
            }
        }
    }
}