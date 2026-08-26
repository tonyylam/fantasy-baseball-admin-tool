using System.Text.Json.Serialization;
using Anthropic;
using FantasyAnalysis.Api.Endpoints;
using FantasyAnalysis.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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

builder.Services.AddSingleton<ILeagueDataStore>(sp =>
{
    var dataRoot = Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["DataRoot"] ?? "data");
    Directory.CreateDirectory(dataRoot);
    return new FileLeagueDataStore(dataRoot);
});

builder.Services.AddSingleton<IScoringSettingsStore>(sp =>
{
    var dataRoot = Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["DataRoot"] ?? "data");
    Directory.CreateDirectory(dataRoot);
    return new FileScoringSettingsStore(dataRoot);
});

builder.Services.AddHttpClient<IStatsProvider, MlbStatsProvider>(client =>
{
    client.BaseAddress = new Uri("https://statsapi.mlb.com/");
});

builder.Services.AddSingleton<RosterCsvParser>();
builder.Services.AddSingleton<IPlayerMatchingService, PlayerMatchingService>();
builder.Services.AddSingleton<LeagueImportService>();

builder.Services.AddSingleton<IStatsCache>(sp =>
{
    var dataRoot = Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["DataRoot"] ?? "data");
    Directory.CreateDirectory(dataRoot);
    return new FileStatsCache(dataRoot);
});

builder.Services.AddSingleton<IRecommendationDataStore>(sp =>
{
    var dataRoot = Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["DataRoot"] ?? "data");
    Directory.CreateDirectory(dataRoot);
    return new FileRecommendationDataStore(dataRoot);
});

builder.Services.AddSingleton<WaiverPoolCalculator>();
builder.Services.AddSingleton<RotoStandingsCalculator>();
builder.Services.AddSingleton<WeakCategoryWaiverShortlist>();

builder.Services.AddSingleton(sp =>
{
    var apiKey = sp.GetRequiredService<IConfiguration>()["AnthropicApiKey"]
        ?? throw new InvalidOperationException("AnthropicApiKey must be configured.");
    return new Anthropic.AnthropicClient { ApiKey = apiKey };
});
builder.Services.AddSingleton<IRecommendationClient, AnthropicRecommendationClient>();
builder.Services.AddSingleton<ClaudeRecommendationEngine>();
builder.Services.AddSingleton<RecommendationOrchestrationService>();

var app = builder.Build();

// Fails fast at startup if AnthropicApiKey is missing, rather than on the first request
// that happens to need it — same rationale as the sibling app's eager AuthService
// resolution for AdminPin.
app.Services.GetRequiredService<Anthropic.AnthropicClient>();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapLeagueEndpoints();
app.MapScoringSettingsEndpoints();
app.MapRecommendationEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
