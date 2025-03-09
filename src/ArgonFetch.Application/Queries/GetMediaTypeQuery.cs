using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;
using MediatR;

namespace ArgonFetch.Application.Queries
{
    public class GetMediaTypeQuery : IRequest<MediaType>
    {
        public GetMediaTypeQuery(string query)
        {
            Query = query;
        }

        public string Query { get; set; }
    }

    public class GetMediaTypeQueryHandler : IRequestHandler<GetMediaTypeQuery, MediaType>
    {
        public async Task<MediaType> Handle(GetMediaTypeQuery request, CancellationToken cancellationToken)
        {
            var contentType = await MediaContentIdentifierService.IdentifyContent(request.Query);

            if (new[] { ContentType.Media, ContentType.Url, ContentType.SearchTerm }.Contains(contentType))
                return MediaType.Media;

            if (contentType == ContentType.Unknown)
                return MediaType.Unknown;

            return MediaType.PlayList;
        }
    }
}
