using System.Text.Json.Serialization;
using FantasyKeeper.Api.Endpoints;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Services.Dev;
using FantasyKeeper.Api.Services.Google;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// NOTE: configuration values are read lazily from IConfiguration inside each
// factory delegate below (resolved at first use), rather than eagerly into
// local variables here. WebApplicationFactory<Program>.WithWebHostBuilder's
// ConfigureAppConfiguration overrides (used by integration tests) are only
// merged into the app's configuration as part of builder.Build() — code that
// reads builder.Configuration directly in this top-level file, before
// Build() runs, would see pre-override values only. Deferring the reads to
// service-resolution time (which always happens after Build()) ensures both
// the real app and WebApplicationFactory-hosted tests see the same,
// fully-merged configuration.
builder.Services.AddSingleton<IConfigStore>(sp =>
{
    var configRoot = Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["ConfigRoot"] ?? "config");
    Directory.CreateDirectory(Path.Combine(configRoot, "team-mappings"));
    return new JsonConfigStore(configRoot);
});

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var keyPath = config["Google:ServiceAccountKeyPath"]
        ?? throw new InvalidOperationException("Google:ServiceAccountKeyPath must be set when Google:UseDevClients is false.");
    return GoogleCredentialLoader.LoadFromFile(
        keyPath,
        "https://www.googleapis.com/auth/spreadsheets",
        "https://www.googleapis.com/auth/drive");
});

builder.Services.AddSingleton<ISheetsClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetValue<bool>("Google:UseDevClients")
        ? new DevSheetsClient()
        : new GoogleSheetsClient(sp.GetRequiredService<GoogleCredential>());
});

builder.Services.AddSingleton<IDriveClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetValue<bool>("Google:UseDevClients")
        ? new DevDriveClient()
        : new GoogleDriveClient(sp.GetRequiredService<GoogleCredential>());
});

builder.Services.AddSingleton(sp =>
{
    var adminPin = sp.GetRequiredService<IConfiguration>()["AdminPin"]
        ?? throw new InvalidOperationException("AdminPin must be configured.");
    return new AuthService(sp.GetRequiredService<IConfigStore>(), adminPin);
});
builder.Services.AddSingleton<KeepersService>();
builder.Services.AddSingleton(sp =>
{
    var commissionerEmail = sp.GetRequiredService<IConfiguration>()["Google:CommissionerEmail"] ?? "";
    return new SeasonService(sp.GetRequiredService<IConfigStore>(), sp.GetRequiredService<IDriveClient>(), commissionerEmail);
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy => policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
    });
}

var app = builder.Build();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapSeasonEndpoints();
app.MapKeeperEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
