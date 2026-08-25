using FantasyAnalysis.Api.Endpoints;
using FantasyAnalysis.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddHttpClient<IStatsProvider, MlbStatsProvider>(client =>
{
    client.BaseAddress = new Uri("https://statsapi.mlb.com/");
});

builder.Services.AddSingleton<RosterCsvParser>();
builder.Services.AddSingleton<IPlayerMatchingService, PlayerMatchingService>();
builder.Services.AddSingleton<LeagueImportService>();

var app = builder.Build();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapLeagueEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
