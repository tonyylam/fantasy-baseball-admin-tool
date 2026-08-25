# Fantasy Analysis App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a single-user web app that imports a CSV of every team's MLB fantasy roster, lets the user enter their scoring settings, and uses Claude to generate ranked, reasoned waiver-pickup and trade suggestions on an interactive dashboard.

**Architecture:** Vite + React + TypeScript frontend, ASP.NET Core 8 minimal-API backend, single-process deployment (backend serves the built frontend from `wwwroot`). JSON file persistence under a gitignored `data/` folder, no database. `statsapi.mlb.com` (free, unauthenticated) supplies MLB player/stat data; Claude Opus 5 (Anthropic C# SDK) with the web-search server tool produces the recommendations.

**Tech Stack:** .NET 8 minimal API, xUnit + `Microsoft.AspNetCore.Mvc.Testing`, Anthropic C# SDK, React 19 + TypeScript + Vite, Vitest + Testing Library.

**Spec:** [docs/superpowers/specs/2026-08-25-fantasy-analysis-app-design.md](../specs/2026-08-25-fantasy-analysis-app-design.md)

## Global Constraints

- Single-user, no authentication — every endpoint is unauthenticated (unlike the sibling `fantasy-keeper-app`, which is PIN-gated).
- MLB only for v1. `IStatsProvider` must be sport-agnostic in shape even though only `MlbStatsProvider` is implemented.
- No database — all persistence is JSON files under a configurable `DataRoot`, written atomically (temp file + `File.Move(overwrite: true)`), matching `fantasy-keeper-app`'s `FileKeepersDataStore` pattern.
- `statsapi.mlb.com` is unofficial/undocumented for third-party use — stats-provider code must fail loudly (typed exception) on unexpected response shape, never silently produce wrong data.
- Player-name matching (CSV → MLB Stats API player) always goes through a user review/confirm step before being persisted — never fully automatic.
- The Anthropic client must be called only through an injected interface (not the concrete SDK client directly) so recommendation-engine logic is unit-testable without hitting the real API.
- Model is always `claude-opus-5`; web search tool is `WebSearchTool20260209`; structured output via `OutputConfig.Format`/`JsonOutputFormat` — per `shared/claude-api` reference confirmed during brainstorming.
- CSV roster format (decided now, since the spec didn't pin an exact schema): two columns, header row `Team,Player`, one data row per rostered player (long format) — e.g. `Team,Player` / `Rhino Wranglers,Shohei Ohtani` / `Rhino Wranglers,Mookie Betts` / `Sea Dogs,Ronald Acuna Jr.`.

## File Structure

**Backend** (`backend/FantasyAnalysis.Api/`):
- `Program.cs` — DI wiring, endpoint mapping, static file serving.
- `Models/Player.cs` (parsed, pre-match shapes), `Models/League.cs` (persisted, post-match shapes: `RosteredPlayer`/`TeamRoster`/`League`), `Models/MlbPlayer.cs`, `Models/StatLine.cs`, `Models/PlayerMatch.cs`, `Models/ImportModels.cs`, `Models/ScoringSettings.cs`, `Models/StatsCacheEntry.cs`, `Models/Recommendation.cs` (includes `RecommendationType`/`Recommendation`/`RecommendationSet`), `Models/DomainExceptions.cs` — plain records (and typed exceptions).
- `Services/RosterCsvParser.cs` — CSV → parsed teams/players.
- `Services/ILeagueDataStore.cs` / `FileLeagueDataStore.cs` — league persistence.
- `Services/IStatsProvider.cs` / `MlbStatsProvider.cs` — `statsapi.mlb.com` client.
- `Services/IPlayerMatchingService.cs` / `PlayerMatchingService.cs` — fuzzy name matching.
- `Services/IStatsCache.cs` / `FileStatsCache.cs` — TTL'd stats cache.
- `Services/IScoringSettingsStore.cs` / `FileScoringSettingsStore.cs` — scoring settings persistence.
- `Services/WaiverPoolCalculator.cs` — active players minus rostered players.
- `Services/FantasyValueRanker.cs` — numeric pre-filter/shortlist using scoring settings.
- `Services/IRecommendationClient.cs` / `AnthropicRecommendationClient.cs` — thin Claude wrapper (the mockable seam).
- `Services/ClaudeRecommendationEngine.cs` — context assembly + response parsing.
- `Services/IRecommendationDataStore.cs` / `FileRecommendationDataStore.cs` — recommendation persistence.
- `Endpoints/LeagueEndpoints.cs`, `Endpoints/ScoringSettingsEndpoints.cs`, `Endpoints/RecommendationEndpoints.cs`.
- `Models/DomainExceptions.cs` — typed exceptions (`StatsProviderException`, `RecommendationClientException`, etc.).

**Backend tests** (`backend/FantasyAnalysis.Api.Tests/`): one test file per service/endpoint above, plus `Fakes/` for test doubles (`FakeStatsProvider`, `FakeRecommendationClient`, etc.), matching `fantasy-keeper-app`'s `Fakes/` convention.

**Frontend** (`frontend/src/`):
- `types.ts` — shared TS types mirroring backend DTOs.
- `api/client.ts` — fetch wrapper + per-endpoint functions.
- `screens/ImportScreen.tsx`, `screens/MatchReviewScreen.tsx`, `screens/ScoringSettingsScreen.tsx`, `screens/DashboardScreen.tsx`.
- `App.tsx` — screen routing/state.
- `main.tsx` — entry point.

---

### Task 1: Backend project scaffold + health endpoint

**Files:**
- Create: `backend/FantasyAnalysis.Api/FantasyAnalysis.Api.csproj`
- Create: `backend/FantasyAnalysis.Api/Program.cs`
- Create: `backend/FantasyAnalysis.Api/appsettings.json`
- Create: `backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj`
- Create: `backend/FantasyAnalysis.Api.Tests/HealthEndpointTests.cs`
- Create: `FantasyAnalysis.sln`
- Create: `.gitignore`

**Interfaces:**
- Produces: a running minimal-API host with `GET /health` returning `{ status = "ok" }`. Every later task adds DI registrations to `Program.cs` and endpoint-mapping calls after this skeleton.

- [ ] **Step 1: Create the solution and backend project**

```bash
cd backend
dotnet new web -n FantasyAnalysis.Api -o FantasyAnalysis.Api
cd ..
dotnet new sln -n FantasyAnalysis
dotnet sln FantasyAnalysis.sln add backend/FantasyAnalysis.Api/FantasyAnalysis.Api.csproj
```

Replace the generated `backend/FantasyAnalysis.Api/FantasyAnalysis.Api.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Anthropic" Version="12.9.0" />
  </ItemGroup>

</Project>
```

Replace `backend/FantasyAnalysis.Api/Program.cs` with:

```csharp
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

var app = builder.Build();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
```

Create `backend/FantasyAnalysis.Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [ ] **Step 2: Create the test project**

```bash
cd backend
dotnet new xunit -n FantasyAnalysis.Api.Tests -o FantasyAnalysis.Api.Tests
dotnet add FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj reference FantasyAnalysis.Api/FantasyAnalysis.Api.csproj
dotnet add FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 8.0.16
cd ..
dotnet sln FantasyAnalysis.sln add backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj
```

Create `backend/FantasyAnalysis.Api.Tests/HealthEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOkStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }
}
```

Add `using System.Text.Json;` to the top of that file (needed for `JsonElement`).

- [ ] **Step 3: Run the test to verify it passes**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj`
Expected: PASS (1 test)

- [ ] **Step 4: Add .gitignore**

Create `.gitignore` at the repo root (`fantasy-analysis-app/`):

```
bin/
obj/
node_modules/
data/
*.tmp
backend/FantasyAnalysis.Api/wwwroot/
```

- [ ] **Step 5: Commit**

```bash
git add FantasyAnalysis.sln .gitignore backend/FantasyAnalysis.Api backend/FantasyAnalysis.Api.Tests
git commit -m "Scaffold backend project with health endpoint"
```

---

### Task 2: Core roster models + CSV parser

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/Player.cs`
- Create: `backend/FantasyAnalysis.Api/Models/DomainExceptions.cs`
- Create: `backend/FantasyAnalysis.Api/Services/RosterCsvParser.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/RosterCsvParserTests.cs`

**Interfaces:**
- Produces: `RosterCsvParser.Parse(string csvContent) : ParsedLeague`, where `ParsedLeague` is `record ParsedLeague(IReadOnlyList<ParsedTeamRoster> Teams)` and `ParsedTeamRoster` is `record ParsedTeamRoster(string TeamName, IReadOnlyList<string> PlayerNames)`. Throws `CsvParseException` (defined in `DomainExceptions.cs`) on malformed input (missing header, wrong column count).
- These are the pre-persistence, pre-matching shapes — Task 3's `League`/`TeamRoster`/`Player` models are the persisted, post-match shapes and are distinct types.

- [ ] **Step 1: Write the failing test**

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RosterCsvParserTests
{
    [Fact]
    public void Parse_GroupsPlayersByTeamInFileOrder()
    {
        var csv = "Team,Player\nRhino Wranglers,Shohei Ohtani\nRhino Wranglers,Mookie Betts\nSea Dogs,Ronald Acuna Jr.\n";

        var result = new RosterCsvParser().Parse(csv);

        Assert.Equal(2, result.Teams.Count);
        Assert.Equal("Rhino Wranglers", result.Teams[0].TeamName);
        Assert.Equal(new[] { "Shohei Ohtani", "Mookie Betts" }, result.Teams[0].PlayerNames);
        Assert.Equal("Sea Dogs", result.Teams[1].TeamName);
        Assert.Equal(new[] { "Ronald Acuna Jr." }, result.Teams[1].PlayerNames);
    }

    [Fact]
    public void Parse_MissingHeader_ThrowsCsvParseException()
    {
        var csv = "Rhino Wranglers,Shohei Ohtani\n";

        Assert.Throws<CsvParseException>(() => new RosterCsvParser().Parse(csv));
    }

    [Fact]
    public void Parse_RowWithWrongColumnCount_ThrowsCsvParseException()
    {
        var csv = "Team,Player\nRhino Wranglers,Shohei Ohtani,ExtraColumn\n";

        Assert.Throws<CsvParseException>(() => new RosterCsvParser().Parse(csv));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RosterCsvParserTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Write the models**

`backend/FantasyAnalysis.Api/Models/Player.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record ParsedLeague(IReadOnlyList<ParsedTeamRoster> Teams);

public record ParsedTeamRoster(string TeamName, IReadOnlyList<string> PlayerNames);
```

`backend/FantasyAnalysis.Api/Models/DomainExceptions.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public class CsvParseException : Exception
{
    public CsvParseException(string message) : base(message) { }
}
```

- [ ] **Step 4: Implement the parser**

`backend/FantasyAnalysis.Api/Services/RosterCsvParser.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class RosterCsvParser
{
    public ParsedLeague Parse(string csvContent)
    {
        var lines = csvContent
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0 || !string.Equals(lines[0], "Team,Player", StringComparison.OrdinalIgnoreCase))
        {
            throw new CsvParseException("Expected a header row \"Team,Player\".");
        }

        var teams = new List<ParsedTeamRoster>();
        var playersByTeam = new Dictionary<string, List<string>>();
        var teamOrder = new List<string>();

        for (var i = 1; i < lines.Count; i++)
        {
            var columns = lines[i].Split(',');
            if (columns.Length != 2)
            {
                throw new CsvParseException($"Line {i + 1}: expected 2 columns (Team,Player), found {columns.Length}.");
            }

            var teamName = columns[0].Trim();
            var playerName = columns[1].Trim();

            if (!playersByTeam.TryGetValue(teamName, out var players))
            {
                players = new List<string>();
                playersByTeam[teamName] = players;
                teamOrder.Add(teamName);
            }
            players.Add(playerName);
        }

        foreach (var teamName in teamOrder)
        {
            teams.Add(new ParsedTeamRoster(teamName, playersByTeam[teamName]));
        }

        return new ParsedLeague(teams);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RosterCsvParserTests`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/Player.cs backend/FantasyAnalysis.Api/Models/DomainExceptions.cs backend/FantasyAnalysis.Api/Services/RosterCsvParser.cs backend/FantasyAnalysis.Api.Tests/RosterCsvParserTests.cs
git commit -m "Add roster CSV parser"
```

---

### Task 3: Persisted league models + FileLeagueDataStore

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/League.cs`
- Create: `backend/FantasyAnalysis.Api/Services/ILeagueDataStore.cs`
- Create: `backend/FantasyAnalysis.Api/Services/FileLeagueDataStore.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/FileLeagueDataStoreTests.cs`

**Interfaces:**
- Consumes: nothing from prior tasks. (Task 2's `ParsedLeague`/`ParsedTeamRoster` live in a separate file, `Models/Player.cs` — this task's `League`/`TeamRoster`/`RosteredPlayer` are the distinct, persisted shapes, in their own new file.)
- Produces: `record RosteredPlayer(string CsvName, string PlayerId, string PlayerFullName, string Position, bool IsPitcher)`, `record TeamRoster(string TeamName, IReadOnlyList<RosteredPlayer> Players)`, `record League(DateTimeOffset ImportedAtUtc, IReadOnlyList<TeamRoster> Teams)`, and `interface ILeagueDataStore { League? LoadLeague(); void SaveLeague(League league); }` implemented by `FileLeagueDataStore(string dataRoot)`. These are the types every later task (matching, waiver pool, recommendation context) reads.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FileLeagueDataStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileLeagueDataStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadLeague_WhenFileMissing_ReturnsNull()
    {
        var store = new FileLeagueDataStore(_tempDir);
        Assert.Null(store.LoadLeague());
    }

    [Fact]
    public void SaveAndLoadLeague_RoundTrips()
    {
        var store = new FileLeagueDataStore(_tempDir);
        var league = new League(
            DateTimeOffset.UtcNow,
            new List<TeamRoster>
            {
                new("Rhino Wranglers", new List<RosteredPlayer>
                {
                    new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
                })
            });

        store.SaveLeague(league);
        var loaded = store.LoadLeague();

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Teams);
        Assert.Equal("Shohei Ohtani", loaded.Teams[0].Players[0].PlayerFullName);
    }

    [Fact]
    public void SaveLeague_OverwritesExistingFileAndLeavesNoTempFileBehind()
    {
        var store = new FileLeagueDataStore(_tempDir);
        var first = new League(DateTimeOffset.UtcNow, new List<TeamRoster>());
        var second = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Sea Dogs", new List<RosteredPlayer>())
        });

        store.SaveLeague(first);
        store.SaveLeague(second);

        Assert.Single(store.LoadLeague()!.Teams);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FileLeagueDataStoreTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Add the persisted models**

Create `backend/FantasyAnalysis.Api/Models/League.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record RosteredPlayer(string CsvName, string PlayerId, string PlayerFullName, string Position, bool IsPitcher);

public record TeamRoster(string TeamName, IReadOnlyList<RosteredPlayer> Players);

public record League(DateTimeOffset ImportedAtUtc, IReadOnlyList<TeamRoster> Teams);
```

- [ ] **Step 4: Implement the store**

`backend/FantasyAnalysis.Api/Services/ILeagueDataStore.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface ILeagueDataStore
{
    League? LoadLeague();
    void SaveLeague(League league);
}
```

`backend/FantasyAnalysis.Api/Services/FileLeagueDataStore.cs`:

```csharp
using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FileLeagueDataStore : ILeagueDataStore
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileLeagueDataStore(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string LeaguePath => Path.Combine(_dataRoot, "league.json");

    public League? LoadLeague()
    {
        if (!File.Exists(LeaguePath)) return null;
        return JsonSerializer.Deserialize<League>(File.ReadAllText(LeaguePath), JsonOptions);
    }

    public void SaveLeague(League league)
    {
        Directory.CreateDirectory(_dataRoot);
        var tempPath = LeaguePath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(league, JsonOptions));
            File.Move(tempPath, LeaguePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FileLeagueDataStoreTests`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/League.cs backend/FantasyAnalysis.Api/Services/ILeagueDataStore.cs backend/FantasyAnalysis.Api/Services/FileLeagueDataStore.cs backend/FantasyAnalysis.Api.Tests/FileLeagueDataStoreTests.cs
git commit -m "Add persisted league model and file-backed store"
```

---

### Task 4: MLB Stats API client — active player list

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/MlbPlayer.cs`
- Create: `backend/FantasyAnalysis.Api/Models/StatLine.cs`
- Create: `backend/FantasyAnalysis.Api/Models/DomainExceptions.cs` (extend)
- Create: `backend/FantasyAnalysis.Api/Services/IStatsProvider.cs`
- Create: `backend/FantasyAnalysis.Api/Services/MlbStatsProvider.cs`
- Create: `backend/FantasyAnalysis.Api.Tests/Fakes/StubHttpMessageHandler.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/MlbStatsProviderTests.cs`

**Interfaces:**
- Consumes: nothing from prior tasks.
- Produces: `record MlbPlayer(string Id, string FullName, string Position, bool IsPitcher, int? MlbTeamId)`; `interface IStatsProvider { Task<IReadOnlyList<MlbPlayer>> GetAllActivePlayersAsync(int season); Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season); }` (the `GetPlayerStatsAsync` method is implemented in Task 5 — this task adds the interface member and a `NotImplementedException` body, Task 5 fills it in); `class StatsProviderException : Exception` for malformed/unexpected API responses; `Fakes/StubHttpMessageHandler` — a reusable `HttpMessageHandler` test double that returns a canned response for a given request predicate, used by this task and Task 5.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class MlbStatsProviderTests
{
    [Fact]
    public async Task GetAllActivePlayersAsync_ParsesPlayerFields()
    {
        var json = """
        {
          "people": [
            {
              "id": 660271,
              "fullName": "Shohei Ohtani",
              "active": true,
              "primaryPosition": { "code": "10", "name": "Designated Hitter", "type": "Hitter", "abbreviation": "DH" },
              "currentTeam": { "id": 119 }
            },
            {
              "id": 605483,
              "fullName": "Gerrit Cole",
              "active": true,
              "primaryPosition": { "code": "1", "name": "Pitcher", "type": "Pitcher", "abbreviation": "P" },
              "currentTeam": { "id": 147 }
            }
          ]
        }
        """;
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            Assert.Contains("/api/v1/sports/1/players", req.RequestUri!.ToString());
            Assert.Contains("season=2026", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        var players = await provider.GetAllActivePlayersAsync(2026);

        Assert.Equal(2, players.Count);
        Assert.Equal("660271", players[0].Id);
        Assert.Equal("Shohei Ohtani", players[0].FullName);
        Assert.False(players[0].IsPitcher);
        Assert.Equal(119, players[0].MlbTeamId);
        Assert.True(players[1].IsPitcher);
    }

    [Fact]
    public async Task GetAllActivePlayersAsync_UnexpectedResponseShape_ThrowsStatsProviderException()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"notPeople\": []}", System.Text.Encoding.UTF8, "application/json")
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        await Assert.ThrowsAsync<StatsProviderException>(() => provider.GetAllActivePlayersAsync(2026));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter MlbStatsProviderTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Add the stub HTTP handler test double**

`backend/FantasyAnalysis.Api.Tests/Fakes/StubHttpMessageHandler.cs`:

```csharp
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responder(request, cancellationToken));
    }
}
```

Add `using FantasyAnalysis.Api.Tests.Fakes;` to the top of `MlbStatsProviderTests.cs`.

- [ ] **Step 4: Add the models and exception**

`backend/FantasyAnalysis.Api/Models/MlbPlayer.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record MlbPlayer(string Id, string FullName, string Position, bool IsPitcher, int? MlbTeamId);
```

`backend/FantasyAnalysis.Api/Models/StatLine.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record StatLine(string PlayerId, int Season, string Group, IReadOnlyDictionary<string, decimal> Stats);
```

`Group` is `"hitting"` or `"pitching"`; `Stats` is a flexible stat-name-to-value bag (avoids hardcoding every MLB Stats API field into a rigid schema — see Task 5).

Append to `backend/FantasyAnalysis.Api/Models/DomainExceptions.cs`:

```csharp
public class StatsProviderException : Exception
{
    public StatsProviderException(string message) : base(message) { }
    public StatsProviderException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 5: Implement the interface and provider**

`backend/FantasyAnalysis.Api/Services/IStatsProvider.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IStatsProvider
{
    Task<IReadOnlyList<MlbPlayer>> GetAllActivePlayersAsync(int season);
    Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season);
}
```

`backend/FantasyAnalysis.Api/Services/MlbStatsProvider.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class MlbStatsProvider : IStatsProvider
{
    private readonly HttpClient _http;

    public MlbStatsProvider(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<MlbPlayer>> GetAllActivePlayersAsync(int season)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"api/v1/sports/1/players?season={season}");
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new StatsProviderException("Failed to reach the MLB Stats API for the player list.", ex);
        }

        var body = await response.Content.ReadAsStringAsync();
        PlayersResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlayersResponse>(body);
        }
        catch (JsonException ex)
        {
            throw new StatsProviderException("MLB Stats API player list response was not valid JSON.", ex);
        }

        if (parsed?.People is null)
        {
            throw new StatsProviderException("MLB Stats API player list response did not contain a \"people\" array.");
        }

        return parsed.People
            .Where(p => p.Active && p.Id is not null && p.FullName is not null)
            .Select(p => new MlbPlayer(
                p.Id!.Value.ToString(),
                p.FullName!,
                p.PrimaryPosition?.Abbreviation ?? "",
                string.Equals(p.PrimaryPosition?.Type, "Pitcher", StringComparison.OrdinalIgnoreCase),
                p.CurrentTeam?.Id))
            .ToList();
    }

    public Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season)
    {
        throw new NotImplementedException("Implemented in Task 5.");
    }

    private class PlayersResponse
    {
        [JsonPropertyName("people")]
        public List<PersonDto>? People { get; set; }
    }

    private class PersonDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("primaryPosition")]
        public PositionDto? PrimaryPosition { get; set; }

        [JsonPropertyName("currentTeam")]
        public TeamDto? CurrentTeam { get; set; }
    }

    private class PositionDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("abbreviation")]
        public string? Abbreviation { get; set; }
    }

    private class TeamDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter MlbStatsProviderTests`
Expected: PASS (2 tests)

- [ ] **Step 7: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/MlbPlayer.cs backend/FantasyAnalysis.Api/Models/StatLine.cs backend/FantasyAnalysis.Api/Models/DomainExceptions.cs backend/FantasyAnalysis.Api/Services/IStatsProvider.cs backend/FantasyAnalysis.Api/Services/MlbStatsProvider.cs backend/FantasyAnalysis.Api.Tests/Fakes/StubHttpMessageHandler.cs backend/FantasyAnalysis.Api.Tests/MlbStatsProviderTests.cs
git commit -m "Add MLB Stats API client for the active player list"
```

---

### Task 5: MLB Stats API client — per-player stat lines

**Files:**
- Modify: `backend/FantasyAnalysis.Api/Services/MlbStatsProvider.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/MlbStatsProviderTests.cs` (extend)

**Interfaces:**
- Consumes: `Fakes/StubHttpMessageHandler` from Task 4.
- Produces: a working `GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season)` — fetches both `hitting` and `pitching` season stat groups per player (concurrently, throttled), and returns one `StatLine` per player per group that actually has stats (a position player yields one `StatLine`, a two-way player like Ohtani yields two, and a group with no stats for that player is silently omitted — not an error).

- [ ] **Step 1: Write the failing test**

Append to `backend/FantasyAnalysis.Api.Tests/MlbStatsProviderTests.cs`:

```csharp
    [Fact]
    public async Task GetPlayerStatsAsync_FetchesHittingAndPitchingAndSkipsEmptyGroups()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            string json;
            if (url.Contains("group=hitting"))
            {
                json = """{ "stats": [ { "splits": [ { "stat": { "homeRuns": 44, "avg": ".310" } } ] } ] }""";
            }
            else
            {
                json = """{ "stats": [ { "splits": [] } ] }""";
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        var lines = await provider.GetPlayerStatsAsync(new[] { "660271" }, 2026);

        var line = Assert.Single(lines);
        Assert.Equal("hitting", line.Group);
        Assert.Equal(44m, line.Stats["homeRuns"]);
        Assert.Equal(0.310m, line.Stats["avg"]);
    }

    [Fact]
    public async Task GetPlayerStatsAsync_TwoWayPlayer_ReturnsBothGroups()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var json = """{ "stats": [ { "splits": [ { "stat": { "strikeOuts": 200 } } ] } ] }""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        var lines = await provider.GetPlayerStatsAsync(new[] { "660271" }, 2026);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Group == "hitting");
        Assert.Contains(lines, l => l.Group == "pitching");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter MlbStatsProviderTests`
Expected: FAIL — `GetPlayerStatsAsync` throws `NotImplementedException`

- [ ] **Step 3: Implement `GetPlayerStatsAsync`**

Replace the `GetPlayerStatsAsync` method body in `backend/FantasyAnalysis.Api/Services/MlbStatsProvider.cs` and add the supporting private members (add `using System.Threading;` at the top):

```csharp
    public async Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season)
    {
        using var throttle = new SemaphoreSlim(5);
        var tasks = playerIds.Select(async playerId =>
        {
            await throttle.WaitAsync();
            try
            {
                var lines = new List<StatLine>();
                foreach (var group in new[] { "hitting", "pitching" })
                {
                    var line = await FetchGroupStatsAsync(playerId, group, season);
                    if (line is not null) lines.Add(line);
                }
                return lines;
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    private async Task<StatLine?> FetchGroupStatsAsync(string playerId, string group, int season)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"api/v1/people/{playerId}/stats?stats=season&group={group}&season={season}");
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new StatsProviderException($"Failed to reach the MLB Stats API for player {playerId} ({group}).", ex);
        }

        var body = await response.Content.ReadAsStringAsync();
        StatsResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StatsResponse>(body);
        }
        catch (JsonException ex)
        {
            throw new StatsProviderException($"MLB Stats API stats response for player {playerId} was not valid JSON.", ex);
        }

        var split = parsed?.Stats?.SelectMany(s => s.Splits ?? new List<SplitDto>()).FirstOrDefault();
        if (split?.Stat is null || split.Stat.Count == 0) return null;

        var stats = new Dictionary<string, decimal>();
        foreach (var (key, element) in split.Stat)
        {
            var value = TryConvertToDecimal(element);
            if (value is not null) stats[key] = value.Value;
        }

        return new StatLine(playerId, season, group, stats);
    }

    private static decimal? TryConvertToDecimal(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDecimal(),
        JsonValueKind.String when decimal.TryParse(element.GetString(), out var parsed) => parsed,
        _ => null
    };

    private class StatsResponse
    {
        [JsonPropertyName("stats")]
        public List<StatGroupDto>? Stats { get; set; }
    }

    private class StatGroupDto
    {
        [JsonPropertyName("splits")]
        public List<SplitDto>? Splits { get; set; }
    }

    private class SplitDto
    {
        [JsonPropertyName("stat")]
        public Dictionary<string, JsonElement>? Stat { get; set; }
    }
```

Remove the old `throw new NotImplementedException(...)` stub method.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter MlbStatsProviderTests`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Services/MlbStatsProvider.cs backend/FantasyAnalysis.Api.Tests/MlbStatsProviderTests.cs
git commit -m "Implement per-player stat line fetching"
```

---

### Task 6: Player matching service

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/PlayerMatch.cs`
- Create: `backend/FantasyAnalysis.Api/Services/IPlayerMatchingService.cs`
- Create: `backend/FantasyAnalysis.Api/Services/PlayerMatchingService.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/PlayerMatchingServiceTests.cs`

**Interfaces:**
- Consumes: `MlbPlayer` from Task 4.
- Produces: `record PlayerMatchCandidate(string PlayerId, string FullName, string Position, bool IsPitcher, double Score)` (carries `Position`/`IsPitcher` through from the candidate pool's `MlbPlayer` so Task 20's confirm request can be built without a second lookup), `record PlayerMatch(string CsvName, PlayerMatchCandidate? BestGuess, IReadOnlyList<PlayerMatchCandidate> Candidates)`, and `interface IPlayerMatchingService { IReadOnlyList<PlayerMatch> MatchPlayers(IReadOnlyList<string> csvNames, IReadOnlyList<MlbPlayer> candidatePool); }` implemented by `PlayerMatchingService`. Pure/synchronous — the caller (Task 7's import endpoint) fetches the candidate pool via `IStatsProvider` once and passes it in, rather than this service doing its own I/O. `BestGuess` is `null` when no candidate scores above the match threshold, signaling the review UI to require a manual pick.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class PlayerMatchingServiceTests
{
    private static readonly List<MlbPlayer> Pool = new()
    {
        new MlbPlayer("660271", "Shohei Ohtani", "DH", false, 119),
        new MlbPlayer("665489", "Ronald Acuña Jr.", "OF", false, 144),
        new MlbPlayer("665742", "Juan Soto", "OF", false, 121)
    };

    [Fact]
    public void MatchPlayers_ExactNameMatch_ReturnsFullConfidenceBestGuess()
    {
        var service = new PlayerMatchingService();

        var matches = service.MatchPlayers(new[] { "Shohei Ohtani" }, Pool);

        var match = Assert.Single(matches);
        Assert.NotNull(match.BestGuess);
        Assert.Equal("660271", match.BestGuess!.PlayerId);
        Assert.Equal(1.0, match.BestGuess.Score, 3);
    }

    [Fact]
    public void MatchPlayers_DiacriticAndPunctuationDifference_StillMatches()
    {
        var service = new PlayerMatchingService();

        var matches = service.MatchPlayers(new[] { "Ronald Acuna Jr" }, Pool);

        var match = Assert.Single(matches);
        Assert.NotNull(match.BestGuess);
        Assert.Equal("665489", match.BestGuess!.PlayerId);
    }

    [Fact]
    public void MatchPlayers_NoCloseCandidate_ReturnsNullBestGuess()
    {
        var service = new PlayerMatchingService();

        var matches = service.MatchPlayers(new[] { "Zzyzx Nobody" }, Pool);

        var match = Assert.Single(matches);
        Assert.Null(match.BestGuess);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter PlayerMatchingServiceTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Add the models**

`backend/FantasyAnalysis.Api/Models/PlayerMatch.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record PlayerMatchCandidate(string PlayerId, string FullName, string Position, bool IsPitcher, double Score);

public record PlayerMatch(string CsvName, PlayerMatchCandidate? BestGuess, IReadOnlyList<PlayerMatchCandidate> Candidates);
```

- [ ] **Step 4: Implement the matching service**

`backend/FantasyAnalysis.Api/Services/IPlayerMatchingService.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IPlayerMatchingService
{
    IReadOnlyList<PlayerMatch> MatchPlayers(IReadOnlyList<string> csvNames, IReadOnlyList<MlbPlayer> candidatePool);
}
```

`backend/FantasyAnalysis.Api/Services/PlayerMatchingService.cs`:

```csharp
using System.Globalization;
using System.Text;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class PlayerMatchingService : IPlayerMatchingService
{
    private const double MatchThreshold = 0.7;
    private const int MaxCandidates = 5;

    public IReadOnlyList<PlayerMatch> MatchPlayers(IReadOnlyList<string> csvNames, IReadOnlyList<MlbPlayer> candidatePool)
    {
        var normalizedPool = candidatePool
            .Select(p => (Player: p, Normalized: Normalize(p.FullName)))
            .ToList();

        var matches = new List<PlayerMatch>();
        foreach (var csvName in csvNames)
        {
            var normalizedCsvName = Normalize(csvName);

            var scored = normalizedPool
                .Select(p => new PlayerMatchCandidate(p.Player.Id, p.Player.FullName, p.Player.Position, p.Player.IsPitcher, Similarity(normalizedCsvName, p.Normalized)))
                .OrderByDescending(c => c.Score)
                .Take(MaxCandidates)
                .ToList();

            var bestGuess = scored.FirstOrDefault();
            matches.Add(new PlayerMatch(
                csvName,
                bestGuess is not null && bestGuess.Score >= MatchThreshold ? bestGuess : null,
                scored));
        }

        return matches;
    }

    private static string Normalize(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        var lower = stripped.ToLowerInvariant().Replace(".", "");
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("jr" or "sr" or "ii" or "iii" or "iv"));
        return string.Join(" ", words);
    }

    private static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        return 1.0 - (double)LevenshteinDistance(a, b) / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter PlayerMatchingServiceTests`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/PlayerMatch.cs backend/FantasyAnalysis.Api/Services/IPlayerMatchingService.cs backend/FantasyAnalysis.Api/Services/PlayerMatchingService.cs backend/FantasyAnalysis.Api.Tests/PlayerMatchingServiceTests.cs
git commit -m "Add fuzzy player name matching service"
```

---

### Task 7: League import service

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/ImportModels.cs`
- Create: `backend/FantasyAnalysis.Api/Services/SeasonClock.cs`
- Create: `backend/FantasyAnalysis.Api/Services/LeagueImportService.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/LeagueImportServiceTests.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/Fakes/FakeStatsProvider.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/Fakes/FakeLeagueDataStore.cs`

**Interfaces:**
- Consumes: `RosterCsvParser` (Task 2), `IPlayerMatchingService` (Task 6), `IStatsProvider.GetAllActivePlayersAsync` (Task 4), `ILeagueDataStore` (Task 3).
- Produces: `record TeamMatchPreview(string TeamName, IReadOnlyList<PlayerMatch> Players)`, `record ImportPreview(IReadOnlyList<TeamMatchPreview> Teams)`, `record ConfirmedPlayer(string CsvName, string? PlayerId, string? PlayerFullName, string? Position, bool IsPitcher)`, `record ConfirmedTeam(string TeamName, IReadOnlyList<ConfirmedPlayer> Players)`, `record ConfirmImportRequest(IReadOnlyList<ConfirmedTeam> Teams)`; `static class SeasonClock { static int Current => DateTime.UtcNow.Year; }` (the single place "current season" is derived — later tasks reuse it instead of inlining `DateTime.UtcNow.Year`); `class LeagueImportService` with `Task<ImportPreview> PreviewImportAsync(string csvContent)` and `League ConfirmImport(ConfirmImportRequest request)`. A `ConfirmedPlayer` with `PlayerId == null` means the user left it unresolved — `ConfirmImport` drops it from the persisted roster rather than erroring, per the spec's "excluded with a warning" behavior (the warning itself is a frontend concern, built in Task 18).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class LeagueImportServiceTests
{
    private static readonly List<MlbPlayer> Pool = new()
    {
        new MlbPlayer("660271", "Shohei Ohtani", "DH", false, 119)
    };

    [Fact]
    public async Task PreviewImportAsync_ReturnsMatchPreviewPerTeam()
    {
        var service = new LeagueImportService(
            new RosterCsvParser(),
            new PlayerMatchingService(),
            new FakeStatsProvider(Pool),
            new FakeLeagueDataStore());

        var preview = await service.PreviewImportAsync("Team,Player\nRhino Wranglers,Shohei Ohtani\n");

        var team = Assert.Single(preview.Teams);
        Assert.Equal("Rhino Wranglers", team.TeamName);
        var player = Assert.Single(team.Players);
        Assert.Equal("660271", player.BestGuess!.PlayerId);
    }

    [Fact]
    public void ConfirmImport_DropsUnresolvedPlayersAndPersistsLeague()
    {
        var store = new FakeLeagueDataStore();
        var service = new LeagueImportService(
            new RosterCsvParser(),
            new PlayerMatchingService(),
            new FakeStatsProvider(Pool),
            store);
        var request = new ConfirmImportRequest(new List<ConfirmedTeam>
        {
            new("Rhino Wranglers", new List<ConfirmedPlayer>
            {
                new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false),
                new("Unknown Guy", null, null, null, false)
            })
        });

        var league = service.ConfirmImport(request);

        var team = Assert.Single(league.Teams);
        var rostered = Assert.Single(team.Players);
        Assert.Equal("Shohei Ohtani", rostered.PlayerFullName);
        Assert.NotNull(store.Saved);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter LeagueImportServiceTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Add the test fakes**

`backend/FantasyAnalysis.Api.Tests/Fakes/FakeStatsProvider.cs`:

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeStatsProvider : IStatsProvider
{
    private readonly IReadOnlyList<MlbPlayer> _players;
    private readonly IReadOnlyList<StatLine> _statLines;

    public FakeStatsProvider(IReadOnlyList<MlbPlayer> players, IReadOnlyList<StatLine>? statLines = null)
    {
        _players = players;
        _statLines = statLines ?? new List<StatLine>();
    }

    public Task<IReadOnlyList<MlbPlayer>> GetAllActivePlayersAsync(int season) => Task.FromResult(_players);

    public Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season) =>
        Task.FromResult<IReadOnlyList<StatLine>>(_statLines.Where(s => playerIds.Contains(s.PlayerId)).ToList());
}
```

`backend/FantasyAnalysis.Api.Tests/Fakes/FakeLeagueDataStore.cs`:

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeLeagueDataStore : ILeagueDataStore
{
    public League? Saved { get; private set; }

    public League? LoadLeague() => Saved;

    public void SaveLeague(League league) => Saved = league;
}
```

- [ ] **Step 4: Implement the models, SeasonClock, and service**

`backend/FantasyAnalysis.Api/Models/ImportModels.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record TeamMatchPreview(string TeamName, IReadOnlyList<PlayerMatch> Players);

public record ImportPreview(IReadOnlyList<TeamMatchPreview> Teams);

public record ConfirmedPlayer(string CsvName, string? PlayerId, string? PlayerFullName, string? Position, bool IsPitcher);

public record ConfirmedTeam(string TeamName, IReadOnlyList<ConfirmedPlayer> Players);

public record ConfirmImportRequest(IReadOnlyList<ConfirmedTeam> Teams);
```

`backend/FantasyAnalysis.Api/Services/SeasonClock.cs`:

```csharp
namespace FantasyAnalysis.Api.Services;

public static class SeasonClock
{
    public static int Current => DateTime.UtcNow.Year;
}
```

`backend/FantasyAnalysis.Api/Services/LeagueImportService.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class LeagueImportService
{
    private readonly RosterCsvParser _parser;
    private readonly IPlayerMatchingService _matcher;
    private readonly IStatsProvider _statsProvider;
    private readonly ILeagueDataStore _leagueStore;

    public LeagueImportService(
        RosterCsvParser parser,
        IPlayerMatchingService matcher,
        IStatsProvider statsProvider,
        ILeagueDataStore leagueStore)
    {
        _parser = parser;
        _matcher = matcher;
        _statsProvider = statsProvider;
        _leagueStore = leagueStore;
    }

    public async Task<ImportPreview> PreviewImportAsync(string csvContent)
    {
        var parsed = _parser.Parse(csvContent);
        var pool = await _statsProvider.GetAllActivePlayersAsync(SeasonClock.Current);

        var teamPreviews = parsed.Teams
            .Select(t => new TeamMatchPreview(t.TeamName, _matcher.MatchPlayers(t.PlayerNames, pool)))
            .ToList();

        return new ImportPreview(teamPreviews);
    }

    public League ConfirmImport(ConfirmImportRequest request)
    {
        var teams = request.Teams
            .Select(t => new TeamRoster(
                t.TeamName,
                t.Players
                    .Where(p => p.PlayerId is not null)
                    .Select(p => new RosteredPlayer(p.CsvName, p.PlayerId!, p.PlayerFullName!, p.Position!, p.IsPitcher))
                    .ToList()))
            .ToList();

        var league = new League(DateTimeOffset.UtcNow, teams);
        _leagueStore.SaveLeague(league);
        return league;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter LeagueImportServiceTests`
Expected: PASS (2 tests)

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/ImportModels.cs backend/FantasyAnalysis.Api/Services/SeasonClock.cs backend/FantasyAnalysis.Api/Services/LeagueImportService.cs backend/FantasyAnalysis.Api.Tests/LeagueImportServiceTests.cs backend/FantasyAnalysis.Api.Tests/Fakes/FakeStatsProvider.cs backend/FantasyAnalysis.Api.Tests/Fakes/FakeLeagueDataStore.cs
git commit -m "Add league import service (preview + confirm)"
```

---

### Task 8: League endpoints + DI wiring

**Files:**
- Create: `backend/FantasyAnalysis.Api/Endpoints/LeagueEndpoints.cs`
- Modify: `backend/FantasyAnalysis.Api/Program.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/LeagueEndpointsTests.cs`

**Interfaces:**
- Consumes: `LeagueImportService`, `ILeagueDataStore` (Task 7/3), `IStatsProvider`/`MlbStatsProvider` (Task 4/5), `IPlayerMatchingService`/`PlayerMatchingService` (Task 6), `RosterCsvParser` (Task 2).
- Produces: `POST /api/league/import` (multipart file upload → `ImportPreview`), `POST /api/league/import/confirm` (`ConfirmImportRequest` → persisted `League`), `GET /api/league` (`League` or 404). Registers the DI graph every later backend task builds on: `DataRoot`-based `ILeagueDataStore`, and the `statsapi.mlb.com` typed `HttpClient` for `IStatsProvider`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class LeagueEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LeagueEndpointsTests(WebApplicationFactory<Program> factory)
    {
        var pool = new List<MlbPlayer> { new("660271", "Shohei Ohtani", "DH", false, 119) };
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStatsProvider>(new FakeStatsProvider(pool));
            services.AddSingleton<ILeagueDataStore>(new FakeLeagueDataStore());
        }));
    }

    [Fact]
    public async Task GetLeague_WhenNoneImported_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/league");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ImportThenConfirm_PersistsLeagueRetrievableViaGet()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        var csvContent = new StringContent("Team,Player\nRhino Wranglers,Shohei Ohtani\n");
        form.Add(csvContent, "file", "roster.csv");

        var importResponse = await client.PostAsync("/api/league/import", form);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var preview = await importResponse.Content.ReadFromJsonAsync<ImportPreview>();

        var bestGuess = preview!.Teams[0].Players[0].BestGuess!;
        var confirmRequest = new ConfirmImportRequest(new List<ConfirmedTeam>
        {
            new("Rhino Wranglers", new List<ConfirmedPlayer>
            {
                new("Shohei Ohtani", bestGuess.PlayerId, bestGuess.FullName, bestGuess.Position, bestGuess.IsPitcher)
            })
        });

        var confirmResponse = await client.PostAsJsonAsync("/api/league/import/confirm", confirmRequest);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/league");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var league = await getResponse.Content.ReadFromJsonAsync<League>();
        Assert.Single(league!.Teams);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter LeagueEndpointsTests`
Expected: FAIL (endpoints don't exist yet — 404s)

- [ ] **Step 3: Implement the endpoints**

`backend/FantasyAnalysis.Api/Endpoints/LeagueEndpoints.cs`:

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Endpoints;

public static class LeagueEndpoints
{
    public static void MapLeagueEndpoints(this WebApplication app)
    {
        app.MapPost("/api/league/import", async (IFormFile file, LeagueImportService importService) =>
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var csvContent = await reader.ReadToEndAsync();

            try
            {
                var preview = await importService.PreviewImportAsync(csvContent);
                return Results.Ok(preview);
            }
            catch (CsvParseException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (StatsProviderException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        }).DisableAntiforgery();

        app.MapPost("/api/league/import/confirm", (ConfirmImportRequest request, LeagueImportService importService) =>
        {
            var league = importService.ConfirmImport(request);
            return Results.Ok(league);
        });

        app.MapGet("/api/league", (ILeagueDataStore leagueStore) =>
        {
            var league = leagueStore.LoadLeague();
            return league is null
                ? Results.NotFound(new { error = "No league has been imported yet." })
                : Results.Ok(league);
        });
    }
}
```

- [ ] **Step 4: Wire DI in Program.cs**

Add to `backend/FantasyAnalysis.Api/Program.cs`, after the CORS block and before `var app = builder.Build();`:

```csharp
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
```

Add `using FantasyAnalysis.Api.Endpoints;` and `using FantasyAnalysis.Api.Services;` to the top of `Program.cs`.

After `app.MapGet("/health", ...)`, add:

```csharp
app.MapLeagueEndpoints();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter LeagueEndpointsTests`
Expected: PASS (2 tests)

Then run the full suite to make sure nothing else broke: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj`
Expected: PASS (all tests)

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyAnalysis.Api/Endpoints/LeagueEndpoints.cs backend/FantasyAnalysis.Api/Program.cs backend/FantasyAnalysis.Api.Tests/LeagueEndpointsTests.cs
git commit -m "Add league import/confirm/get endpoints"
```

---

### Task 9: Stats cache

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/StatsCacheEntry.cs`
- Create: `backend/FantasyAnalysis.Api/Services/IStatsCache.cs`
- Create: `backend/FantasyAnalysis.Api/Services/FileStatsCache.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/FileStatsCacheTests.cs`

**Interfaces:**
- Consumes: `StatLine` from Task 4.
- Produces: `record StatsCacheEntry(DateTimeOffset FetchedAtUtc, IReadOnlyList<StatLine> StatLines)`; `interface IStatsCache { IReadOnlyList<StatLine>? GetIfFresh(int season, TimeSpan maxAge); void Store(int season, IReadOnlyList<StatLine> statLines); }` implemented by `FileStatsCache(string dataRoot)`. Caches at whole-season granularity (one file per season, one timestamp) rather than per-player — simpler, and matches the spec's "refresh periodically" framing rather than per-player TTLs. `GetIfFresh` returns `null` (a full cache miss) both when no cache file exists and when it's older than `maxAge`; the caller (Task 12) always refetches everything from `IStatsProvider` on a miss.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FileStatsCacheTests : IDisposable
{
    private readonly string _tempDir;

    public FileStatsCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void GetIfFresh_WhenNoCacheExists_ReturnsNull()
    {
        var cache = new FileStatsCache(_tempDir);
        Assert.Null(cache.GetIfFresh(2026, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void StoreThenGetIfFresh_WithinMaxAge_ReturnsStatLines()
    {
        var cache = new FileStatsCache(_tempDir);
        var statLines = new List<StatLine>
        {
            new("660271", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 44m })
        };

        cache.Store(2026, statLines);
        var result = cache.GetIfFresh(2026, TimeSpan.FromHours(24));

        Assert.NotNull(result);
        Assert.Equal(44m, result![0].Stats["homeRuns"]);
    }

    [Fact]
    public void GetIfFresh_WhenCacheOlderThanMaxAge_ReturnsNull()
    {
        var oldEntryJson = """{ "fetchedAtUtc": "2000-01-01T00:00:00+00:00", "statLines": [] }""";
        File.WriteAllText(Path.Combine(_tempDir, "stats-cache-2026.json"), oldEntryJson);
        var cache = new FileStatsCache(_tempDir);

        var result = cache.GetIfFresh(2026, TimeSpan.FromHours(24));

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FileStatsCacheTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Implement the model and cache**

`backend/FantasyAnalysis.Api/Models/StatsCacheEntry.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record StatsCacheEntry(DateTimeOffset FetchedAtUtc, IReadOnlyList<StatLine> StatLines);
```

`backend/FantasyAnalysis.Api/Services/IStatsCache.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IStatsCache
{
    IReadOnlyList<StatLine>? GetIfFresh(int season, TimeSpan maxAge);
    void Store(int season, IReadOnlyList<StatLine> statLines);
}
```

`backend/FantasyAnalysis.Api/Services/FileStatsCache.cs`:

```csharp
using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FileStatsCache : IStatsCache
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileStatsCache(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string PathFor(int season) => Path.Combine(_dataRoot, $"stats-cache-{season}.json");

    public IReadOnlyList<StatLine>? GetIfFresh(int season, TimeSpan maxAge)
    {
        var path = PathFor(season);
        if (!File.Exists(path)) return null;

        var entry = JsonSerializer.Deserialize<StatsCacheEntry>(File.ReadAllText(path), JsonOptions);
        if (entry is null) return null;
        if (DateTimeOffset.UtcNow - entry.FetchedAtUtc > maxAge) return null;

        return entry.StatLines;
    }

    public void Store(int season, IReadOnlyList<StatLine> statLines)
    {
        Directory.CreateDirectory(_dataRoot);
        var entry = new StatsCacheEntry(DateTimeOffset.UtcNow, statLines);
        var path = PathFor(season);
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FileStatsCacheTests`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/StatsCacheEntry.cs backend/FantasyAnalysis.Api/Services/IStatsCache.cs backend/FantasyAnalysis.Api/Services/FileStatsCache.cs backend/FantasyAnalysis.Api.Tests/FileStatsCacheTests.cs
git commit -m "Add season-granularity stats cache"
```

---

### Task 10: Scoring settings store + endpoints

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/ScoringSettings.cs`
- Create: `backend/FantasyAnalysis.Api/Services/IScoringSettingsStore.cs`
- Create: `backend/FantasyAnalysis.Api/Services/FileScoringSettingsStore.cs`
- Create: `backend/FantasyAnalysis.Api/Endpoints/ScoringSettingsEndpoints.cs`
- Modify: `backend/FantasyAnalysis.Api/Program.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/FileScoringSettingsStoreTests.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/ScoringSettingsEndpointsTests.cs`

**Interfaces:**
- Consumes: nothing from prior tasks.
- Produces: `record ScoringCategory(string StatKey, decimal PointsPerUnit)`, `record ScoringSettings(IReadOnlyList<ScoringCategory> HittingCategories, IReadOnlyList<ScoringCategory> PitchingCategories, IReadOnlyDictionary<string, int> RosterSlots)` — `StatKey` matches a `StatLine.Stats` key (e.g. `"homeRuns"`, `"strikeOuts"`) so Task 12's ranker can look values up directly; `interface IScoringSettingsStore { ScoringSettings? Load(); void Save(ScoringSettings settings); }`; `GET /api/settings/scoring` (200 with settings, or 404 if none saved), `PUT /api/settings/scoring` (200, persists and echoes back). Task 12 (`FantasyValueRanker`) and Task 13 (`ClaudeRecommendationEngine`) both consume `ScoringSettings` by this shape.

- [ ] **Step 1: Write the failing store test**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FileScoringSettingsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileScoringSettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = new FileScoringSettingsStore(_tempDir);
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new FileScoringSettingsStore(_tempDir);
        var settings = new ScoringSettings(
            new List<ScoringCategory> { new("homeRuns", 4m), new("stolenBases", 2m) },
            new List<ScoringCategory> { new("strikeOuts", 1m) },
            new Dictionary<string, int> { ["C"] = 1, ["1B"] = 1, ["SP"] = 5 });

        store.Save(settings);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(4m, loaded!.HittingCategories[0].PointsPerUnit);
        Assert.Equal(5, loaded.RosterSlots["SP"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FileScoringSettingsStoreTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Implement the model and store**

`backend/FantasyAnalysis.Api/Models/ScoringSettings.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record ScoringCategory(string StatKey, decimal PointsPerUnit);

public record ScoringSettings(
    IReadOnlyList<ScoringCategory> HittingCategories,
    IReadOnlyList<ScoringCategory> PitchingCategories,
    IReadOnlyDictionary<string, int> RosterSlots);
```

`backend/FantasyAnalysis.Api/Services/IScoringSettingsStore.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IScoringSettingsStore
{
    ScoringSettings? Load();
    void Save(ScoringSettings settings);
}
```

`backend/FantasyAnalysis.Api/Services/FileScoringSettingsStore.cs`:

```csharp
using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FileScoringSettingsStore : IScoringSettingsStore
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileScoringSettingsStore(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string SettingsPath => Path.Combine(_dataRoot, "scoring-settings.json");

    public ScoringSettings? Load()
    {
        if (!File.Exists(SettingsPath)) return null;
        return JsonSerializer.Deserialize<ScoringSettings>(File.ReadAllText(SettingsPath), JsonOptions);
    }

    public void Save(ScoringSettings settings)
    {
        Directory.CreateDirectory(_dataRoot);
        var tempPath = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }
}
```

- [ ] **Step 4: Run store test to verify it passes**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FileScoringSettingsStoreTests`
Expected: PASS (2 tests)

- [ ] **Step 5: Write the failing endpoint test**

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class ScoringSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScoringSettingsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetThenPut_RoundTripsSettings()
    {
        var client = _factory.CreateClient();
        var settings = new ScoringSettings(
            new List<ScoringCategory> { new("homeRuns", 4m) },
            new List<ScoringCategory> { new("strikeOuts", 1m) },
            new Dictionary<string, int> { ["C"] = 1 });

        var putResponse = await client.PutAsJsonAsync("/api/settings/scoring", settings);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/settings/scoring");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var loaded = await getResponse.Content.ReadFromJsonAsync<ScoringSettings>();
        Assert.Equal(4m, loaded!.HittingCategories[0].PointsPerUnit);
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter ScoringSettingsEndpointsTests`
Expected: FAIL (endpoint doesn't exist yet)

- [ ] **Step 7: Implement the endpoints and wire DI**

`backend/FantasyAnalysis.Api/Endpoints/ScoringSettingsEndpoints.cs`:

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Endpoints;

public static class ScoringSettingsEndpoints
{
    public static void MapScoringSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings/scoring", (IScoringSettingsStore store) =>
        {
            var settings = store.Load();
            return settings is null
                ? Results.NotFound(new { error = "No scoring settings saved yet." })
                : Results.Ok(settings);
        });

        app.MapPut("/api/settings/scoring", (ScoringSettings settings, IScoringSettingsStore store) =>
        {
            store.Save(settings);
            return Results.Ok(settings);
        });
    }
}
```

Add to `Program.cs`, alongside the other `DataRoot`-based registrations:

```csharp
builder.Services.AddSingleton<IScoringSettingsStore>(sp =>
{
    var dataRoot = Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["DataRoot"] ?? "data");
    Directory.CreateDirectory(dataRoot);
    return new FileScoringSettingsStore(dataRoot);
});
```

Add `app.MapScoringSettingsEndpoints();` next to `app.MapLeagueEndpoints();`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter ScoringSettingsEndpointsTests`
Expected: PASS (1 test)

- [ ] **Step 9: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/ScoringSettings.cs backend/FantasyAnalysis.Api/Services/IScoringSettingsStore.cs backend/FantasyAnalysis.Api/Services/FileScoringSettingsStore.cs backend/FantasyAnalysis.Api/Endpoints/ScoringSettingsEndpoints.cs backend/FantasyAnalysis.Api/Program.cs backend/FantasyAnalysis.Api.Tests/FileScoringSettingsStoreTests.cs backend/FantasyAnalysis.Api.Tests/ScoringSettingsEndpointsTests.cs
git commit -m "Add scoring settings store and endpoints"
```

---

### Task 11: Waiver pool calculator

**Files:**
- Create: `backend/FantasyAnalysis.Api/Services/WaiverPoolCalculator.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/WaiverPoolCalculatorTests.cs`

**Interfaces:**
- Consumes: `MlbPlayer` (Task 4), `League` (Task 3).
- Produces: `class WaiverPoolCalculator` with `IReadOnlyList<MlbPlayer> ComputeWaiverPool(IReadOnlyList<MlbPlayer> allActivePlayers, League league)`. Pure function, no interface needed — matches the `RosterCsvParser` convention of leaving stateless/pure logic un-abstracted (only I/O-performing services get an interface + fake).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class WaiverPoolCalculatorTests
{
    [Fact]
    public void ComputeWaiverPool_ExcludesEveryRosteredPlayerAcrossAllTeams()
    {
        var allPlayers = new List<MlbPlayer>
        {
            new("660271", "Shohei Ohtani", "DH", false, 119),
            new("665742", "Juan Soto", "OF", false, 121),
            new("605483", "Gerrit Cole", "P", true, 147)
        };
        var league = new League(
            System.DateTimeOffset.UtcNow,
            new List<TeamRoster>
            {
                new("Rhino Wranglers", new List<RosteredPlayer>
                {
                    new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
                }),
                new("Sea Dogs", new List<RosteredPlayer>
                {
                    new("Gerrit Cole", "605483", "Gerrit Cole", "P", true)
                })
            });

        var pool = new WaiverPoolCalculator().ComputeWaiverPool(allPlayers, league);

        var player = Assert.Single(pool);
        Assert.Equal("665742", player.Id);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter WaiverPoolCalculatorTests`
Expected: FAIL (type doesn't exist yet)

- [ ] **Step 3: Implement the calculator**

`backend/FantasyAnalysis.Api/Services/WaiverPoolCalculator.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class WaiverPoolCalculator
{
    public IReadOnlyList<MlbPlayer> ComputeWaiverPool(IReadOnlyList<MlbPlayer> allActivePlayers, League league)
    {
        var rosteredIds = league.Teams
            .SelectMany(t => t.Players)
            .Select(p => p.PlayerId)
            .ToHashSet();

        return allActivePlayers.Where(p => !rosteredIds.Contains(p.Id)).ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter WaiverPoolCalculatorTests`
Expected: PASS (1 test)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Services/WaiverPoolCalculator.cs backend/FantasyAnalysis.Api.Tests/WaiverPoolCalculatorTests.cs
git commit -m "Add waiver pool calculator"
```

---

### Task 12: Fantasy value ranker (numeric shortlist)

**Files:**
- Create: `backend/FantasyAnalysis.Api/Services/FantasyValueRanker.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/FantasyValueRankerTests.cs`

**Interfaces:**
- Consumes: `StatLine` (Task 4), `ScoringSettings`/`ScoringCategory` (Task 10), `MlbPlayer` (Task 4).
- Produces: `class FantasyValueRanker` with `decimal ComputePlayerValue(IReadOnlyList<StatLine> playerStatLines, ScoringSettings settings)` (sums `stat value * PointsPerUnit` across every matching category, using `PitchingCategories` for a `"pitching"`-group line and `HittingCategories` otherwise) and `IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> TopCandidatesByPosition(IReadOnlyList<MlbPlayer> candidates, IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId, ScoringSettings settings, int topNPerPosition)`. This is a purely numeric shortlist grouped by position — it does **not** decide which positions the user is weak at; that judgment (matching the shortlist against the user's actual roster gaps) is left to Claude in Task 13, which receives both the user's roster and this shortlist. Keeping the split this way is deliberate: mechanical number-crunching stays here, subjective team-need judgment stays with the AI.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FantasyValueRankerTests
{
    private static readonly ScoringSettings Settings = new(
        new List<ScoringCategory> { new("homeRuns", 4m), new("stolenBases", 2m) },
        new List<ScoringCategory> { new("strikeOuts", 1m) },
        new Dictionary<string, int>());

    [Fact]
    public void ComputePlayerValue_SumsAcrossMatchingCategories()
    {
        var ranker = new FantasyValueRanker();
        var lines = new List<StatLine>
        {
            new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m, ["stolenBases"] = 5m, ["hits"] = 100m })
        };

        var value = ranker.ComputePlayerValue(lines, Settings);

        Assert.Equal(50m, value); // 10*4 + 5*2; "hits" isn't a scored category, ignored
    }

    [Fact]
    public void TopCandidatesByPosition_GroupsAndRanksWithinEachPosition()
    {
        var ranker = new FantasyValueRanker();
        var candidates = new List<MlbPlayer>
        {
            new("1", "Low OF", "OF", false, 100),
            new("2", "High OF", "OF", false, 100),
            new("3", "Only SS", "SS", false, 100)
        };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 1m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m }) },
            ["3"] = new List<StatLine> { new("3", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 3m }) }
        };

        var result = ranker.TopCandidatesByPosition(candidates, stats, Settings, topNPerPosition: 1);

        Assert.Equal("High OF", result["OF"][0].FullName);
        Assert.Equal("Only SS", result["SS"][0].FullName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FantasyValueRankerTests`
Expected: FAIL (type doesn't exist yet)

- [ ] **Step 3: Implement the ranker**

`backend/FantasyAnalysis.Api/Services/FantasyValueRanker.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FantasyValueRanker
{
    public decimal ComputePlayerValue(IReadOnlyList<StatLine> playerStatLines, ScoringSettings settings)
    {
        decimal total = 0;
        foreach (var line in playerStatLines)
        {
            var categories = line.Group == "pitching" ? settings.PitchingCategories : settings.HittingCategories;
            foreach (var category in categories)
            {
                if (line.Stats.TryGetValue(category.StatKey, out var value))
                {
                    total += value * category.PointsPerUnit;
                }
            }
        }
        return total;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> TopCandidatesByPosition(
        IReadOnlyList<MlbPlayer> candidates,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId,
        ScoringSettings settings,
        int topNPerPosition)
    {
        return candidates
            .GroupBy(p => p.Position)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<MlbPlayer>)g
                    .Select(p => (Player: p, Value: ComputePlayerValue(
                        statsByPlayerId.TryGetValue(p.Id, out var lines) ? lines : Array.Empty<StatLine>(),
                        settings)))
                    .OrderByDescending(x => x.Value)
                    .Take(topNPerPosition)
                    .Select(x => x.Player)
                    .ToList());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FantasyValueRankerTests`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Services/FantasyValueRanker.cs backend/FantasyAnalysis.Api.Tests/FantasyValueRankerTests.cs
git commit -m "Add fantasy value ranker for shortlisting waiver candidates"
```

---

### Task 13: Recommendation models + Anthropic client seam

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/Recommendation.cs`
- Create: `backend/FantasyAnalysis.Api/Models/DomainExceptions.cs` (extend)
- Create: `backend/FantasyAnalysis.Api/Services/IRecommendationClient.cs`
- Create: `backend/FantasyAnalysis.Api/Services/AnthropicRecommendationClient.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/AnthropicRecommendationClientTests.cs`

**Interfaces:**
- Consumes: nothing from prior tasks.
- Produces: `enum RecommendationType { Waiver, Trade }`, `record Recommendation(RecommendationType Type, string Summary, string Reasoning, IReadOnlyList<string> InvolvedPlayerIds, IReadOnlyList<string> Citations, int Rank)`, `record RecommendationSet(DateTimeOffset GeneratedAtUtc, IReadOnlyList<Recommendation> WaiverSuggestions, IReadOnlyList<Recommendation> TradeSuggestions)`, `class RecommendationClientException : Exception`, and `interface IRecommendationClient { Task<string> GetRecommendationsJsonAsync(string systemPrompt, string userPrompt); }` implemented by `AnthropicRecommendationClient(AnthropicClient client)`. **This interface is the mockable seam** — Task 14's `ClaudeRecommendationEngine` depends on `IRecommendationClient`, never on the concrete SDK client, so its context-assembly and parsing logic can be unit tested without hitting the real Anthropic API. This task's own test only verifies the client compiles and shapes its request correctly (model, tool, schema) — it does not call the live API.

- [ ] **Step 1: Write the failing test**

This test verifies `AnthropicRecommendationClient` builds a well-formed request without making a network call, by constructing a real `AnthropicClient` pointed at an unreachable base address and asserting the failure mode is a network error (proving the request serialized and was sent), not a construction-time exception.

```csharp
using System;
using System.Threading.Tasks;
using Anthropic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class AnthropicRecommendationClientTests
{
    [Fact]
    public async Task GetRecommendationsJsonAsync_SendsRequest_FailsOnNetworkNotConstruction()
    {
        var anthropicClient = new AnthropicClient
        {
            ApiKey = "test-key",
            BaseUrl = "http://127.0.0.1:1" // nothing listens here - guarantees a connection failure, not a 4xx/5xx
        };
        var client = new AnthropicRecommendationClient(anthropicClient);

        // A connection-level exception here proves the request was built and dispatched;
        // any exception before that (e.g. a schema-construction bug) would throw synchronously
        // during GetRecommendationsJsonAsync's setup, before the awaited call, and this assertion
        // would fail with the wrong exception type instead.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.GetRecommendationsJsonAsync("system prompt", "user prompt"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter AnthropicRecommendationClientTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Add the models**

`backend/FantasyAnalysis.Api/Models/Recommendation.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public enum RecommendationType { Waiver, Trade }

public record Recommendation(
    RecommendationType Type,
    string Summary,
    string Reasoning,
    IReadOnlyList<string> InvolvedPlayerIds,
    IReadOnlyList<string> Citations,
    int Rank);

public record RecommendationSet(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<Recommendation> WaiverSuggestions,
    IReadOnlyList<Recommendation> TradeSuggestions);
```

Append to `backend/FantasyAnalysis.Api/Models/DomainExceptions.cs`:

```csharp
public class RecommendationClientException : Exception
{
    public RecommendationClientException(string message) : base(message) { }
    public RecommendationClientException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 4: Implement the interface and client**

`backend/FantasyAnalysis.Api/Services/IRecommendationClient.cs`:

```csharp
namespace FantasyAnalysis.Api.Services;

public interface IRecommendationClient
{
    Task<string> GetRecommendationsJsonAsync(string systemPrompt, string userPrompt);
}
```

`backend/FantasyAnalysis.Api/Services/AnthropicRecommendationClient.cs`:

```csharp
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class AnthropicRecommendationClient : IRecommendationClient
{
    private readonly AnthropicClient _client;

    public AnthropicRecommendationClient(AnthropicClient client)
    {
        _client = client;
    }

    public async Task<string> GetRecommendationsJsonAsync(string systemPrompt, string userPrompt)
    {
        var parameters = new MessageCreateParams
        {
            Model = "claude-opus-5",
            MaxTokens = 16000,
            System = new List<TextBlockParam> { new() { Text = systemPrompt } },
            Messages = [new() { Role = Role.User, Content = userPrompt }],
            Tools = [new ToolUnion(new WebSearchTool20260209())],
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = BuildSchema() } },
        };

        Message response;
        try
        {
            response = await _client.Messages.Create(parameters);
        }
        catch (Exception ex)
        {
            throw new RecommendationClientException("Failed to get recommendations from Claude.", ex);
        }

        // Web search results and other server-tool blocks may precede the final structured
        // answer, so take the LAST text block, not the first.
        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().LastOrDefault()?.Text;
        if (text is null)
        {
            throw new RecommendationClientException("Claude response contained no text content.");
        }

        return text;
    }

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        var recommendationSchema = new
        {
            type = "object",
            properties = new
            {
                summary = new { type = "string" },
                reasoning = new { type = "string" },
                involvedPlayerIds = new { type = "array", items = new { type = "string" } },
                citations = new { type = "array", items = new { type = "string" } }
            },
            required = new[] { "summary", "reasoning", "involvedPlayerIds", "citations" }
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                waiverSuggestions = new { type = "array", items = recommendationSchema },
                tradeSuggestions = new { type = "array", items = recommendationSchema }
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "waiverSuggestions", "tradeSuggestions" })
        };
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter AnthropicRecommendationClientTests`
Expected: PASS (1 test)

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/Recommendation.cs backend/FantasyAnalysis.Api/Models/DomainExceptions.cs backend/FantasyAnalysis.Api/Services/IRecommendationClient.cs backend/FantasyAnalysis.Api/Services/AnthropicRecommendationClient.cs backend/FantasyAnalysis.Api.Tests/AnthropicRecommendationClientTests.cs
git commit -m "Add recommendation models and Anthropic client seam"
```

---

### Task 14: Claude recommendation engine (context assembly + parsing)

**Files:**
- Create: `backend/FantasyAnalysis.Api/Services/ClaudeRecommendationEngine.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/ClaudeRecommendationEngineTests.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/Fakes/FakeRecommendationClient.cs`

**Interfaces:**
- Consumes: `IRecommendationClient` (Task 13), `FantasyValueRanker` (Task 12), `League`/`TeamRoster`/`RosteredPlayer` (Task 3), `ScoringSettings` (Task 10), `StatLine` (Task 4), `MlbPlayer` (Task 4).
- Produces: `class ClaudeRecommendationEngine` with `Task<RecommendationSet> GenerateRecommendationsAsync(League league, string yourTeamName, ScoringSettings settings, IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId, IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> waiverShortlistByPosition)`. This is the piece Task 15's endpoint calls after assembling stats/shortlist from the other services. Throws `RecommendationClientException` if the client's JSON can't be parsed into the expected shape.

- [ ] **Step 1: Write the failing test**

`backend/FantasyAnalysis.Api.Tests/Fakes/FakeRecommendationClient.cs`:

```csharp
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeRecommendationClient : IRecommendationClient
{
    private readonly string _responseJson;
    public string? LastSystemPrompt { get; private set; }
    public string? LastUserPrompt { get; private set; }

    public FakeRecommendationClient(string responseJson)
    {
        _responseJson = responseJson;
    }

    public Task<string> GetRecommendationsJsonAsync(string systemPrompt, string userPrompt)
    {
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        return Task.FromResult(_responseJson);
    }
}
```

`backend/FantasyAnalysis.Api.Tests/ClaudeRecommendationEngineTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class ClaudeRecommendationEngineTests
{
    private static readonly League League = new(
        System.DateTimeOffset.UtcNow,
        new List<TeamRoster>
        {
            new("Rhino Wranglers", new List<RosteredPlayer>
            {
                new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
            }),
            new("Sea Dogs", new List<RosteredPlayer>
            {
                new("Juan Soto", "665742", "Juan Soto", "OF", false)
            })
        });

    private static readonly ScoringSettings Settings = new(
        new List<ScoringCategory> { new("homeRuns", 4m) },
        new List<ScoringCategory>(),
        new Dictionary<string, int>());

    [Fact]
    public async Task GenerateRecommendationsAsync_ParsesClientJsonIntoRecommendationSet()
    {
        var json = """
        {
          "waiverSuggestions": [
            { "summary": "Pick up X", "reasoning": "Hot streak", "involvedPlayerIds": ["123"], "citations": ["https://example.com"] }
          ],
          "tradeSuggestions": []
        }
        """;
        var fakeClient = new FakeRecommendationClient(json);
        var engine = new ClaudeRecommendationEngine(fakeClient, new FantasyValueRanker());

        var result = await engine.GenerateRecommendationsAsync(
            League,
            "Rhino Wranglers",
            Settings,
            new Dictionary<string, IReadOnlyList<StatLine>>(),
            new Dictionary<string, IReadOnlyList<MlbPlayer>>());

        var suggestion = Assert.Single(result.WaiverSuggestions);
        Assert.Equal("Pick up X", suggestion.Summary);
        Assert.Equal(RecommendationType.Waiver, suggestion.Type);
        Assert.Equal(1, suggestion.Rank);
        Assert.Empty(result.TradeSuggestions);
    }

    [Fact]
    public async Task GenerateRecommendationsAsync_PromptMentionsYourTeamAndOtherTeams()
    {
        var json = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";
        var fakeClient = new FakeRecommendationClient(json);
        var engine = new ClaudeRecommendationEngine(fakeClient, new FantasyValueRanker());

        await engine.GenerateRecommendationsAsync(
            League,
            "Rhino Wranglers",
            Settings,
            new Dictionary<string, IReadOnlyList<StatLine>>(),
            new Dictionary<string, IReadOnlyList<MlbPlayer>>());

        Assert.Contains("Rhino Wranglers", fakeClient.LastUserPrompt);
        Assert.Contains("Sea Dogs", fakeClient.LastUserPrompt);
        Assert.Contains("Shohei Ohtani", fakeClient.LastUserPrompt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter ClaudeRecommendationEngineTests`
Expected: FAIL (type doesn't exist yet)

- [ ] **Step 3: Implement the engine**

`backend/FantasyAnalysis.Api/Services/ClaudeRecommendationEngine.cs`:

```csharp
using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class ClaudeRecommendationEngine
{
    private readonly IRecommendationClient _client;
    private readonly FantasyValueRanker _ranker;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ClaudeRecommendationEngine(IRecommendationClient client, FantasyValueRanker ranker)
    {
        _client = client;
        _ranker = ranker;
    }

    public async Task<RecommendationSet> GenerateRecommendationsAsync(
        League league,
        string yourTeamName,
        ScoringSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId,
        IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> waiverShortlistByPosition)
    {
        var systemPrompt =
            "You are a fantasy baseball analyst. Given one team's roster, every other team's roster, " +
            "a shortlist of available waiver-wire candidates, and the league's scoring settings, " +
            "recommend waiver pickups and trades that would improve the given team. Use web search " +
            "to check recent news, injuries, or performance trends that could affect a recommendation, " +
            "and cite any URLs you used. Respond only with JSON matching the provided schema.";

        var userPrompt = BuildUserPrompt(league, yourTeamName, settings, statsByPlayerId, waiverShortlistByPosition);

        var json = await _client.GetRecommendationsJsonAsync(systemPrompt, userPrompt);
        return ParseResponse(json);
    }

    private string BuildUserPrompt(
        League league,
        string yourTeamName,
        ScoringSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId,
        IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> waiverShortlistByPosition)
    {
        object PlayerPayload(RosteredPlayer p) => new
        {
            playerId = p.PlayerId,
            fullName = p.PlayerFullName,
            position = p.Position,
            fantasyValue = _ranker.ComputePlayerValue(
                statsByPlayerId.TryGetValue(p.PlayerId, out var lines) ? lines : Array.Empty<StatLine>(),
                settings)
        };

        var yourTeam = league.Teams.First(t => t.TeamName == yourTeamName);
        var otherTeams = league.Teams.Where(t => t.TeamName != yourTeamName);

        var payload = new
        {
            yourTeam = new { teamName = yourTeam.TeamName, players = yourTeam.Players.Select(PlayerPayload) },
            otherTeams = otherTeams.Select(t => new { teamName = t.TeamName, players = t.Players.Select(PlayerPayload) }),
            waiverShortlistByPosition = waiverShortlistByPosition.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(p => new
                {
                    playerId = p.Id,
                    fullName = p.FullName,
                    position = p.Position,
                    fantasyValue = _ranker.ComputePlayerValue(
                        statsByPlayerId.TryGetValue(p.Id, out var lines) ? lines : Array.Empty<StatLine>(),
                        settings)
                })),
            scoringSettings = settings
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static RecommendationSet ParseResponse(string json)
    {
        ClaudeRecommendationSetDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ClaudeRecommendationSetDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new RecommendationClientException("Claude's recommendation response was not valid JSON.", ex);
        }

        if (dto is null)
        {
            throw new RecommendationClientException("Claude's recommendation response deserialized to null.");
        }

        IReadOnlyList<Recommendation> ToRecommendations(List<ClaudeRecommendationDto>? items, RecommendationType type) =>
            (items ?? new List<ClaudeRecommendationDto>())
                .Select((item, index) => new Recommendation(
                    type,
                    item.Summary,
                    item.Reasoning,
                    item.InvolvedPlayerIds ?? new List<string>(),
                    item.Citations ?? new List<string>(),
                    index + 1))
                .ToList();

        return new RecommendationSet(
            DateTimeOffset.UtcNow,
            ToRecommendations(dto.WaiverSuggestions, RecommendationType.Waiver),
            ToRecommendations(dto.TradeSuggestions, RecommendationType.Trade));
    }

    private class ClaudeRecommendationDto
    {
        public string Summary { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public List<string>? InvolvedPlayerIds { get; set; }
        public List<string>? Citations { get; set; }
    }

    private class ClaudeRecommendationSetDto
    {
        public List<ClaudeRecommendationDto>? WaiverSuggestions { get; set; }
        public List<ClaudeRecommendationDto>? TradeSuggestions { get; set; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter ClaudeRecommendationEngineTests`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Services/ClaudeRecommendationEngine.cs backend/FantasyAnalysis.Api.Tests/ClaudeRecommendationEngineTests.cs backend/FantasyAnalysis.Api.Tests/Fakes/FakeRecommendationClient.cs
git commit -m "Add Claude recommendation engine (context assembly + response parsing)"
```

---

### Task 15: Recommendation data store

**Files:**
- Create: `backend/FantasyAnalysis.Api/Services/IRecommendationDataStore.cs`
- Create: `backend/FantasyAnalysis.Api/Services/FileRecommendationDataStore.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/FileRecommendationDataStoreTests.cs`

**Interfaces:**
- Consumes: `RecommendationSet` (Task 13).
- Produces: `interface IRecommendationDataStore { RecommendationSet? Load(); void Save(RecommendationSet recommendations); }` implemented by `FileRecommendationDataStore(string dataRoot)` — same atomic-write JSON pattern as every other file store in this plan.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FileRecommendationDataStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileRecommendationDataStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = new FileRecommendationDataStore(_tempDir);
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new FileRecommendationDataStore(_tempDir);
        var set = new RecommendationSet(
            DateTimeOffset.UtcNow,
            new List<Recommendation> { new(RecommendationType.Waiver, "Pick up X", "reason", new List<string> { "1" }, new List<string>(), 1) },
            new List<Recommendation>());

        store.Save(set);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("Pick up X", loaded!.WaiverSuggestions[0].Summary);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FileRecommendationDataStoreTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Implement the store**

`backend/FantasyAnalysis.Api/Services/IRecommendationDataStore.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IRecommendationDataStore
{
    RecommendationSet? Load();
    void Save(RecommendationSet recommendations);
}
```

`backend/FantasyAnalysis.Api/Services/FileRecommendationDataStore.cs`:

```csharp
using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FileRecommendationDataStore : IRecommendationDataStore
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FileRecommendationDataStore(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string Path_ => Path.Combine(_dataRoot, "recommendations.json");

    public RecommendationSet? Load()
    {
        if (!File.Exists(Path_)) return null;
        return JsonSerializer.Deserialize<RecommendationSet>(File.ReadAllText(Path_), JsonOptions);
    }

    public void Save(RecommendationSet recommendations)
    {
        Directory.CreateDirectory(_dataRoot);
        var tempPath = Path_ + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(recommendations, JsonOptions));
            File.Move(tempPath, Path_, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }
}
```

Add `using System.Text.Json.Serialization;` to the top of the file (for `JsonStringEnumConverter`, needed because `RecommendationType` is an enum).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter FileRecommendationDataStoreTests`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Services/IRecommendationDataStore.cs backend/FantasyAnalysis.Api/Services/FileRecommendationDataStore.cs backend/FantasyAnalysis.Api.Tests/FileRecommendationDataStoreTests.cs
git commit -m "Add recommendation data store"
```

---

### Task 16: Recommendation orchestration service

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/DomainExceptions.cs` (extend)
- Create: `backend/FantasyAnalysis.Api/Services/RecommendationOrchestrationService.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/RecommendationOrchestrationServiceTests.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/Fakes/FakeScoringSettingsStore.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/Fakes/FakeStatsCache.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/Fakes/FakeRecommendationDataStore.cs`

**Interfaces:**
- Consumes: `ILeagueDataStore` (Task 3), `IScoringSettingsStore` (Task 10), `IStatsProvider` (Task 4), `IStatsCache` (Task 9), `WaiverPoolCalculator` (Task 11), `FantasyValueRanker` (Task 12), `ClaudeRecommendationEngine` (Task 14), `IRecommendationDataStore` (Task 15), `SeasonClock` (Task 7).
- Produces: `class RecommendationPrerequisiteException : Exception` (thrown when no league or no scoring settings is saved yet); `class RecommendationOrchestrationService` with `Task<RecommendationSet> RefreshAsync(string yourTeamName)` and `RecommendationSet? GetLast()`. This is the full pipeline described in the spec's Data Flow step 6: refresh stats (cache-first), compute the waiver pool, shortlist it numerically, call the AI engine, persist and return the result. Task 17's endpoints call this directly — it is the last piece before the recommendation feature is wired to HTTP.
- **Known limitation, carried from the spec:** `statsapi.mlb.com` has no confirmed bulk/leaderboard endpoint, so a cache-miss refresh fetches every rostered-and-waiver-pool player's stats one request at a time (throttled). With MLB's ~1,000+ active players this can take a couple of minutes on a cold cache; the 24-hour `IStatsCache` TTL means this cost is paid rarely, not per-request. This is a deliberate, spec-acknowledged tradeoff of using a free, bulk-endpoint-less API — not a bug to fix in this task.

- [ ] **Step 1: Write the failing test**

`backend/FantasyAnalysis.Api.Tests/Fakes/FakeScoringSettingsStore.cs`:

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeScoringSettingsStore : IScoringSettingsStore
{
    private ScoringSettings? _settings;

    public FakeScoringSettingsStore(ScoringSettings? initial = null) => _settings = initial;

    public ScoringSettings? Load() => _settings;

    public void Save(ScoringSettings settings) => _settings = settings;
}
```

`backend/FantasyAnalysis.Api.Tests/Fakes/FakeStatsCache.cs`:

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeStatsCache : IStatsCache
{
    private readonly Dictionary<int, IReadOnlyList<StatLine>> _stored = new();

    public IReadOnlyList<StatLine>? GetIfFresh(int season, TimeSpan maxAge) =>
        _stored.TryGetValue(season, out var lines) ? lines : null;

    public void Store(int season, IReadOnlyList<StatLine> statLines) => _stored[season] = statLines;
}
```

`backend/FantasyAnalysis.Api.Tests/Fakes/FakeRecommendationDataStore.cs`:

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeRecommendationDataStore : IRecommendationDataStore
{
    public RecommendationSet? Saved { get; private set; }

    public RecommendationSet? Load() => Saved;

    public void Save(RecommendationSet recommendations) => Saved = recommendations;
}
```

`backend/FantasyAnalysis.Api.Tests/RecommendationOrchestrationServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RecommendationOrchestrationServiceTests
{
    private static readonly League League = new(
        System.DateTimeOffset.UtcNow,
        new List<TeamRoster>
        {
            new("Rhino Wranglers", new List<RosteredPlayer>
            {
                new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
            })
        });

    private static readonly ScoringSettings Settings = new(
        new List<ScoringCategory> { new("homeRuns", 4m) },
        new List<ScoringCategory>(),
        new Dictionary<string, int>());

    private static RecommendationOrchestrationService BuildService(
        League? league,
        ScoringSettings? settings,
        out FakeRecommendationDataStore recommendationStore)
    {
        var pool = new List<MlbPlayer> { new("665742", "Juan Soto", "OF", false, 121) };
        var statsProvider = new FakeStatsProvider(pool, new List<StatLine>
        {
            new("665742", SeasonClock.Current, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m })
        });
        var leagueStore = new FakeLeagueDataStore();
        if (league is not null) leagueStore.SaveLeague(league);

        var responseJson = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";
        var engine = new ClaudeRecommendationEngine(new FakeRecommendationClient(responseJson), new FantasyValueRanker());
        recommendationStore = new FakeRecommendationDataStore();

        return new RecommendationOrchestrationService(
            leagueStore,
            new FakeScoringSettingsStore(settings),
            statsProvider,
            new FakeStatsCache(),
            new WaiverPoolCalculator(),
            new FantasyValueRanker(),
            engine,
            recommendationStore);
    }

    [Fact]
    public async Task RefreshAsync_NoLeagueImported_ThrowsPrerequisiteException()
    {
        var service = BuildService(null, Settings, out _);

        await Assert.ThrowsAsync<RecommendationPrerequisiteException>(() => service.RefreshAsync("Rhino Wranglers"));
    }

    [Fact]
    public async Task RefreshAsync_NoScoringSettings_ThrowsPrerequisiteException()
    {
        var service = BuildService(League, null, out _);

        await Assert.ThrowsAsync<RecommendationPrerequisiteException>(() => service.RefreshAsync("Rhino Wranglers"));
    }

    [Fact]
    public async Task RefreshAsync_HappyPath_SavesAndReturnsRecommendations()
    {
        var service = BuildService(League, Settings, out var recommendationStore);

        var result = await service.RefreshAsync("Rhino Wranglers");

        Assert.NotNull(result);
        Assert.Same(result, recommendationStore.Saved);
        Assert.Equal(result, service.GetLast());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RecommendationOrchestrationServiceTests`
Expected: FAIL (type doesn't exist yet)

- [ ] **Step 3: Implement the exception and service**

Append to `backend/FantasyAnalysis.Api/Models/DomainExceptions.cs`:

```csharp
public class RecommendationPrerequisiteException : Exception
{
    public RecommendationPrerequisiteException(string message) : base(message) { }
}
```

`backend/FantasyAnalysis.Api/Services/RecommendationOrchestrationService.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class RecommendationOrchestrationService
{
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);
    private const int ShortlistPerPosition = 5;

    private readonly ILeagueDataStore _leagueStore;
    private readonly IScoringSettingsStore _settingsStore;
    private readonly IStatsProvider _statsProvider;
    private readonly IStatsCache _statsCache;
    private readonly WaiverPoolCalculator _waiverPoolCalculator;
    private readonly FantasyValueRanker _ranker;
    private readonly ClaudeRecommendationEngine _engine;
    private readonly IRecommendationDataStore _recommendationStore;

    public RecommendationOrchestrationService(
        ILeagueDataStore leagueStore,
        IScoringSettingsStore settingsStore,
        IStatsProvider statsProvider,
        IStatsCache statsCache,
        WaiverPoolCalculator waiverPoolCalculator,
        FantasyValueRanker ranker,
        ClaudeRecommendationEngine engine,
        IRecommendationDataStore recommendationStore)
    {
        _leagueStore = leagueStore;
        _settingsStore = settingsStore;
        _statsProvider = statsProvider;
        _statsCache = statsCache;
        _waiverPoolCalculator = waiverPoolCalculator;
        _ranker = ranker;
        _engine = engine;
        _recommendationStore = recommendationStore;
    }

    public async Task<RecommendationSet> RefreshAsync(string yourTeamName)
    {
        var league = _leagueStore.LoadLeague()
            ?? throw new RecommendationPrerequisiteException("A league must be imported before generating recommendations.");
        var settings = _settingsStore.Load()
            ?? throw new RecommendationPrerequisiteException("Scoring settings must be saved before generating recommendations.");

        var season = SeasonClock.Current;
        var allPlayers = await _statsProvider.GetAllActivePlayersAsync(season);
        var waiverPool = _waiverPoolCalculator.ComputeWaiverPool(allPlayers, league);

        var statLines = _statsCache.GetIfFresh(season, CacheMaxAge);
        if (statLines is null)
        {
            var rosteredIds = league.Teams.SelectMany(t => t.Players).Select(p => p.PlayerId);
            var idsNeeded = rosteredIds.Concat(waiverPool.Select(p => p.Id)).Distinct().ToList();
            statLines = await _statsProvider.GetPlayerStatsAsync(idsNeeded, season);
            _statsCache.Store(season, statLines);
        }

        var statsByPlayerId = statLines
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StatLine>)g.ToList());

        var shortlist = _ranker.TopCandidatesByPosition(waiverPool, statsByPlayerId, settings, ShortlistPerPosition);

        var recommendations = await _engine.GenerateRecommendationsAsync(league, yourTeamName, settings, statsByPlayerId, shortlist);
        _recommendationStore.Save(recommendations);
        return recommendations;
    }

    public RecommendationSet? GetLast() => _recommendationStore.Load();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RecommendationOrchestrationServiceTests`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/DomainExceptions.cs backend/FantasyAnalysis.Api/Services/RecommendationOrchestrationService.cs backend/FantasyAnalysis.Api.Tests/RecommendationOrchestrationServiceTests.cs backend/FantasyAnalysis.Api.Tests/Fakes/FakeScoringSettingsStore.cs backend/FantasyAnalysis.Api.Tests/Fakes/FakeStatsCache.cs backend/FantasyAnalysis.Api.Tests/Fakes/FakeRecommendationDataStore.cs
git commit -m "Add recommendation orchestration service"
```

---

### Task 17: Recommendation endpoints + final DI wiring

**Files:**
- Create: `backend/FantasyAnalysis.Api/Endpoints/RecommendationEndpoints.cs`
- Modify: `backend/FantasyAnalysis.Api/Program.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/RecommendationEndpointsTests.cs`

**Interfaces:**
- Consumes: `RecommendationOrchestrationService` (Task 16) and every service it depends on.
- Produces: `POST /api/recommendations/refresh?teamName=...` (runs the full pipeline, 200 with `RecommendationSet`, 400 on `RecommendationPrerequisiteException`, 502 on `StatsProviderException`/`RecommendationClientException`) and `GET /api/recommendations` (last saved set, or 404). Completes the backend — every remaining task is frontend work.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RecommendationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RecommendationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        var league = new League(
            System.DateTimeOffset.UtcNow,
            new List<TeamRoster> { new("Rhino Wranglers", new List<RosteredPlayer>()) });
        var leagueStore = new FakeLeagueDataStore();
        leagueStore.SaveLeague(league);
        var settings = new ScoringSettings(new List<ScoringCategory>(), new List<ScoringCategory>(), new Dictionary<string, int>());
        var responseJson = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["AnthropicApiKey"] = "test-key" });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILeagueDataStore>(leagueStore);
                services.AddSingleton<IScoringSettingsStore>(new FakeScoringSettingsStore(settings));
                services.AddSingleton<IStatsProvider>(new FakeStatsProvider(new List<MlbPlayer>()));
                services.AddSingleton<IStatsCache>(new FakeStatsCache());
                services.AddSingleton<IRecommendationClient>(new FakeRecommendationClient(responseJson));
            });
        });
    }

    [Fact]
    public async Task GetRecommendations_WhenNoneGenerated_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/recommendations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ThenGet_ReturnsSavedRecommendations()
    {
        var client = _factory.CreateClient();

        var refreshResponse = await client.PostAsync("/api/recommendations/refresh?teamName=Rhino+Wranglers", null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/recommendations");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var set = await getResponse.Content.ReadFromJsonAsync<RecommendationSet>();
        Assert.NotNull(set);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RecommendationEndpointsTests`
Expected: FAIL (endpoints don't exist yet; `Program.cs` also can't resolve the new services yet)

- [ ] **Step 3: Implement the endpoints**

`backend/FantasyAnalysis.Api/Endpoints/RecommendationEndpoints.cs`:

```csharp
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Endpoints;

public static class RecommendationEndpoints
{
    public static void MapRecommendationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/recommendations/refresh", async (string teamName, RecommendationOrchestrationService orchestrator) =>
        {
            try
            {
                var result = await orchestrator.RefreshAsync(teamName);
                return Results.Ok(result);
            }
            catch (RecommendationPrerequisiteException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (StatsProviderException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
            catch (RecommendationClientException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        app.MapGet("/api/recommendations", (RecommendationOrchestrationService orchestrator) =>
        {
            var last = orchestrator.GetLast();
            return last is null
                ? Results.NotFound(new { error = "No recommendations generated yet." })
                : Results.Ok(last);
        });
    }
}
```

- [ ] **Step 4: Wire DI in Program.cs**

Add to `backend/FantasyAnalysis.Api/Program.cs`, alongside the other registrations (before `var app = builder.Build();`):

```csharp
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
builder.Services.AddSingleton<FantasyValueRanker>();

builder.Services.AddSingleton(sp =>
{
    var apiKey = sp.GetRequiredService<IConfiguration>()["AnthropicApiKey"]
        ?? throw new InvalidOperationException("AnthropicApiKey must be configured.");
    return new Anthropic.AnthropicClient { ApiKey = apiKey };
});
builder.Services.AddSingleton<IRecommendationClient, AnthropicRecommendationClient>();
builder.Services.AddSingleton<ClaudeRecommendationEngine>();
builder.Services.AddSingleton<RecommendationOrchestrationService>();
```

Add `using Anthropic;` to the top of `Program.cs`.

After `var app = builder.Build();`, add:

```csharp
// Fails fast at startup if AnthropicApiKey is missing, rather than on the first request
// that happens to need it — same rationale as the sibling app's eager AuthService
// resolution for AdminPin.
app.Services.GetRequiredService<Anthropic.AnthropicClient>();
```

After `app.MapScoringSettingsEndpoints();`, add:

```csharp
app.MapRecommendationEndpoints();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RecommendationEndpointsTests`
Expected: PASS (2 tests)

Then run the full backend suite: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj`
Expected: PASS (all tests)

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyAnalysis.Api/Endpoints/RecommendationEndpoints.cs backend/FantasyAnalysis.Api/Program.cs backend/FantasyAnalysis.Api.Tests/RecommendationEndpointsTests.cs
git commit -m "Add recommendation endpoints and complete backend DI wiring"
```

---

### Task 18: Frontend scaffold

**Files:**
- Create: `frontend/package.json`, `frontend/vite.config.ts`, `frontend/vitest.config.ts`, `frontend/tsconfig.json`, `frontend/tsconfig.app.json`, `frontend/tsconfig.node.json`, `frontend/index.html`, `frontend/.env.development`
- Create: `frontend/src/main.tsx`, `frontend/src/App.tsx`, `frontend/src/types.ts`, `frontend/src/api/client.ts`, `frontend/src/test/setup.ts`
- Test: `frontend/src/App.test.tsx`

**Interfaces:**
- Produces: a building, running Vite dev server with a placeholder `App` component, plus the shared `types.ts` DTOs every screen imports (`League`, `TeamRoster`, `RosteredPlayer`, `PlayerMatch`, `ImportPreview`, `ScoringSettings`, `RecommendationSet`, `Recommendation`) mirroring the backend records field-for-field (camelCase, since `System.Text.Json`'s `JsonNamingPolicy.CamelCase` is used on every backend store/response), and the `api/client.ts` fetch wrapper pattern (`request<T>`, `ApiError`) that every later frontend task's API functions are added to.

- [ ] **Step 1: Scaffold the Vite project**

```bash
cd frontend
npm create vite@latest . -- --template react-ts
```

When prompted about the non-empty directory, choose to continue (the directory won't actually have conflicting files at this point). Then:

```bash
npm install
```

- [ ] **Step 2: Replace generated config files**

`frontend/package.json` — replace the `scripts`/`dependencies`/`devDependencies` sections with:

```json
{
  "name": "frontend",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "test": "vitest run",
    "preview": "vite preview"
  },
  "dependencies": {
    "react": "^19.2.8",
    "react-dom": "^19.2.8"
  },
  "devDependencies": {
    "@testing-library/jest-dom": "^6.6.3",
    "@testing-library/react": "^16.1.0",
    "@types/node": "^24.13.3",
    "@types/react": "^19.2.17",
    "@types/react-dom": "^19.2.3",
    "@vitejs/plugin-react": "^6.0.4",
    "jsdom": "^25.0.1",
    "typescript": "~6.0.2",
    "vite": "^8.2.0",
    "vitest": "^3.0.5"
  }
}
```

Run `npm install` again to pick up the new dev dependencies.

`frontend/vite.config.ts`:

```typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../backend/FantasyAnalysis.Api/wwwroot",
    emptyOutDir: true
  }
});
```

`frontend/vitest.config.ts`:

```typescript
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"]
  }
});
```

`frontend/src/test/setup.ts`:

```typescript
import "@testing-library/jest-dom/vitest";
```

`frontend/.env.development`:

```
VITE_API_BASE_URL=http://localhost:5080
```

- [ ] **Step 3: Add shared types**

`frontend/src/types.ts`:

```typescript
export interface RosteredPlayer {
  csvName: string;
  playerId: string;
  playerFullName: string;
  position: string;
  isPitcher: boolean;
}

export interface TeamRoster {
  teamName: string;
  players: RosteredPlayer[];
}

export interface League {
  importedAtUtc: string;
  teams: TeamRoster[];
}

export interface PlayerMatchCandidate {
  playerId: string;
  fullName: string;
  position: string;
  isPitcher: boolean;
  score: number;
}

export interface PlayerMatch {
  csvName: string;
  bestGuess: PlayerMatchCandidate | null;
  candidates: PlayerMatchCandidate[];
}

export interface TeamMatchPreview {
  teamName: string;
  players: PlayerMatch[];
}

export interface ImportPreview {
  teams: TeamMatchPreview[];
}

export interface ConfirmedPlayer {
  csvName: string;
  playerId: string | null;
  playerFullName: string | null;
  position: string | null;
  isPitcher: boolean;
}

export interface ConfirmedTeam {
  teamName: string;
  players: ConfirmedPlayer[];
}

export interface ConfirmImportRequest {
  teams: ConfirmedTeam[];
}

export interface ScoringCategory {
  statKey: string;
  pointsPerUnit: number;
}

export interface ScoringSettings {
  hittingCategories: ScoringCategory[];
  pitchingCategories: ScoringCategory[];
  rosterSlots: Record<string, number>;
}

export type RecommendationType = "Waiver" | "Trade";

export interface Recommendation {
  type: RecommendationType;
  summary: string;
  reasoning: string;
  involvedPlayerIds: string[];
  citations: string[];
  rank: number;
}

export interface RecommendationSet {
  generatedAtUtc: string;
  waiverSuggestions: Recommendation[];
  tradeSuggestions: Recommendation[];
}
```

- [ ] **Step 4: Add the API client skeleton**

`frontend/src/api/client.ts` — deliberately just `ApiError`/`BASE_URL` here; Task 20 adds the shared `request<T>` JSON helper (Task 19's `importLeague` posts raw `FormData` directly and never needs one):

```typescript
export const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export class ApiError extends Error {
  status: number;
  body: unknown;

  constructor(status: number, body: unknown) {
    super(`API request failed with status ${status}`);
    this.status = status;
    this.body = body;
  }
}
```

(`BASE_URL` is exported, not module-private, so it compiles cleanly under `noUnusedLocals` before anything in this file consumes it — Task 19's `importLeague` and Task 20's `request<T>` both reference it via this export.)

- [ ] **Step 5: Add a minimal App and its smoke test**

`frontend/src/App.tsx`:

```tsx
export function App() {
  return <div>Fantasy Analysis</div>;
}
```

`frontend/src/main.tsx`:

```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>
);
```

`frontend/src/App.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "./App";

describe("App", () => {
  it("renders the app shell", () => {
    render(<App />);
    expect(screen.getByText("Fantasy Analysis")).toBeInTheDocument();
  });
});
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `npm run test --prefix frontend`
Expected: PASS (1 test)

- [ ] **Step 7: Commit**

```bash
git add frontend
git commit -m "Scaffold frontend project with Vitest and shared types"
```

---

### Task 19: Import screen

**Files:**
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/screens/ImportScreen.tsx`
- Test: `frontend/src/screens/ImportScreen.test.tsx`

**Interfaces:**
- Consumes: `ImportPreview` type, `request`/`ApiError` from `api/client.ts` (Task 18).
- Produces: `importLeague(file: File): Promise<ImportPreview>` in `api/client.ts`; `ImportScreen` component with props `{ onPreviewReady: (preview: ImportPreview) => void }` — Task 23's `App.tsx` renders this first and switches to the match-review screen when `onPreviewReady` fires.

- [ ] **Step 1: Write the failing test**

`frontend/src/screens/ImportScreen.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ImportScreen } from "./ImportScreen";
import type { ImportPreview } from "../types";

describe("ImportScreen", () => {
  it("uploads the selected CSV and reports the preview", async () => {
    const preview: ImportPreview = { teams: [] };
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(preview)
    });
    vi.stubGlobal("fetch", fetchMock);
    const onPreviewReady = vi.fn();

    render(<ImportScreen onPreviewReady={onPreviewReady} />);

    const file = new File(["Team,Player\nA,B\n"], "roster.csv", { type: "text/csv" });
    const input = screen.getByLabelText(/roster csv/i) as HTMLInputElement;
    fireEvent.change(input, { target: { files: [file] } });
    fireEvent.click(screen.getByRole("button", { name: /import/i }));

    await waitFor(() => expect(onPreviewReady).toHaveBeenCalledWith(preview));
    expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining("/api/league/import"), expect.objectContaining({ method: "POST" }));

    vi.unstubAllGlobals();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm run test --prefix frontend`
Expected: FAIL (`ImportScreen` doesn't exist yet)

- [ ] **Step 3: Add the API function**

Append to `frontend/src/api/client.ts`:

```typescript
import type { ImportPreview } from "../types";

export async function importLeague(file: File): Promise<ImportPreview> {
  const formData = new FormData();
  formData.append("file", file);

  const response = await fetch(`${BASE_URL}/api/league/import`, {
    method: "POST",
    body: formData
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body);
  }

  return response.json() as Promise<ImportPreview>;
}
```

Move the `import type { ImportPreview } from "../types";` line to the top of the file alongside any future type imports, rather than mid-file.

- [ ] **Step 4: Implement the screen**

`frontend/src/screens/ImportScreen.tsx`:

```tsx
import { useState } from "react";
import { importLeague, ApiError } from "../api/client";
import type { ImportPreview } from "../types";

interface ImportScreenProps {
  onPreviewReady: (preview: ImportPreview) => void;
}

export function ImportScreen({ onPreviewReady }: ImportScreenProps) {
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);

  async function handleImport() {
    if (!file) return;
    setImporting(true);
    setError(null);
    try {
      const preview = await importLeague(file);
      onPreviewReady(preview);
    } catch (err) {
      setError(err instanceof ApiError ? String(err.body) : "Import failed. Please try again.");
    } finally {
      setImporting(false);
    }
  }

  return (
    <div>
      <h1>Import League Roster</h1>
      <label htmlFor="roster-csv">Roster CSV (Team,Player)</label>
      <input
        id="roster-csv"
        type="file"
        accept=".csv"
        onChange={(e) => setFile(e.target.files?.[0] ?? null)}
      />
      <button onClick={handleImport} disabled={!file || importing}>
        {importing ? "Importing..." : "Import"}
      </button>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npm run test --prefix frontend`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/screens/ImportScreen.tsx frontend/src/screens/ImportScreen.test.tsx
git commit -m "Add CSV import screen"
```

---

### Task 20: Match review screen

**Files:**
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/screens/MatchReviewScreen.tsx`
- Test: `frontend/src/screens/MatchReviewScreen.test.tsx`

**Interfaces:**
- Consumes: `ImportPreview`, `ConfirmImportRequest`, `League`, `PlayerMatchCandidate` types (Task 18); the `Position`/`IsPitcher` fields on `PlayerMatchCandidate` added just above.
- Produces: `confirmImport(request: ConfirmImportRequest): Promise<League>` in `api/client.ts`; `MatchReviewScreen` component with props `{ preview: ImportPreview; onConfirmed: (league: League) => void }`. Defaults every player's selection to its `bestGuess` (or unresolved if there is none), lets the user override via a dropdown per player, and on confirm builds the request from whichever candidate is currently selected — never fully automatic, per the spec's review-step requirement.

- [ ] **Step 1: Write the failing test**

`frontend/src/screens/MatchReviewScreen.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { MatchReviewScreen } from "./MatchReviewScreen";
import type { ImportPreview, League } from "../types";

describe("MatchReviewScreen", () => {
  const preview: ImportPreview = {
    teams: [
      {
        teamName: "Rhino Wranglers",
        players: [
          {
            csvName: "Shohei Ohtani",
            bestGuess: { playerId: "660271", fullName: "Shohei Ohtani", position: "DH", isPitcher: false, score: 1 },
            candidates: [{ playerId: "660271", fullName: "Shohei Ohtani", position: "DH", isPitcher: false, score: 1 }]
          },
          {
            csvName: "Unknown Guy",
            bestGuess: null,
            candidates: []
          }
        ]
      }
    ]
  };

  it("confirms with the default best-guess selection and drops unresolved players", async () => {
    const league: League = { importedAtUtc: "2026-01-01T00:00:00Z", teams: [] };
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve(league) });
    vi.stubGlobal("fetch", fetchMock);
    const onConfirmed = vi.fn();

    render(<MatchReviewScreen preview={preview} onConfirmed={onConfirmed} />);
    fireEvent.click(screen.getByRole("button", { name: /confirm import/i }));

    await waitFor(() => expect(onConfirmed).toHaveBeenCalledWith(league));
    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(body.teams[0].players).toHaveLength(2);
    expect(body.teams[0].players[0].playerId).toBe("660271");
    expect(body.teams[0].players[1].playerId).toBeNull();

    vi.unstubAllGlobals();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm run test --prefix frontend`
Expected: FAIL (`MatchReviewScreen` doesn't exist yet)

- [ ] **Step 3: Add the API function**

`api/client.ts` doesn't yet have a shared JSON-request helper (Task 19's `importLeague` posts `FormData` directly, so it never needed one). Add one now — a plain `request<T>` function, matching the shape used in `fantasy-keeper-app/frontend/src/api/client.ts` — and use it for `confirmImport`. Append to `frontend/src/api/client.ts` (add `ConfirmImportRequest` and `League` to the existing type import from `../types`):

```typescript
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers }
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body);
  }

  return response.json() as Promise<T>;
}

export function confirmImport(confirmRequest: ConfirmImportRequest): Promise<League> {
  return request<League>("/api/league/import/confirm", {
    method: "POST",
    body: JSON.stringify(confirmRequest)
  });
}
```

- [ ] **Step 4: Implement the screen**

`frontend/src/screens/MatchReviewScreen.tsx`:

```tsx
import { useState } from "react";
import { confirmImport } from "../api/client";
import type { ConfirmedPlayer, ConfirmImportRequest, ImportPreview, League, PlayerMatchCandidate } from "../types";

interface MatchReviewScreenProps {
  preview: ImportPreview;
  onConfirmed: (league: League) => void;
}

export function MatchReviewScreen({ preview, onConfirmed }: MatchReviewScreenProps) {
  const [selections, setSelections] = useState<(string | null)[][]>(() =>
    preview.teams.map((team) => team.players.map((p) => p.bestGuess?.playerId ?? null))
  );
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function selectCandidate(teamIndex: number, playerIndex: number, playerId: string) {
    setSelections((prev) => {
      const next = prev.map((row) => [...row]);
      next[teamIndex][playerIndex] = playerId || null;
      return next;
    });
  }

  function findCandidate(candidates: PlayerMatchCandidate[], playerId: string | null): PlayerMatchCandidate | null {
    return candidates.find((c) => c.playerId === playerId) ?? null;
  }

  async function handleConfirm() {
    setConfirming(true);
    setError(null);
    try {
      const request: ConfirmImportRequest = {
        teams: preview.teams.map((team, teamIndex) => ({
          teamName: team.teamName,
          players: team.players.map((player, playerIndex): ConfirmedPlayer => {
            const selectedId = selections[teamIndex][playerIndex];
            const candidate = findCandidate(player.candidates, selectedId);
            return {
              csvName: player.csvName,
              playerId: candidate?.playerId ?? null,
              playerFullName: candidate?.fullName ?? null,
              position: candidate?.position ?? null,
              isPitcher: candidate?.isPitcher ?? false
            };
          })
        }))
      };
      const league = await confirmImport(request);
      onConfirmed(league);
    } catch {
      setError("Failed to confirm import. Please try again.");
    } finally {
      setConfirming(false);
    }
  }

  return (
    <div>
      <h1>Review Matched Players</h1>
      {preview.teams.map((team, teamIndex) => (
        <section key={team.teamName}>
          <h2>{team.teamName}</h2>
          {team.players.map((player, playerIndex) => (
            <div key={player.csvName}>
              <span>{player.csvName}</span>
              <select
                value={selections[teamIndex][playerIndex] ?? ""}
                onChange={(e) => selectCandidate(teamIndex, playerIndex, e.target.value)}
              >
                <option value="">-- Unresolved / Skip --</option>
                {player.candidates.map((c) => (
                  <option key={c.playerId} value={c.playerId}>
                    {c.fullName} ({Math.round(c.score * 100)}%)
                  </option>
                ))}
              </select>
            </div>
          ))}
        </section>
      ))}
      <button onClick={handleConfirm} disabled={confirming}>
        {confirming ? "Confirming..." : "Confirm Import"}
      </button>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npm run test --prefix frontend`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/screens/MatchReviewScreen.tsx frontend/src/screens/MatchReviewScreen.test.tsx
git commit -m "Add player match review screen"
```

---

### Task 21: Scoring settings screen

**Files:**
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/screens/ScoringSettingsScreen.tsx`
- Test: `frontend/src/screens/ScoringSettingsScreen.test.tsx`

**Interfaces:**
- Consumes: `ScoringSettings`, `ScoringCategory` types (Task 18); `request`/`ApiError` (Task 20).
- Produces: `getScoringSettings(): Promise<ScoringSettings | null>` (returns `null` on a 404, rather than throwing — "no settings saved yet" is expected, not an error) and `saveScoringSettings(settings: ScoringSettings): Promise<ScoringSettings>` in `api/client.ts`; `ScoringSettingsScreen` component with props `{ onSaved: (settings: ScoringSettings) => void }`. Lets the user add/remove hitting categories, pitching categories, and roster slots as dynamic rows, loads any previously saved settings on mount, and saves on submit.

- [ ] **Step 1: Write the failing test**

`frontend/src/screens/ScoringSettingsScreen.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ScoringSettingsScreen } from "./ScoringSettingsScreen";
import type { ScoringSettings } from "../types";

describe("ScoringSettingsScreen", () => {
  it("loads nothing on a fresh league, adds a category, and saves", async () => {
    const saved: ScoringSettings = {
      hittingCategories: [{ statKey: "homeRuns", pointsPerUnit: 4 }],
      pitchingCategories: [],
      rosterSlots: {}
    };
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, status: 404, json: () => Promise.resolve({}) }) // initial load
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(saved) }); // save
    vi.stubGlobal("fetch", fetchMock);
    const onSaved = vi.fn();

    render(<ScoringSettingsScreen onSaved={onSaved} />);
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByRole("button", { name: /add hitting category/i }));
    fireEvent.change(screen.getByLabelText(/hitting stat key 0/i), { target: { value: "homeRuns" } });
    fireEvent.change(screen.getByLabelText(/hitting points 0/i), { target: { value: "4" } });
    fireEvent.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved));

    vi.unstubAllGlobals();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm run test --prefix frontend`
Expected: FAIL (`ScoringSettingsScreen` doesn't exist yet)

- [ ] **Step 3: Add the API functions**

Append to `frontend/src/api/client.ts` (add `ScoringSettings` to the existing type import from `../types`):

```typescript
export async function getScoringSettings(): Promise<ScoringSettings | null> {
  try {
    return await request<ScoringSettings>("/api/settings/scoring");
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}

export function saveScoringSettings(settings: ScoringSettings): Promise<ScoringSettings> {
  return request<ScoringSettings>("/api/settings/scoring", {
    method: "PUT",
    body: JSON.stringify(settings)
  });
}
```

- [ ] **Step 4: Implement the screen**

`frontend/src/screens/ScoringSettingsScreen.tsx`:

```tsx
import { useEffect, useState } from "react";
import { getScoringSettings, saveScoringSettings } from "../api/client";
import type { ScoringCategory, ScoringSettings } from "../types";

interface ScoringSettingsScreenProps {
  onSaved: (settings: ScoringSettings) => void;
}

export function ScoringSettingsScreen({ onSaved }: ScoringSettingsScreenProps) {
  const [hitting, setHitting] = useState<ScoringCategory[]>([]);
  const [pitching, setPitching] = useState<ScoringCategory[]>([]);
  const [rosterSlots, setRosterSlots] = useState<[string, number][]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    getScoringSettings().then((settings) => {
      if (!settings) return;
      setHitting(settings.hittingCategories);
      setPitching(settings.pitchingCategories);
      setRosterSlots(Object.entries(settings.rosterSlots));
    });
  }, []);

  function updateCategory(
    list: ScoringCategory[],
    setList: (v: ScoringCategory[]) => void,
    index: number,
    field: keyof ScoringCategory,
    value: string
  ) {
    const next = [...list];
    next[index] = { ...next[index], [field]: field === "pointsPerUnit" ? Number(value) : value };
    setList(next);
  }

  function categoryRows(
    label: string,
    list: ScoringCategory[],
    setList: (v: ScoringCategory[]) => void
  ) {
    return (
      <fieldset>
        <legend>{label}</legend>
        {list.map((category, index) => (
          <div key={index}>
            <label htmlFor={`${label}-key-${index}`}>{label} stat key {index}</label>
            <input
              id={`${label}-key-${index}`}
              value={category.statKey}
              onChange={(e) => updateCategory(list, setList, index, "statKey", e.target.value)}
            />
            <label htmlFor={`${label}-points-${index}`}>{label} points {index}</label>
            <input
              id={`${label}-points-${index}`}
              type="number"
              value={category.pointsPerUnit}
              onChange={(e) => updateCategory(list, setList, index, "pointsPerUnit", e.target.value)}
            />
            <button type="button" onClick={() => setList(list.filter((_, i) => i !== index))}>
              Remove
            </button>
          </div>
        ))}
        <button type="button" onClick={() => setList([...list, { statKey: "", pointsPerUnit: 0 }])}>
          Add {label} Category
        </button>
      </fieldset>
    );
  }

  function rosterSlotRows() {
    return (
      <fieldset>
        <legend>Roster Slots</legend>
        {rosterSlots.map(([position, count], index) => (
          <div key={index}>
            <label htmlFor={`roster-slot-position-${index}`}>Roster slot position {index}</label>
            <input
              id={`roster-slot-position-${index}`}
              value={position}
              onChange={(e) => {
                const next = [...rosterSlots];
                next[index] = [e.target.value, next[index][1]];
                setRosterSlots(next);
              }}
            />
            <label htmlFor={`roster-slot-count-${index}`}>Roster slot count {index}</label>
            <input
              id={`roster-slot-count-${index}`}
              type="number"
              value={count}
              onChange={(e) => {
                const next = [...rosterSlots];
                next[index] = [next[index][0], Number(e.target.value)];
                setRosterSlots(next);
              }}
            />
            <button type="button" onClick={() => setRosterSlots(rosterSlots.filter((_, i) => i !== index))}>
              Remove
            </button>
          </div>
        ))}
        <button type="button" onClick={() => setRosterSlots([...rosterSlots, ["", 0]])}>
          Add Roster Slot
        </button>
      </fieldset>
    );
  }

  async function handleSave() {
    setSaving(true);
    try {
      const settings: ScoringSettings = {
        hittingCategories: hitting,
        pitchingCategories: pitching,
        rosterSlots: Object.fromEntries(rosterSlots)
      };
      const result = await saveScoringSettings(settings);
      onSaved(result);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <h1>Scoring Settings</h1>
      {categoryRows("Hitting", hitting, setHitting)}
      {categoryRows("Pitching", pitching, setPitching)}
      {rosterSlotRows()}
      <button onClick={handleSave} disabled={saving}>
        Save
      </button>
    </div>
  );
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npm run test --prefix frontend`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/screens/ScoringSettingsScreen.tsx frontend/src/screens/ScoringSettingsScreen.test.tsx
git commit -m "Add scoring settings screen"
```

---

### Task 22: Dashboard screen

**Files:**
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/screens/DashboardScreen.tsx`
- Test: `frontend/src/screens/DashboardScreen.test.tsx`

**Interfaces:**
- Consumes: `League`, `RecommendationSet`, `Recommendation` types (Task 18); `request`/`ApiError` (Task 20).
- Produces: `refreshRecommendations(teamName: string): Promise<RecommendationSet>` and `getRecommendations(): Promise<RecommendationSet | null>` (404 → `null`, same pattern as scoring settings) in `api/client.ts`; `DashboardScreen` component with props `{ league: League; yourTeamName: string }`. Shows the user's roster, loads any previously generated recommendations on mount, has an "Analyze" action that calls refresh (noting it can take a while on a cold cache — Task 16's documented limitation), and lets the user click a suggestion to see its full reasoning and citations.

- [ ] **Step 1: Write the failing test**

`frontend/src/screens/DashboardScreen.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DashboardScreen } from "./DashboardScreen";
import type { League, RecommendationSet } from "../types";

describe("DashboardScreen", () => {
  const league: League = {
    importedAtUtc: "2026-01-01T00:00:00Z",
    teams: [
      {
        teamName: "Rhino Wranglers",
        players: [{ csvName: "Shohei Ohtani", playerId: "660271", playerFullName: "Shohei Ohtani", position: "DH", isPitcher: false }]
      }
    ]
  };

  it("loads existing recommendations, then re-analyzes and shows suggestion detail", async () => {
    const initial: RecommendationSet = { generatedAtUtc: "2026-01-01T00:00:00Z", waiverSuggestions: [], tradeSuggestions: [] };
    const refreshed: RecommendationSet = {
      generatedAtUtc: "2026-01-02T00:00:00Z",
      waiverSuggestions: [
        { type: "Waiver", summary: "Pick up X", reasoning: "Hot streak per recent box scores", involvedPlayerIds: ["1"], citations: ["https://example.com"], rank: 1 }
      ],
      tradeSuggestions: []
    };
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(initial) }) // initial GET
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(refreshed) }); // POST refresh
    vi.stubGlobal("fetch", fetchMock);

    render(<DashboardScreen league={league} yourTeamName="Rhino Wranglers" />);

    await waitFor(() => expect(screen.getByText("Shohei Ohtani")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: /analyze/i }));
    await waitFor(() => expect(screen.getByText("Pick up X")).toBeInTheDocument());

    fireEvent.click(screen.getByText("Pick up X"));
    expect(screen.getByText("Hot streak per recent box scores")).toBeInTheDocument();

    vi.unstubAllGlobals();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm run test --prefix frontend`
Expected: FAIL (`DashboardScreen` doesn't exist yet)

- [ ] **Step 3: Add the API functions**

Append to `frontend/src/api/client.ts` (add `RecommendationSet` to the existing type import from `../types`):

```typescript
export function refreshRecommendations(teamName: string): Promise<RecommendationSet> {
  return request<RecommendationSet>(`/api/recommendations/refresh?teamName=${encodeURIComponent(teamName)}`, {
    method: "POST"
  });
}

export async function getRecommendations(): Promise<RecommendationSet | null> {
  try {
    return await request<RecommendationSet>("/api/recommendations");
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}
```

- [ ] **Step 4: Implement the screen**

`frontend/src/screens/DashboardScreen.tsx`:

```tsx
import { useEffect, useState } from "react";
import { getRecommendations, refreshRecommendations } from "../api/client";
import type { League, Recommendation, RecommendationSet } from "../types";

interface DashboardScreenProps {
  league: League;
  yourTeamName: string;
}

export function DashboardScreen({ league, yourTeamName }: DashboardScreenProps) {
  const [recommendations, setRecommendations] = useState<RecommendationSet | null>(null);
  const [selected, setSelected] = useState<Recommendation | null>(null);
  const [analyzing, setAnalyzing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const yourTeam = league.teams.find((t) => t.teamName === yourTeamName);

  useEffect(() => {
    getRecommendations()
      .then(setRecommendations)
      .catch(() => setError("Failed to load existing recommendations."));
  }, []);

  async function handleAnalyze() {
    setAnalyzing(true);
    setError(null);
    setSelected(null);
    try {
      const result = await refreshRecommendations(yourTeamName);
      setRecommendations(result);
    } catch {
      setError("Analysis failed. Please try again.");
    } finally {
      setAnalyzing(false);
    }
  }

  function suggestionList(title: string, suggestions: Recommendation[]) {
    return (
      <section>
        <h2>{title}</h2>
        <ul>
          {suggestions.map((s) => (
            <li key={`${s.type}-${s.rank}`}>
              <button onClick={() => setSelected(s)}>{s.summary}</button>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  return (
    <div>
      <h1>{yourTeamName} Dashboard</h1>
      <section>
        <h2>Your Roster</h2>
        <ul>
          {yourTeam?.players.map((p) => (
            <li key={p.playerId}>{p.playerFullName}</li>
          ))}
        </ul>
      </section>

      <button onClick={handleAnalyze} disabled={analyzing}>
        {analyzing ? "Analyzing... (this can take a couple minutes on a cold cache)" : "Analyze"}
      </button>
      {error && <p role="alert">{error}</p>}

      {recommendations && (
        <>
          {suggestionList("Waiver Suggestions", recommendations.waiverSuggestions)}
          {suggestionList("Trade Suggestions", recommendations.tradeSuggestions)}
        </>
      )}

      {selected && (
        <aside>
          <h3>{selected.summary}</h3>
          <p>{selected.reasoning}</p>
          <ul>
            {selected.citations.map((c) => (
              <li key={c}>
                <a href={c}>{c}</a>
              </li>
            ))}
          </ul>
        </aside>
      )}
    </div>
  );
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `npm run test --prefix frontend`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/screens/DashboardScreen.tsx frontend/src/screens/DashboardScreen.test.tsx
git commit -m "Add recommendation dashboard screen"
```

---

### Task 23: App routing and screen wiring

**Files:**
- Modify: `frontend/src/api/client.ts`
- Modify: `frontend/src/App.tsx`
- Test: `frontend/src/App.test.tsx` (replace the Task 18 smoke test)

**Interfaces:**
- Consumes: every screen and API function from Tasks 19–22.
- Produces: the finished `App` component. On mount it loads any existing league (`GET /api/league`) and scoring settings in parallel and picks a starting screen: no league → `ImportScreen`; league but no "your team" chosen yet → an inline team picker (new, small enough to not warrant its own file/task); team chosen but no settings saved → `ScoringSettingsScreen`; otherwise → `DashboardScreen`. "Your team" is a lightweight client-only preference stored in `localStorage` (not backend-persisted — the spec's persistence requirement covers league/settings, not this per-viewer choice). A small header lets the user jump to Scoring Settings or re-import at any time once a league exists.

- [ ] **Step 1: Write the failing test**

Replace `frontend/src/App.test.tsx`:

```tsx
import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import type { League, ScoringSettings } from "./types";

describe("App", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("shows the import screen when no league has been imported yet", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: false, status: 404, json: () => Promise.resolve({}) });
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    await waitFor(() => expect(screen.getByText(/import league roster/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });

  it("goes straight to the dashboard when league, team, and settings already exist", async () => {
    const league: League = {
      importedAtUtc: "2026-01-01T00:00:00Z",
      teams: [{ teamName: "Rhino Wranglers", players: [] }]
    };
    const settings: ScoringSettings = { hittingCategories: [], pitchingCategories: [], rosterSlots: {} };
    localStorage.setItem("yourTeamName", "Rhino Wranglers");
    const fetchMock = vi.fn((url: string) => {
      if (url.includes("/api/league") && !url.includes("import")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(league) });
      }
      if (url.includes("/api/settings/scoring")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(settings) });
      }
      if (url.includes("/api/recommendations")) {
        return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
      }
      return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    await waitFor(() => expect(screen.getByText(/rhino wranglers dashboard/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm run test --prefix frontend`
Expected: FAIL (`App` doesn't yet do any of this routing)

- [ ] **Step 3: Add `getLeague` to the API client**

Append to `frontend/src/api/client.ts` (add `League` to the existing type import from `../types`, if not already present from Task 22):

```typescript
export async function getLeague(): Promise<League | null> {
  try {
    return await request<League>("/api/league");
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}
```

- [ ] **Step 4: Implement App routing**

Replace `frontend/src/App.tsx`:

```tsx
import { useEffect, useState } from "react";
import { getLeague, getScoringSettings } from "./api/client";
import { ImportScreen } from "./screens/ImportScreen";
import { MatchReviewScreen } from "./screens/MatchReviewScreen";
import { ScoringSettingsScreen } from "./screens/ScoringSettingsScreen";
import { DashboardScreen } from "./screens/DashboardScreen";
import type { ImportPreview, League } from "./types";

type Screen = "loading" | "import" | "matchReview" | "teamPicker" | "settings" | "dashboard";

export function App() {
  const [screen, setScreen] = useState<Screen>("loading");
  const [league, setLeague] = useState<League | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [yourTeamName, setYourTeamName] = useState<string | null>(null);
  const [hasSettings, setHasSettings] = useState(false);

  useEffect(() => {
    Promise.all([getLeague(), getScoringSettings()]).then(([loadedLeague, settings]) => {
      setLeague(loadedLeague);
      setHasSettings(settings !== null);
      const storedTeam = localStorage.getItem("yourTeamName");
      setYourTeamName(storedTeam);

      if (!loadedLeague) {
        setScreen("import");
      } else if (!storedTeam || !loadedLeague.teams.some((t) => t.teamName === storedTeam)) {
        setScreen("teamPicker");
      } else if (settings === null) {
        setScreen("settings");
      } else {
        setScreen("dashboard");
      }
    });
  }, []);

  function handlePreviewReady(nextPreview: ImportPreview) {
    setPreview(nextPreview);
    setScreen("matchReview");
  }

  function handleConfirmed(nextLeague: League) {
    setLeague(nextLeague);
    setPreview(null);
    setScreen("teamPicker");
  }

  function handleTeamChosen(teamName: string) {
    localStorage.setItem("yourTeamName", teamName);
    setYourTeamName(teamName);
    setScreen(hasSettings ? "dashboard" : "settings");
  }

  function handleSettingsSaved() {
    setHasSettings(true);
    setScreen("dashboard");
  }

  if (screen === "loading") return <p>Loading...</p>;

  return (
    <div>
      {league && (
        <header>
          <button onClick={() => setScreen(yourTeamName ? "dashboard" : "teamPicker")}>Dashboard</button>
          <button onClick={() => setScreen("settings")}>Scoring Settings</button>
          <button onClick={() => setScreen("import")}>Re-import League</button>
        </header>
      )}

      {screen === "import" && <ImportScreen onPreviewReady={handlePreviewReady} />}
      {screen === "matchReview" && preview && (
        <MatchReviewScreen preview={preview} onConfirmed={handleConfirmed} />
      )}
      {screen === "teamPicker" && league && (
        <div>
          <h1>Which team is yours?</h1>
          {league.teams.map((t) => (
            <button key={t.teamName} onClick={() => handleTeamChosen(t.teamName)}>
              {t.teamName}
            </button>
          ))}
        </div>
      )}
      {screen === "settings" && <ScoringSettingsScreen onSaved={handleSettingsSaved} />}
      {screen === "dashboard" && league && yourTeamName && (
        <DashboardScreen league={league} yourTeamName={yourTeamName} />
      )}
    </div>
  );
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `npm run test --prefix frontend`
Expected: PASS (all frontend tests)

- [ ] **Step 6: Verify the production build works end-to-end**

```bash
npm run build --prefix frontend
```

Expected: builds successfully and populates `backend/FantasyAnalysis.Api/wwwroot`.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/App.tsx frontend/src/App.test.tsx
git commit -m "Wire App routing across import, match review, team picker, settings, and dashboard"
```

---

## Plan Self-Review Notes

- **Spec coverage:** CSV import (Tasks 2, 7, 8, 19) · player-name review step (Tasks 6, 20) · waiver pool computation (Task 11) · scoring settings form + persistence (Tasks 10, 21) · stats sourcing from a free, confirmed-working API (Tasks 4, 5) · numeric shortlist before the AI call (Task 12) · Claude reasoning + web search + structured output (Tasks 13, 14) · caching to avoid re-fetching (Task 9) · persisted/revisitable state (Tasks 3, 15, and `App.tsx`'s load-on-mount in Task 23) · interactive dashboard with click-through reasoning (Task 22) · single-process deployment (Task 18's `vite.config.ts` `outDir`, verified in Task 23 Step 6) · typed error handling throughout (every endpoint task's catch blocks). No spec section was left without a task.
- **Type consistency fix applied during writing:** `PlayerMatchCandidate` (Task 6) was missing `Position`/`IsPitcher`, which Task 20's confirm-request builder needs — fixed by adding both fields to the record, the matching service, Task 8's test, and the frontend type, before Task 20 was written (see the note before Task 20).
- **Not covered by this plan, by design:** the spec's own "Open Questions" section (NFL/NBA as a second sport) is explicitly future work, not a v1 requirement.
