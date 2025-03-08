using ArgonFetch.Application.Behaviors;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services.DDLFetcherServices;
using ArgonFetch.Application.Validators;
using DotNetEnv;
using FluentValidation;
using FluentValidation.AspNetCore;
using MediatR;
using SpotifyAPI.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSpaStaticFiles(spaStaticFilesOptions => { spaStaticFilesOptions.RootPath = "wwwroot/browser"; });

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetMediaQuery).Assembly));

// Add HttpClient for TikTokDllFetcherService
builder.Services.AddHttpClient<TikTokDllFetcherService>();

// Register the IDllFetcher implementations
builder.Services.AddScoped<TikTokDllFetcherService>();
builder.Services.AddScoped<DllFetcherService>();

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

// Register YoutubeMusicAPI
builder.Services.AddScoped<YTMusicAPI.SearchClient>();

// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<GetMediaQueryValidator>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

var app = builder.Build();

// yt-dlp Version Check
using (var scope = app.Services.CreateScope())
{
    var dllFetcherService = scope.ServiceProvider.GetRequiredService<DllFetcherService>();
    var ytDlpVersion = await dllFetcherService.GetYtDlpVersionAsync();

    if (string.IsNullOrEmpty(ytDlpVersion))
    {
        Console.WriteLine("WARNING: yt-dlp is not installed or cannot be found!");
    }
    else
    {
        Console.WriteLine($"yt-dlp Version: {ytDlpVersion}");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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

// Serve Angular Frontend in Production
if (!app.Environment.IsDevelopment())
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "wwwroot";
    });
}

app.Run();