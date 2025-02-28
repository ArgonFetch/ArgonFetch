using ArgonFetch.Application.Behaviors;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services.DDLFetcherServices;
using ArgonFetch.Application.Validators;
using FluentValidation;
using FluentValidation.AspNetCore;
using MediatR;
using SpotifyAPI.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

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
    var clientId = config["Spotify:ClientId"];
    var clientSecret = config["Spotify:ClientSecret"];
    if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
    {
        throw new InvalidOperationException("Spotify ClientId and ClientSecret must be provided.");
    }
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseStaticFiles();

if (!app.Environment.IsDevelopment())
{
    app.UseSpaStaticFiles();
}

if (!app.Environment.IsDevelopment())
{
    app.UseSpa(spa =>
    {
        // To learn more about options for serving an Angular SPA from ASP.NET Core,
        // see https://go.microsoft.com/fwlink/?linkid=864501
        spa.Options.SourcePath = "frontend";
    });
}

app.UseAuthorization();

app.MapControllers();

app.Run();
