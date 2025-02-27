using MediatR;

namespace ArgonFetch.Application.Queries
{
    public class ProxyResourceQuery : IRequest<string>
    {
        public string Url { get; set; }

        public ProxyResourceQuery(string url)
        {
            Url = url;
        }
    }

    public class ProxyResourceQueryHandler : IRequestHandler<ProxyResourceQuery, string>
    {
        public async Task<string> Handle(ProxyResourceQuery request, CancellationToken cancellationToken)
        {
            return string.Empty;
        }
    }
}
