using ArgonFetch.Application.Models;
using ArgonFetch.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly IMediator _mediator;

        public ProxyController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet("Url", Name = "ProxyUrl")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<string>> ProxyUrl(string url)
        {
            var data = await _mediator.Send(new ProxyUrlQuery(url));
            return Content(System.Text.Encoding.UTF8.GetString(data), "text/plain");
        }

        [HttpGet("Head", Name = "ProxyHead")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ProxyHeadResponse))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ProxyHeadResponse>> ProxyHead(string url)
        {
            var response = await _mediator.Send(new ProxyHeadQuery(url));

            if (!response.IsSuccess)
                return BadRequest(response.ErrorMessage);

            return response;
        }

        [HttpGet("Range", Name = "ProxyRange")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status416RequestedRangeNotSatisfiable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> ProxyRange(string url, int start, int end)
        {
            var response = await _mediator.Send(new ProxyRangeQuery(url, start, end));
            if (!response.IsSuccess)
                return BadRequest(response.ErrorMessage);

            return File(response.Data, "application/octet-stream");
        }
    }
}