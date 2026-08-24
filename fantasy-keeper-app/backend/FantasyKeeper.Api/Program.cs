using System.Text.Json.Serialization;
using FantasyKeeper.Api.Endpoints;
using FantasyKeeper.Api.Services;

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
    return new JsonConfigStore(configRoot);
});

builder.Services.AddSingleton<IKeepersDataStore>(sp =>
{
    var dataRoot = Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["DataRoot"] ?? "data");
    Directory.CreateDirectory(dataRoot);
    return new FileKeepersDataStore(dataRoot);
});

builder.Services.AddSingleton(sp =>
{
    var adminPin = sp.GetRequiredService<IConfiguration>()["AdminPin"]
        ?? throw new InvalidOperationException("AdminPin must be configured.");
    return new AuthService(sp.GetRequiredService<IConfigStore>(), adminPin);
});
builder.Services.AddSingleton<KeepersService>();
builder.Services.AddSingleton<KeepersImportService>();

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

// Eagerly resolve config-dependent singletons so a misconfigured deployment
// (missing AdminPin) fails fast at startup instead of on the first HTTP
// request that happens to need it.
app.Services.GetRequiredService<AuthService>();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapKeeperEndpoints();
app.MapAdminKeepersEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
