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
        private readonly IRequestCounterService _requestCounter;
        private readonly IMaintenanceState _maintenance;

        public AppController(IMediator mediator, IWebHostEnvironment environment, IApplicationInfoService applicationInfoService, IRequestCounterService requestCounter, IMaintenanceState maintenance)
        {
            _mediator = mediator;
            _environment = environment;
            _applicationInfoService = applicationInfoService;
            _requestCounter = requestCounter;
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
    
        /// <summary>
        /// Total media requests this installation has served. Shaped for shields.io so it can
        /// be embedded as a badge without another service in between.
        /// </summary>
        [HttpGet("requests", Name = "GetRequestCount")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> GetRequestCount(CancellationToken cancellationToken)
        {
            var total = await _requestCounter.GetTotalAsync(cancellationToken);

            return Ok(new
            {
                schemaVersion = 1,
                label = "requests",
                message = total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                color = "9f54e5",
                total
            });
        }
    }
}
