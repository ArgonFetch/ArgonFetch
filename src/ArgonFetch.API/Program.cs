using ArgonFetch.Application.Behaviors;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services.DDLFetcherServices;
using ArgonFetch.Application.Validators;
using ArgonFetch.Infrastructure;
using FluentValidation;
using FluentValidation.AspNetCore;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    new ArgonFetch.Application.Services.ToolPaths(builder.Configuration["TOOLS_PATH"]));
builder.Services.AddSingleton<ArgonFetch.Infrastructure.Services.MediaToolsService>();
builder.Services.AddHostedService(
    sp => sp.GetRequiredService<ArgonFetch.Infrastructure.Services.MediaToolsService>());

builder.Services.AddScoped<ArgonFetch.Application.Services.IRequestCounterService,
                           ArgonFetch.Infrastructure.Services.RequestCounterService>();

// Register Application Info Service
builder.Services.AddSingleton<ArgonFetch.Application.Services.IApplicationInfoService, ArgonFetch.Infrastructure.Services.ApplicationInfoService>();
#endregion

#region Database Configuration
// Configure the DbContext with a connection string.
builder.Services.AddDbContext<ArgonFetchDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("ArgonFetchDatabase"),
        npgsqlOptions => npgsqlOptions
        .EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null
        )
    ));
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

// Optional proxy list (one proxy per line) rotated across yt-dlp fetches; no file means direct fetches.
builder.Services.AddSingleton<ArgonFetch.Application.Services.IProxyPool>(sp =>
    new ArgonFetch.Application.Services.ProxyPool(
        ArgonFetch.Application.Services.ProxyPool.ReadList(builder.Configuration["PROXY_LIST_PATH"])));
#endregion

#region Validation
// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
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

#region Database Initialization with Retry Logic
bool dbConnected = false;
int retryCount = 0;
const int maxRetries = 10;
const int retryDelaySeconds = 5;

var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

if (app.Environment.IsProduction() && corsUsesDevelopmentDefault)
{
    startupLogger.LogWarning("CORS is using default localhost origin in production. Please set CORS_ALLOWED_ORIGINS environment variable.");
}

// Logged so a mistyped PROXY_LIST_PATH shows up at startup rather than as silent direct fetches.
startupLogger.LogInformation("Proxy rotation: {Count} proxies loaded",
    app.Services.GetRequiredService<ArgonFetch.Application.Services.IProxyPool>().Count);

while (!dbConnected && retryCount < maxRetries)
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider
            .GetRequiredService<ArgonFetchDbContext>();
        try
        {
            startupLogger.LogInformation("Attempting to connect to the " +
                                         "database and apply migrations " +
                                         "(Attempt {Attempt}/{MaxRetries})...",
                                         retryCount + 1, maxRetries);
            dbContext.Database.Migrate();
            dbConnected = true;
            startupLogger.LogInformation("Database connection successful " +
                                         "and migrations applied.");
        }
        catch (NpgsqlException ex)
        {
            startupLogger.LogError(ex, "Database connection failed: {ErrorMessage}",
                ex.Message);
            retryCount++;
            if (retryCount < maxRetries)
            {
                startupLogger.LogInformation("Retrying in {Delay} seconds...",
                                             retryDelaySeconds);
                System.Threading.Thread.Sleep(TimeSpan
                    .FromSeconds(retryDelaySeconds));
            }
            else
            {
                startupLogger.LogCritical("Failed to connect to the database " +
                                         "after {MaxRetries} retries. " +
                                         "Application will now terminate.",
                                         maxRetries);
                throw;
            }
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex, "An unexpected error occurred during " +
                                     "database connection/migration: {ErrorMessage}",
                                     ex.Message);
            retryCount++;
            if (retryCount < maxRetries)
            {
                startupLogger.LogInformation("Retrying in {Delay} seconds...",
                                             retryDelaySeconds);
                System.Threading.Thread.Sleep(TimeSpan
                    .FromSeconds(retryDelaySeconds));
            }
            else
            {
                startupLogger.LogCritical("Failed to perform database " +
                                         "operations after {MaxRetries} " +
                                         "retries due to an unexpected error. " +
                                         "Application will now terminate.",
                                         maxRetries);
                throw;
            }
        }
    }
}
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