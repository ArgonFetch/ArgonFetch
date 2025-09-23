using ArgonFetch.API.IntegrationValidators;
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
using SpotifyAPI.Web;
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
builder.Services.AddControllers();
builder.Services.AddSpaStaticFiles(spaStaticFilesOptions => { spaStaticFilesOptions.RootPath = "wwwroot/browser"; });

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetMediaQuery).Assembly));

// Add HttpClient for TikTokDllFetcherService
builder.Services.AddHttpClient<TikTokDllFetcherService>();

// Register the IDllFetcher implementations
builder.Services.AddScoped<TikTokDllFetcherService>();

// Register In memory caching
builder.Services.AddMemoryCache();
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
// Register SpotifyAPI
builder.Services.AddScoped<SpotifyClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    // Use Configuration pattern consistently - same as ConnectionStrings
    string clientId = config["Spotify:ClientId"];
    string clientSecret = config["Spotify:ClientSecret"];

    if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
    {
        var logger = sp.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Spotify API credentials not configured. Spotify features will be disabled.");
        return null;
    }

    var spotifyConfig = SpotifyClientConfig
       .CreateDefault()
       .WithAuthenticator(new ClientCredentialsAuthenticator(clientId, clientSecret));
    return new SpotifyClient(spotifyConfig);
});

// Register YoutubeMusicAPI and YoutubeDL
builder.Services.AddScoped<YTMusicAPI.SearchClient>();
builder.Services.AddScoped<YoutubeDL>();

// Register FFmpeg and streaming services
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ArgonFetch.Application.Interfaces.IFfmpegStreamingService, ArgonFetch.Infrastructure.Services.FfmpegStreamingService>();
builder.Services.AddScoped<ArgonFetch.Application.Interfaces.IAcceleratedDownloadService, ArgonFetch.Infrastructure.Services.AcceleratedDownloadService>();
builder.Services.AddScoped<ArgonFetch.Application.Services.ICombinedStreamUrlBuilder, ArgonFetch.Application.Services.CombinedStreamUrlBuilder>();
builder.Services.AddScoped<ArgonFetch.Application.Services.IProxyUrlBuilder, ArgonFetch.Application.Services.ProxyUrlBuilder>();
builder.Services.AddSingleton<ArgonFetch.Application.Services.IMediaUrlCacheService, ArgonFetch.Application.Services.MediaUrlCacheService>();
#endregion

#region Validation
// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<GetMediaQueryValidator>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
#endregion

#region CORS Configuration
// Configure CORS with environment variable support
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(corsBuilder =>
    {
        // Get allowed origins from environment variable only
        var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? new[] { "http://localhost:4200" }; // Default for development

        // In production, ensure we have proper origins configured
        if (builder.Environment.IsProduction() && allowedOrigins.Length == 1 && allowedOrigins[0] == "http://localhost:4200")
        {
            var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
            logger.LogWarning("CORS is using default localhost origin in production. Please set CORS_ALLOWED_ORIGINS environment variable.");
        }

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

#region Dependency Validation
// yt-dlp and FFmpeg Version Check.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Check yt-dlp
    var ytDlpVersion = await MediaValidators.GetYtDlpVersionAsync();
    if (string.IsNullOrEmpty(ytDlpVersion))
        logger.LogWarning("yt-dlp is not installed or cannot be found!");
    else
        logger.LogInformation("yt-dlp Version: {Version}", ytDlpVersion);

    // Check FFmpeg
    var ffmpegVersion = await MediaValidators.GetFfmpegVersionAsync();
    if (string.IsNullOrEmpty(ffmpegVersion))
        logger.LogWarning("FFmpeg is not installed or cannot be found!");
    else
        logger.LogInformation("FFmpeg Version: {Version}", ffmpegVersion);
}
#endregion

#region Configure HTTP Pipeline
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "DockiUp API V1");
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