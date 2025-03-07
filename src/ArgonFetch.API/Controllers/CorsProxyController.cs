using ArgonFetch.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CorsProxyController : ControllerBase
    {
        private readonly IMediator _mediator;
        public CorsProxyController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet("ProxyUrl", Name = "ProxyUrl")]
        public async Task<ActionResult<string>> ProxyUrl(string url)
        {
            return await _mediator.Send(new ProxyResourceQuery(url));
        }
    }
}
