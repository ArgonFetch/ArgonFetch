using ArgonFetch.API.IntegrationValidators;
using ArgonFetch.Application.Behaviors;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services.DDLFetcherServices;
using ArgonFetch.Application.Validators;
using ArgonFetch.Infrastructure;
using DotNetEnv;
using FluentValidation;
using FluentValidation.AspNetCore;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SpotifyAPI.Web;
using YoutubeDLSharp;

var builder = WebApplication.CreateBuilder(args);

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

    string clientId, clientSecret;

    if (builder.Environment.IsDevelopment())
    {
        clientId = config["Spotify:ClientId"];
        clientSecret = config["Spotify:ClientSecret"];
    }
    else
    {
        Env.Load();
        clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        clientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");
    }

    if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        throw new InvalidOperationException("Spotify ClientId and ClientSecret must be provided.");

    var spotifyConfig = SpotifyClientConfig
       .CreateDefault()
       .WithAuthenticator(new ClientCredentialsAuthenticator(clientId, clientSecret));
    return new SpotifyClient(spotifyConfig);
});

// Register YoutubeMusicAPI and YoutubeDL
builder.Services.AddScoped<YTMusicAPI.SearchClient>();
builder.Services.AddScoped<YoutubeDL>();
#endregion

#region Validation
// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<GetMediaQueryValidator>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
#endregion

#region CORS Configuration
// Configure CORS for frontend development
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(corsBuilder =>
        {
            corsBuilder.WithOrigins("http://localhost:4200");
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
}
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

app.UseHttpsRedirection();
app.UseStaticFiles();

if (!app.Environment.IsDevelopment())
{
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