using ArgonFetch.Application.Behaviors;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services.DDLFetcherServices;
using ArgonFetch.Application.Validators;
using FluentValidation;
using FluentValidation.AspNetCore;
using MediatR;
using System.Text.Json.Serialization;
using YoutubeDLSharp;

var builder = WebApplication.CreateBuilder(args);

// Load .env file if it exists (for local development)
if (File.Exists(".env"))
{
    foreach (var line in File.ReadAllLines(".env"))
    {
        var trimmedLine = line.Trim();
        if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
            continue;

        var parts = trimmedLine.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}

#region Configure Services
// Add services to the container.
// Enums are serialized by name so the wire format stays stable when members are
// reordered, and clients don't have to mirror the numeric values.
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSpaStaticFiles(spaStaticFilesOptions => { spaStaticFilesOptions.RootPath = "wwwroot/browser"; });

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetMediaQuery).Assembly));

// Add HttpClient for TikTokDllFetcherService
builder.Services.AddHttpClient<TikTokDllFetcherService>();

// Register the IDllFetcher implementations
builder.Services.AddScoped<TikTokDllFetcherService>();

// Register In memory caching
builder.Services.AddMemoryCache();

// yt-dlp is fetched when the app boots and kept current from there, so the image ships
// without it and a restart is enough to pick up an extractor fix.
builder.Services.AddSingleton<ArgonFetch.Application.Services.IToolPaths>(
    new ArgonFetch.Application.Services.ToolPaths(
        builder.Configuration["TOOLS_PATH"],
        builder.Configuration["COOKIES_PATH"]));
builder.Services.AddSingleton<ArgonFetch.Infrastructure.Services.MediaToolsService>();
builder.Services.AddHostedService(
    sp => sp.GetRequiredService<ArgonFetch.Infrastructure.Services.MediaToolsService>());

// Register Application Info Service
builder.Services.AddSingleton<ArgonFetch.Application.Services.IApplicationInfoService, ArgonFetch.Infrastructure.Services.ApplicationInfoService>();
#endregion

#region API Documentation
// Register Swagger services
builder.Services.AddSwaggerGen();
builder.Services.AddEndpointsApiExplorer();
#endregion

#region External Services Configuration
// Spotify track details are read from the public track page, so no credentials
// and no API client are needed.
builder.Services.AddScoped<ArgonFetch.Application.Services.ISpotifyMetadataService,
                           ArgonFetch.Application.Services.SpotifyMetadataService>();

// Reads long playlists through the web player's own API. Singleton so the anonymous session
// it mints is held across requests rather than re-minted for each one.
builder.Services.AddSingleton<ArgonFetch.Application.Services.ISpotifyWebPlayerClient,
                              ArgonFetch.Application.Services.SpotifyWebPlayerClient>();

// Register YoutubeMusicAPI and YoutubeDL
// Search and metadata only. Its streaming endpoints need a PoToken and answer 403 to IPs
// that look like VPNs, which is exactly what a rotated proxy looks like; yt-dlp fetches the
// media instead, as it always has.
builder.Services.AddSingleton<YouTubeMusicAPI.Client.YouTubeMusicClient>();
builder.Services.AddScoped(sp =>
{
    var toolPaths = sp.GetRequiredService<ArgonFetch.Application.Services.IToolPaths>();

    return new YoutubeDL
    {
        YoutubeDLPath = toolPaths.YtDlpPath,
        FFmpegPath = toolPaths.FfmpegPath
    };
});

// Register FFmpeg and streaming services
builder.Services.AddHttpClient();

// Client used for all upstream media fetches; carries the User-Agent that avoids 403s.
builder.Services.AddHttpClient(
    ArgonFetch.Application.Services.MediaHttpClientDefaults.ClientName,
    client => client.DefaultRequestHeaders.UserAgent.ParseAdd(
        ArgonFetch.Application.Services.MediaHttpClientDefaults.UserAgent));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ArgonFetch.Application.Interfaces.IFfmpegStreamingService, ArgonFetch.Infrastructure.Services.FfmpegStreamingService>();
builder.Services.AddScoped<ArgonFetch.Application.Interfaces.IAcceleratedDownloadService, ArgonFetch.Infrastructure.Services.AcceleratedDownloadService>();
builder.Services.AddScoped<ArgonFetch.Application.Services.ICombinedStreamUrlBuilder, ArgonFetch.Application.Services.CombinedStreamUrlBuilder>();
builder.Services.AddScoped<ArgonFetch.Application.Services.IProxyUrlBuilder, ArgonFetch.Application.Services.ProxyUrlBuilder>();
builder.Services.AddSingleton<ArgonFetch.Application.Services.IMediaUrlCacheService, ArgonFetch.Application.Services.MediaUrlCacheService>();
builder.Services.AddSingleton<ArgonFetch.Application.Services.IMediaHttpClients, ArgonFetch.Application.Services.MediaHttpClients>();

// Lets the app tell clients it is briefly busy (updating yt-dlp) instead of failing fetches.
builder.Services.AddSingleton<ArgonFetch.Application.Services.IMaintenanceState, ArgonFetch.Application.Services.MaintenanceState>();
// Singleton so an archive being built by one request can be read by the request watching it.
builder.Services.AddSingleton<ArgonFetch.Application.Services.IArchiveProgressTracker, ArgonFetch.Application.Services.ArchiveProgressTracker>();

// Optional proxy list (one proxy per line) rotated across yt-dlp fetches; no file means direct fetches.
builder.Services.AddSingleton<ArgonFetch.Application.Services.IProxyPool>(sp =>
    new ArgonFetch.Application.Services.ProxyPool(
        ArgonFetch.Application.Services.ProxyPool.ReadList(builder.Configuration["PROXY_LIST_PATH"])));
#endregion

#region Validation
// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
#region Plugins
// Read as desired state: what the configuration lists is installed, what it does not is removed.
var pluginOptions = builder.Configuration
    .GetSection(ArgonFetch.Application.Plugins.PluginOptions.SectionName)
    .Get<ArgonFetch.Application.Plugins.PluginOptions>() ?? new ArgonFetch.Application.Plugins.PluginOptions();

builder.Services.AddSingleton(pluginOptions);
builder.Services.AddSingleton<ArgonFetch.Application.Plugins.PluginInstaller>();
builder.Services.AddSingleton<ArgonFetch.Application.Plugins.PluginLoader>();
builder.Services.AddSingleton<ArgonFetch.Application.Plugins.IProviderContextFactory,
    ArgonFetch.Application.Plugins.ProviderContextFactory>();

// Loaded once. Providers are asked on every request, so building them per request would pay
// reflection over and over for an answer that cannot change while the process is running.
builder.Services.AddSingleton<ArgonFetch.Application.Plugins.IProviderRegistry>(serviceProvider =>
{
    var loader = serviceProvider.GetRequiredService<ArgonFetch.Application.Plugins.PluginLoader>();
    var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
    var root = Path.IsPathRooted(pluginOptions.Path)
        ? pluginOptions.Path
        : Path.Combine(environment.ContentRootPath, pluginOptions.Path);

    return new ArgonFetch.Application.Plugins.ProviderRegistry(
        loader.Load(root, pluginOptions.Install),
        serviceProvider.GetRequiredService<ILogger<ArgonFetch.Application.Plugins.ProviderRegistry>>());
});
#endregion

builder.Services.AddValidatorsFromAssemblyContaining<GetMediaQueryValidator>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
#endregion

#region CORS Configuration
// Configure CORS with environment variable support
const string defaultCorsOrigin = "http://localhost:4200";

// Get allowed origins from environment variable only
var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? new[] { defaultCorsOrigin }; // Default for development

// In production, ensure we have proper origins configured. The warning is emitted
// after the host is built so it can use the application's own logger - resolving
// one here would require building a second service provider (ASP0000).
var corsUsesDevelopmentDefault = allowedOrigins.Length == 1 && allowedOrigins[0] == defaultCorsOrigin;

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(corsBuilder =>
    {
        corsBuilder.WithOrigins(allowedOrigins);
        corsBuilder.WithExposedHeaders("Content-Disposition");
        corsBuilder.AllowAnyHeader();
        corsBuilder.AllowAnyMethod();
        corsBuilder.AllowCredentials();

        if (!builder.Environment.IsProduction())
        {
            corsBuilder.WithExposedHeaders("X-Impersonate");
        }
    });
});
#endregion

var app = builder.Build();

#region Startup Diagnostics
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

// Before the registry is ever resolved, so what is on disk is what was asked for by the time
// anything looks. Deliberately at startup and never during a request: software that installs
// itself halfway through a download is not something anyone wants to debug.
{
    var pluginRoot = Path.IsPathRooted(pluginOptions.Path)
        ? pluginOptions.Path
        : Path.Combine(app.Environment.ContentRootPath, pluginOptions.Path);

    await app.Services.GetRequiredService<ArgonFetch.Application.Plugins.PluginInstaller>()
        .InstallAsync(pluginOptions, pluginRoot);

    var registry = app.Services.GetRequiredService<ArgonFetch.Application.Plugins.IProviderRegistry>();

    startupLogger.LogInformation("Plugins: {Count} loaded{Names}",
        registry.Plugins.Count,
        registry.Plugins.Count == 0 ? string.Empty : " - " + string.Join(", ", registry.Plugins.Select(p => $"{p.Id} {p.Version}")));
}

if (app.Environment.IsProduction() && corsUsesDevelopmentDefault)
{
    startupLogger.LogWarning("CORS is using default localhost origin in production. Please set CORS_ALLOWED_ORIGINS environment variable.");
}

// Logged so a mistyped PROXY_LIST_PATH shows up at startup rather than as silent direct fetches.
startupLogger.LogInformation("Proxy rotation: {Count} proxies loaded",
    app.Services.GetRequiredService<ArgonFetch.Application.Services.IProxyPool>().Count);
#endregion

#region Configure HTTP Pipeline
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ArgonFetch API V1");
    });
}


app.UseStaticFiles();

if (!app.Environment.IsDevelopment())
{
    // Don't use HTTPS redirection in production when running behind a reverse proxy
    // The proxy handles HTTPS termination
    // app.UseHttpsRedirection();
    app.UseSpaStaticFiles();
}

// Ensure frontend routes work
app.UseRouting();
app.UseAuthorization();
app.UseCors();
app.MapControllers();

// An unmatched /api path is a client error, not a page. UseSpa below is terminal and answers
// anything routing did not match with index.html, which for an API path means a caller gets
// 200 and a page of HTML instead of a 404 - a parse error rather than a status they can act
// on. Registered as a fallback so it is chosen only when no controller matched, and the SPA
// middleware passes through any request that already has an endpoint, so every non-API path
// still reaches the client router untouched.
app.MapFallback("/api/{**path}", (HttpContext context) => Results.Problem(
    title: "Not Found",
    detail: $"No endpoint matches {context.Request.Path}.",
    statusCode: StatusCodes.Status404NotFound));
#endregion

#region SPA Configuration
// Serve Angular Frontend in Production
if (!app.Environment.IsDevelopment())
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "wwwroot";
    });
}
#endregion

app.Run();