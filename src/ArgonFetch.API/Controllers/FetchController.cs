using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FetchController : ControllerBase
    {
        private readonly IMediator _mediator;
        public FetchController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet("GetMediaType", Name = "GetMediaType")]
        [ProducesResponseType(typeof(MediaType), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<MediaType>> GetMediaType(string url)
        {
            var mediaType = await _mediator.Send(new GetMediaTypeQuery(url));
            return Ok(mediaType);
        }

        [HttpGet("GetResource", Name = "GetResource")]
        [ProducesResponseType(typeof(ResourceInformationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<ResourceInformationDto>> GetResource(string url)
        {
            var mediaType = await _mediator.Send(new GetMediaTypeQuery(url));

            if (mediaType == Application.Enums.MediaType.Media)
            {
                var result = await _mediator.Send(new GetMediaQuery(url));
                var returnDto = new ResourceInformationDto
                {
                    Type = mediaType,
                    MediaItems = new List<MediaInformationDto>
                    {
                        new MediaInformationDto
                        {
                            RequestedUrl = url,
                            StreamingUrl = result.StreamingUrl,
                            CoverUrl = result.CoverUrl,
                            Title = result.Title,
                            Author = result.Author
                        }
                    }
                };
                return Ok(returnDto);
            }
            else
            {
                var result = await _mediator.Send(new GetPlaylistQuery(url));
                var returnDto = new ResourceInformationDto
                {
                    Type = mediaType,
                    Title = result.Title,
                    Author = result.Author,
                    CoverUrl = result.ImageUrl,
                    MediaItems = result.MediaItems.Select(mid => new MediaInformationDto
                    {
                        RequestedUrl = mid.RequestedUrl,
                        StreamingUrl = mid.StreamingUrl,
                        CoverUrl = mid.CoverUrl,
                        Title = mid.Title,
                        Author = mid.Author
                    })
                };

                return Ok(returnDto);
            }
        }
    }
}
