using ArgonFetch.Application.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly IWebHostEnvironment _environment;

        public AppController(IMediator mediator, IWebHostEnvironment environment)
        {
            _mediator = mediator;
            _environment = environment;
        }

        [HttpGet("", Name = "GetAppInfo")]
        [ProducesResponseType(typeof(AppInfoDto), StatusCodes.Status200OK)]
        public ActionResult<AppInfoDto> GetAppInfo()
        {
            var version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (version != null)
            {
                var versionParts = version.Split('+');
                if (versionParts.Length > 0)
                {
                    version = versionParts[0];
                }
            }

            var environment = _environment.IsDevelopment() ? "Development" : "Production";

            var appInfo = new AppInfoDto
            {
                Version = !string.IsNullOrEmpty(version) ? $"v{version}" : "unknown",
                IsHealthy = true,
                Environment = environment
            };

            return Ok(appInfo);
        }
    }
}