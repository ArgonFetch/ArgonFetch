using ArgonFetch.Application.Factories;
using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services.DDLFetcherServices;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetMediaQuery).Assembly));

// Add HttpClient for TikTokDllFetcherService
builder.Services.AddHttpClient<TikTokDllFetcherService>();

// Register the IDllFetcher implementations
builder.Services.AddScoped<IDllFetcher, TikTokDllFetcherService>();
builder.Services.AddScoped<IDllFetcher>(sp =>
    new SpotifyDllFetcherService(
        builder.Configuration["Spotify:ClientId"],
        builder.Configuration["Spotify:ClientSecret"]
    ));

// Register DllFetcherFactory
builder.Services.AddSingleton<DllFetcherFactory>();

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
