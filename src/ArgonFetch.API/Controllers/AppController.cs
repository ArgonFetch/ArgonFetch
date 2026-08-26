using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly IWebHostEnvironment _environment;
        private readonly IApplicationInfoService _applicationInfoService;
        private readonly IMaintenanceState _maintenance;

        public AppController(IMediator mediator, IWebHostEnvironment environment, IApplicationInfoService applicationInfoService, IMaintenanceState maintenance)
        {
            _mediator = mediator;
            _environment = environment;
            _applicationInfoService = applicationInfoService;
            _maintenance = maintenance;
        }

        [HttpGet("", Name = "GetAppInfo")]
        [ProducesResponseType(typeof(AppInfoDto), StatusCodes.Status200OK)]
        public ActionResult<AppInfoDto> GetAppInfo()
        {
            var version = _applicationInfoService.GetVersion();
            var environment = _environment.IsDevelopment() ? "Development" : "Production";

            var appInfo = new AppInfoDto
            {
                Version = !string.IsNullOrEmpty(version) && version != "unknown" ? $"v{version}" : "unknown",
                IsHealthy = true,
                Environment = environment,
                Maintenance = _maintenance.Activity
            };

            return Ok(appInfo);
        }
    }
}

