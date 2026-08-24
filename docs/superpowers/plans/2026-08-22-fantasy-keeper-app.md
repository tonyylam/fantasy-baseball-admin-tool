# Fantasy Keeper App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a React/TypeScript + ASP.NET Core web app that gives Worm Burners Dynasty League owners a friendly form for submitting their keepers, writing only their team's specific cells back into the live Google Sheet, plus commissioner tools to clone a new season and switch between seasons.

**Architecture:** A single ASP.NET Core minimal API process serves both the JSON API and the built React SPA. The backend talks to Google Sheets/Drive through a service account, reading/writing only each team's mapped cell range. A `Google:UseDevClients` flag swaps in in-memory fake Sheets/Drive clients so the whole stack is buildable and testable without real Google credentials; flipping it to `false` (with a real service-account key) switches to live Google APIs.

**Tech Stack:** .NET 8 (ASP.NET Core minimal API), `Google.Apis.Sheets.v4` / `Google.Apis.Drive.v3` / `Google.Apis.Auth`, xUnit + `Microsoft.AspNetCore.Mvc.Testing`; React 18 + TypeScript via Vite, no state-management or routing library, plain `fetch`.

**Spec:** `docs/superpowers/specs/2026-08-21-fantasy-keeper-app-design.md`

## Global Constraints

- Backend targets .NET 8 (LTS), ASP.NET Core **minimal API** — no MVC controllers.
- Frontend is React + TypeScript via Vite — no Redux, no router library.
- No database. All app-side state lives in plain JSON files under `config/`.
- Google access is via a **service account** — no per-owner OAuth.
- Access control is **PIN-based**, passed as a `pin` query-string parameter on every request — no sessions, cookies, or JWTs.
- The backend only ever reads/writes each team's specific mapped cell range — never the whole sheet.
- One ASP.NET Core process serves both the API and the built SPA static files.
- V1 scope is keeper submission + season cloning/switching only. Nothing else.

---

## Task 1: Backend scaffold + health endpoint

**Files:**
- Create: `fantasy-keeper-app/backend/FantasyKeeper.sln`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Program.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/FantasyKeeper.Api.Tests.csproj`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Produces: `public partial class Program { }` marker in `Program.cs`, required by every later `WebApplicationFactory<Program>` integration test (Task 9).

- [ ] **Step 1: Scaffold the solution and projects**

```bash
dotnet new sln -n FantasyKeeper -o fantasy-keeper-app/backend
dotnet new web -n FantasyKeeper.Api -o fantasy-keeper-app/backend/FantasyKeeper.Api
dotnet new xunit -n FantasyKeeper.Api.Tests -o fantasy-keeper-app/backend/FantasyKeeper.Api.Tests
dotnet sln fantasy-keeper-app/backend/FantasyKeeper.sln add fantasy-keeper-app/backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/FantasyKeeper.Api.Tests.csproj
dotnet add fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/FantasyKeeper.Api.Tests.csproj reference fantasy-keeper-app/backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj
dotnet add fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/FantasyKeeper.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 2: Write the failing test**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/HealthEndpointTests.cs
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL — `/health` returns 404, and/or compile error because `Program` isn't public yet.

- [ ] **Step 4: Add the health endpoint and expose `Program`**

Replace the entire contents of `fantasy-keeper-app/backend/FantasyKeeper.Api/Program.cs` with:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: scaffold backend with health endpoint"
```

---

## Task 2: Config models + JsonConfigStore

**Files:**
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/Season.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/Team.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/TeamMapping.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/IConfigStore.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/JsonConfigStore.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/JsonConfigStoreTests.cs`

**Interfaces:**
- Produces: `Season(string Id, string Label, string GoogleSheetId, string Status, DateTimeOffset CreatedAt)` with `bool IsActive` computed property; `Team(string TeamId, string Name, string Pin)`; `TeamMapping(string SheetTab, string ExistingContractsRange, string NewContractsRange)`; `IConfigStore` with `GetSeasons()`, `SaveSeasons(IReadOnlyList<Season>)`, `GetTeams()`, `GetTeamMappings(string seasonId)`, `SaveTeamMappings(string seasonId, IReadOnlyDictionary<string, TeamMapping>)`; `JsonConfigStore(string configRoot) : IConfigStore`.

- [ ] **Step 1: Write the failing tests**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/JsonConfigStoreTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class JsonConfigStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void SaveAndGetSeasons_RoundTrips()
    {
        var store = new JsonConfigStore(_tempDir);
        var seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", "active", DateTimeOffset.UtcNow) };

        store.SaveSeasons(seasons);
        var loaded = store.GetSeasons();

        Assert.Single(loaded);
        Assert.Equal("2026 Season", loaded[0].Label);
        Assert.True(loaded[0].IsActive);
    }

    [Fact]
    public void GetSeasons_WhenFileMissing_ReturnsEmptyList()
    {
        var store = new JsonConfigStore(_tempDir);
        Assert.Empty(store.GetSeasons());
    }

    [Fact]
    public void GetTeams_ReadsSeedFile()
    {
        File.WriteAllText(Path.Combine(_tempDir, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"}]""");

        var store = new JsonConfigStore(_tempDir);
        var teams = store.GetTeams();

        Assert.Single(teams);
        Assert.Equal("B Squared", teams[0].Name);
    }

    [Fact]
    public void SaveAndGetTeamMappings_RoundTrips()
    {
        var store = new JsonConfigStore(_tempDir);
        var mappings = new Dictionary<string, TeamMapping>
        {
            ["b-squared"] = new TeamMapping("2026 Keepers", "H8:N13", "C8:F13")
        };

        store.SaveTeamMappings("2026", mappings);
        var loaded = store.GetTeamMappings("2026");

        Assert.True(loaded.ContainsKey("b-squared"));
        Assert.Equal("C8:F13", loaded["b-squared"].NewContractsRange);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL to compile — `Season`, `Team`, `TeamMapping`, `JsonConfigStore` don't exist yet.

- [ ] **Step 3: Write the models and store**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/Season.cs
namespace FantasyKeeper.Api.Models;

public record Season(string Id, string Label, string GoogleSheetId, string Status, DateTimeOffset CreatedAt)
{
    public bool IsActive => Status == "active";
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/Team.cs
namespace FantasyKeeper.Api.Models;

public record Team(string TeamId, string Name, string Pin);
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/TeamMapping.cs
namespace FantasyKeeper.Api.Models;

public record TeamMapping(string SheetTab, string ExistingContractsRange, string NewContractsRange);
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/IConfigStore.cs
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public interface IConfigStore
{
    IReadOnlyList<Season> GetSeasons();
    void SaveSeasons(IReadOnlyList<Season> seasons);
    IReadOnlyList<Team> GetTeams();
    IReadOnlyDictionary<string, TeamMapping> GetTeamMappings(string seasonId);
    void SaveTeamMappings(string seasonId, IReadOnlyDictionary<string, TeamMapping> mappings);
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/JsonConfigStore.cs
using System.Text.Json;
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class JsonConfigStore : IConfigStore
{
    private readonly string _configRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public JsonConfigStore(string configRoot)
    {
        _configRoot = configRoot;
    }

    public IReadOnlyList<Season> GetSeasons() =>
        ReadJson<List<Season>>(Path.Combine(_configRoot, "seasons.json")) ?? new List<Season>();

    public void SaveSeasons(IReadOnlyList<Season> seasons) =>
        WriteJson(Path.Combine(_configRoot, "seasons.json"), seasons);

    public IReadOnlyList<Team> GetTeams() =>
        ReadJson<List<Team>>(Path.Combine(_configRoot, "teams.json")) ?? new List<Team>();

    public IReadOnlyDictionary<string, TeamMapping> GetTeamMappings(string seasonId) =>
        ReadJson<Dictionary<string, TeamMapping>>(Path.Combine(_configRoot, "team-mappings", $"{seasonId}.json"))
        ?? new Dictionary<string, TeamMapping>();

    public void SaveTeamMappings(string seasonId, IReadOnlyDictionary<string, TeamMapping> mappings)
    {
        var path = Path.Combine(_configRoot, "team-mappings", $"{seasonId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteJson(path, mappings);
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS (5 tests total including Task 1's health test)

- [ ] **Step 5: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: add config models and JSON-backed config store"
```

---

## Task 3: AuthService

**Files:**
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/AuthResult.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/AuthService.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/Fakes/FakeConfigStore.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `IConfigStore`, `Season`, `Team` (Task 2).
- Produces: `enum AuthRole { Owner, Admin }`; `record AuthResult(AuthRole Role, string? TeamId, string? SeasonId)`; `AuthService(IConfigStore configStore, string adminPin)` with `AuthResult? ResolvePin(string pin)`; `FakeConfigStore : IConfigStore` (mutable in-memory test double), reused by Tasks 5 and 6.

- [ ] **Step 1: Write the failing tests**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/Fakes/FakeConfigStore.cs
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeConfigStore : IConfigStore
{
    public List<Season> Seasons { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
    public Dictionary<string, Dictionary<string, TeamMapping>> Mappings { get; set; } = new();

    public IReadOnlyList<Season> GetSeasons() => Seasons;
    public void SaveSeasons(IReadOnlyList<Season> seasons) => Seasons = seasons.ToList();
    public IReadOnlyList<Team> GetTeams() => Teams;

    public IReadOnlyDictionary<string, TeamMapping> GetTeamMappings(string seasonId) =>
        Mappings.TryGetValue(seasonId, out var m) ? m : new Dictionary<string, TeamMapping>();

    public void SaveTeamMappings(string seasonId, IReadOnlyDictionary<string, TeamMapping> mappings) =>
        Mappings[seasonId] = mappings.ToDictionary(kv => kv.Key, kv => kv.Value);
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/AuthServiceTests.cs
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class AuthServiceTests
{
    private static FakeConfigStore BuildStore() => new()
    {
        Teams = new List<Team> { new("b-squared", "B Squared", "1111") },
        Seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", "active", DateTimeOffset.UtcNow) }
    };

    [Fact]
    public void ResolvePin_AdminPin_ReturnsAdminRole()
    {
        var service = new AuthService(BuildStore(), "9999");
        var result = service.ResolvePin("9999");

        Assert.NotNull(result);
        Assert.Equal(AuthRole.Admin, result!.Role);
    }

    [Fact]
    public void ResolvePin_TeamPin_ReturnsOwnerWithActiveSeason()
    {
        var service = new AuthService(BuildStore(), "9999");
        var result = service.ResolvePin("1111");

        Assert.NotNull(result);
        Assert.Equal(AuthRole.Owner, result!.Role);
        Assert.Equal("b-squared", result.TeamId);
        Assert.Equal("2026", result.SeasonId);
    }

    [Fact]
    public void ResolvePin_UnknownPin_ReturnsNull()
    {
        var service = new AuthService(BuildStore(), "9999");
        Assert.Null(service.ResolvePin("0000"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL to compile — `AuthRole`, `AuthResult`, `AuthService` don't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/AuthResult.cs
namespace FantasyKeeper.Api.Models;

public enum AuthRole { Owner, Admin }

public record AuthResult(AuthRole Role, string? TeamId, string? SeasonId);
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/AuthService.cs
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class AuthService
{
    private readonly IConfigStore _configStore;
    private readonly string _adminPin;

    public AuthService(IConfigStore configStore, string adminPin)
    {
        _configStore = configStore;
        _adminPin = adminPin;
    }

    public AuthResult? ResolvePin(string pin)
    {
        if (!string.IsNullOrEmpty(pin) && pin == _adminPin)
        {
            return new AuthResult(AuthRole.Admin, null, null);
        }

        var team = _configStore.GetTeams().FirstOrDefault(t => t.Pin == pin);
        if (team is null)
        {
            return null;
        }

        var activeSeason = _configStore.GetSeasons().FirstOrDefault(s => s.IsActive);
        if (activeSeason is null)
        {
            return null;
        }

        return new AuthResult(AuthRole.Owner, team.TeamId, activeSeason.Id);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: add PIN-based AuthService"
```

---

## Task 4: A1Range helper

**Files:**
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/A1Range.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/A1RangeTests.cs`

**Interfaces:**
- Produces: `static (int Rows, int Cols) A1Range.GetDimensions(string range)`, used by Task 5's `KeepersService`.

- [ ] **Step 1: Write the failing tests**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/A1RangeTests.cs
using FantasyKeeper.Api.Services;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class A1RangeTests
{
    [Theory]
    [InlineData("C8:F13", 6, 4)]
    [InlineData("A1:A1", 1, 1)]
    [InlineData("AA1:AB2", 2, 2)]
    public void GetDimensions_ValidRange_ReturnsRowsAndCols(string range, int expectedRows, int expectedCols)
    {
        var (rows, cols) = A1Range.GetDimensions(range);
        Assert.Equal(expectedRows, rows);
        Assert.Equal(expectedCols, cols);
    }

    [Fact]
    public void GetDimensions_NoColon_Throws()
    {
        Assert.Throws<ArgumentException>(() => A1Range.GetDimensions("C8"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL to compile — `A1Range` doesn't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/A1Range.cs
namespace FantasyKeeper.Api.Services;

public static class A1Range
{
    public static (int Rows, int Cols) GetDimensions(string range)
    {
        var parts = range.Split(':');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Range '{range}' is not a valid A1 range with two corners.");
        }

        var (startCol, startRow) = ParseCell(parts[0]);
        var (endCol, endRow) = ParseCell(parts[1]);

        return (endRow - startRow + 1, endCol - startCol + 1);
    }

    private static (int Col, int Row) ParseCell(string cell)
    {
        var i = 0;
        while (i < cell.Length && char.IsLetter(cell[i])) i++;
        if (i == 0 || i == cell.Length)
        {
            throw new ArgumentException($"Cell reference '{cell}' is not valid.");
        }

        var colLetters = cell[..i];
        var rowDigits = cell[i..];

        var col = 0;
        foreach (var ch in colLetters)
        {
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return (col, int.Parse(rowDigits));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: add A1 range dimension helper"
```

---

## Task 5: KeepersService

**Files:**
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/KeeperRow.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/ExistingContractRow.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/KeeperTeamData.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/KeeperSubmission.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Models/KeeperExceptions.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/ISheetsClient.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/KeepersService.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/Fakes/FakeSheetsClient.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs`

**Interfaces:**
- Consumes: `IConfigStore`, `Season`, `Team`, `TeamMapping` (Task 2); `A1Range.GetDimensions` (Task 4); `FakeConfigStore` (Task 3).
- Produces: `record KeeperRow(string Player, int? ContractType, decimal? Salary, int? KeeperYears)`; `record ExistingContractRow(string Player, string ContractInfo, decimal? LastYearSalary, decimal? LeagueValue, decimal? ThisYearSalary)`; `record KeeperTeamData(string TeamName, bool ReadOnly, IReadOnlyList<ExistingContractRow> ExistingContracts, IReadOnlyList<KeeperRow> NewContracts)`; `record KeeperSubmission(IReadOnlyList<KeeperRow> NewContracts)`; `class NotFoundException`, `class SeasonNotActiveException`, `class KeeperValidationException` (used by Task 9 endpoints); `interface ISheetsClient` with `GetRangeAsync`/`UpdateRangeAsync` (used by Tasks 7, 8); `KeepersService(ISheetsClient sheets, IConfigStore configStore)` with `GetKeeperDataAsync(string seasonId, string teamId, CancellationToken ct = default)` and `UpdateKeeperDataAsync(string seasonId, string teamId, KeeperSubmission submission, CancellationToken ct = default)` (used by Task 9); `FakeSheetsClient : ISheetsClient` (reused by Task 6's tests are not needed, but reusable pattern for Task 8's Dev clients).

- [ ] **Step 1: Write the failing tests**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/Fakes/FakeSheetsClient.cs
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeSheetsClient : ISheetsClient
{
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyList<string>>> _data = new();
    public List<(string SpreadsheetId, string Tab, string Range, IReadOnlyList<IReadOnlyList<string>> Values)> Updates { get; } = new();

    public void Seed(string spreadsheetId, string tab, string range, IReadOnlyList<IReadOnlyList<string>> values) =>
        _data[Key(spreadsheetId, tab, range)] = values;

    public Task<IReadOnlyList<IReadOnlyList<string>>> GetRangeAsync(string spreadsheetId, string sheetTab, string range, CancellationToken ct = default)
    {
        return Task.FromResult(_data.TryGetValue(Key(spreadsheetId, sheetTab, range), out var values)
            ? values
            : (IReadOnlyList<IReadOnlyList<string>>)new List<IReadOnlyList<string>>());
    }

    public Task UpdateRangeAsync(string spreadsheetId, string sheetTab, string range, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken ct = default)
    {
        Updates.Add((spreadsheetId, sheetTab, range, values));
        _data[Key(spreadsheetId, sheetTab, range)] = values;
        return Task.CompletedTask;
    }

    private static string Key(string spreadsheetId, string tab, string range) => $"{spreadsheetId}|{tab}|{range}";
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeepersServiceTests
{
    private static (FakeConfigStore Config, FakeSheetsClient Sheets, KeepersService Service) Build(string seasonStatus = "active")
    {
        var config = new FakeConfigStore
        {
            Seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", seasonStatus, DateTimeOffset.UtcNow) },
            Teams = new List<Team> { new("b-squared", "B Squared", "1111") },
            Mappings = new Dictionary<string, Dictionary<string, TeamMapping>>
            {
                ["2026"] = new()
                {
                    ["b-squared"] = new TeamMapping("2026 Keepers", "H8:H9", "C8:F9")
                }
            }
        };

        var sheets = new FakeSheetsClient();
        sheets.Seed("sheet-1", "2026 Keepers", "H8:H9", new List<IReadOnlyList<string>>
        {
            new List<string> { "T. Story" },
            new List<string> { "" }
        });
        sheets.Seed("sheet-1", "2026 Keepers", "C8:F9", new List<IReadOnlyList<string>>
        {
            new List<string> { "T. Story", "1", "14", "2" },
            new List<string> { "", "", "", "" }
        });

        return (config, sheets, new KeepersService(sheets, config));
    }

    [Fact]
    public async Task GetKeeperDataAsync_ReturnsParsedRows()
    {
        var (_, _, service) = Build();

        var data = await service.GetKeeperDataAsync("2026", "b-squared");

        Assert.Equal("B Squared", data.TeamName);
        Assert.False(data.ReadOnly);
        Assert.Equal("T. Story", data.NewContracts[0].Player);
        Assert.Equal(1, data.NewContracts[0].ContractType);
        Assert.Equal(14, data.NewContracts[0].Salary);
    }

    [Fact]
    public async Task GetKeeperDataAsync_ArchivedSeason_IsReadOnly()
    {
        var (_, _, service) = Build(seasonStatus: "archived");

        var data = await service.GetKeeperDataAsync("2026", "b-squared");

        Assert.True(data.ReadOnly);
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_ValidSubmission_WritesRange()
    {
        var (_, sheets, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        await service.UpdateKeeperDataAsync("2026", "b-squared", submission);

        var update = Assert.Single(sheets.Updates);
        Assert.Equal("C8:F9", update.Range);
        Assert.Equal("New Guy", update.Values[0][0]);
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_InvalidContractType_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 3, 10, 2),
            new("", null, null, null)
        });

        await Assert.ThrowsAsync<KeeperValidationException>(
            () => service.UpdateKeeperDataAsync("2026", "b-squared", submission));
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_WrongRowCount_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow> { new("New Guy", 1, 10, 2) });

        await Assert.ThrowsAsync<KeeperValidationException>(
            () => service.UpdateKeeperDataAsync("2026", "b-squared", submission));
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_ArchivedSeason_Throws()
    {
        var (_, _, service) = Build(seasonStatus: "archived");
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        await Assert.ThrowsAsync<SeasonNotActiveException>(
            () => service.UpdateKeeperDataAsync("2026", "b-squared", submission));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL to compile — models and `KeepersService` don't exist yet.

- [ ] **Step 3: Write the models, exceptions, interface, and service**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/KeeperRow.cs
namespace FantasyKeeper.Api.Models;

public record KeeperRow(string Player, int? ContractType, decimal? Salary, int? KeeperYears);
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/ExistingContractRow.cs
namespace FantasyKeeper.Api.Models;

public record ExistingContractRow(string Player, string ContractInfo, decimal? LastYearSalary, decimal? LeagueValue, decimal? ThisYearSalary);
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/KeeperTeamData.cs
namespace FantasyKeeper.Api.Models;

public record KeeperTeamData(string TeamName, bool ReadOnly, IReadOnlyList<ExistingContractRow> ExistingContracts, IReadOnlyList<KeeperRow> NewContracts);
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/KeeperSubmission.cs
namespace FantasyKeeper.Api.Models;

public record KeeperSubmission(IReadOnlyList<KeeperRow> NewContracts);
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Models/KeeperExceptions.cs
namespace FantasyKeeper.Api.Models;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class SeasonNotActiveException : Exception
{
    public SeasonNotActiveException(string seasonId) : base($"Season '{seasonId}' is not the active season.") { }
}

public class KeeperValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public KeeperValidationException(IReadOnlyList<string> errors) : base(string.Join("; ", errors))
    {
        Errors = errors;
    }
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/ISheetsClient.cs
namespace FantasyKeeper.Api.Services;

public interface ISheetsClient
{
    Task<IReadOnlyList<IReadOnlyList<string>>> GetRangeAsync(string spreadsheetId, string sheetTab, string range, CancellationToken ct = default);
    Task UpdateRangeAsync(string spreadsheetId, string sheetTab, string range, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken ct = default);
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/KeepersService.cs
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class KeepersService
{
    private readonly ISheetsClient _sheets;
    private readonly IConfigStore _configStore;

    public KeepersService(ISheetsClient sheets, IConfigStore configStore)
    {
        _sheets = sheets;
        _configStore = configStore;
    }

    public async Task<KeeperTeamData> GetKeeperDataAsync(string seasonId, string teamId, CancellationToken ct = default)
    {
        var season = FindSeason(seasonId);
        var team = FindTeam(teamId);
        var mapping = FindMapping(seasonId, teamId);

        var existingRaw = await _sheets.GetRangeAsync(season.GoogleSheetId, mapping.SheetTab, mapping.ExistingContractsRange, ct);
        var newRaw = await _sheets.GetRangeAsync(season.GoogleSheetId, mapping.SheetTab, mapping.NewContractsRange, ct);

        var existing = existingRaw.Select(ParseExistingRow).ToList();
        var newContracts = newRaw.Select(ParseNewRow).ToList();

        return new KeeperTeamData(team.Name, !season.IsActive, existing, newContracts);
    }

    public async Task<KeeperTeamData> UpdateKeeperDataAsync(string seasonId, string teamId, KeeperSubmission submission, CancellationToken ct = default)
    {
        var season = FindSeason(seasonId);
        if (!season.IsActive)
        {
            throw new SeasonNotActiveException(seasonId);
        }

        var team = FindTeam(teamId);
        var mapping = FindMapping(seasonId, teamId);

        var (expectedRows, expectedCols) = A1Range.GetDimensions(mapping.NewContractsRange);
        if (expectedCols != 4)
        {
            throw new InvalidOperationException(
                $"Mapping for '{teamId}' expects 4 columns (Player, Contract Type, Salary, Keeper Years) but range '{mapping.NewContractsRange}' has {expectedCols}.");
        }

        var errors = ValidateSubmission(submission, expectedRows);
        if (errors.Count > 0)
        {
            throw new KeeperValidationException(errors);
        }

        var values = submission.NewContracts
            .Select(row => (IReadOnlyList<string>)new List<string>
            {
                row.Player ?? "",
                row.ContractType?.ToString() ?? "",
                row.Salary?.ToString() ?? "",
                row.KeeperYears?.ToString() ?? ""
            })
            .ToList();

        await _sheets.UpdateRangeAsync(season.GoogleSheetId, mapping.SheetTab, mapping.NewContractsRange, values, ct);

        return await GetKeeperDataAsync(seasonId, teamId, ct);
    }

    private static List<string> ValidateSubmission(KeeperSubmission submission, int expectedRows)
    {
        var errors = new List<string>();

        if (submission.NewContracts.Count != expectedRows)
        {
            errors.Add($"Expected {expectedRows} rows but received {submission.NewContracts.Count}.");
            return errors;
        }

        for (var i = 0; i < submission.NewContracts.Count; i++)
        {
            var row = submission.NewContracts[i];
            var isBlank = string.IsNullOrWhiteSpace(row.Player)
                && row.ContractType is null
                && row.Salary is null
                && row.KeeperYears is null;
            if (isBlank)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Player))
            {
                errors.Add($"Row {i + 1}: player name is required when other fields are set.");
            }

            if (row.ContractType is not (1 or 2))
            {
                errors.Add($"Row {i + 1}: contract type must be 1 or 2.");
            }

            if (row.Salary is null || row.Salary < 0)
            {
                errors.Add($"Row {i + 1}: salary must be a non-negative number.");
            }

            if (row.KeeperYears is null || row.KeeperYears < 0)
            {
                errors.Add($"Row {i + 1}: keeper years must be a non-negative number.");
            }
        }

        return errors;
    }

    private static KeeperRow ParseNewRow(IReadOnlyList<string> cells)
    {
        string Cell(int i) => i < cells.Count ? cells[i] : "";
        return new KeeperRow(
            Cell(0),
            int.TryParse(Cell(1), out var ct) ? ct : null,
            decimal.TryParse(Cell(2), out var salary) ? salary : null,
            int.TryParse(Cell(3), out var years) ? years : null);
    }

    private static ExistingContractRow ParseExistingRow(IReadOnlyList<string> cells)
    {
        string Cell(int i) => i < cells.Count ? cells[i] : "";
        return new ExistingContractRow(
            Cell(0),
            Cell(1),
            decimal.TryParse(Cell(2), out var lastYear) ? lastYear : null,
            decimal.TryParse(Cell(3), out var leagueValue) ? leagueValue : null,
            decimal.TryParse(Cell(4), out var thisYear) ? thisYear : null);
    }

    private Season FindSeason(string seasonId)
    {
        var season = _configStore.GetSeasons().FirstOrDefault(s => s.Id == seasonId);
        if (season is null) throw new NotFoundException($"Season '{seasonId}' not found.");
        return season;
    }

    private Team FindTeam(string teamId)
    {
        var team = _configStore.GetTeams().FirstOrDefault(t => t.TeamId == teamId);
        if (team is null) throw new NotFoundException($"Team '{teamId}' not found.");
        return team;
    }

    private TeamMapping FindMapping(string seasonId, string teamId)
    {
        var mappings = _configStore.GetTeamMappings(seasonId);
        if (!mappings.TryGetValue(teamId, out var mapping))
        {
            throw new NotFoundException($"No mapping for team '{teamId}' in season '{seasonId}'.");
        }
        return mapping;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: add KeepersService with cell-range validation"
```

---

## Task 6: SeasonService

**Files:**
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/IDriveClient.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/SeasonService.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/Fakes/FakeDriveClient.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/SeasonServiceTests.cs`

**Interfaces:**
- Consumes: `IConfigStore`, `Season` (Task 2); `FakeConfigStore` (Task 3).
- Produces: `interface IDriveClient` with `CopyFileAsync`/`ShareFileAsync` (used by Tasks 7, 8); `SeasonService(IConfigStore configStore, IDriveClient drive, string commissionerEmail)` with `ListSeasons()` and `CreateNewSeasonAsync(string label, CancellationToken ct = default)` (used by Task 9); `FakeDriveClient : IDriveClient`.

- [ ] **Step 1: Write the failing tests**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/Fakes/FakeDriveClient.cs
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeDriveClient : IDriveClient
{
    public List<(string FileId, string NewTitle)> Copies { get; } = new();
    public List<(string FileId, string Email)> Shares { get; } = new();
    public string NextCopyId { get; set; } = "copied-sheet-id";

    public Task<string> CopyFileAsync(string fileId, string newTitle, CancellationToken ct = default)
    {
        Copies.Add((fileId, newTitle));
        return Task.FromResult(NextCopyId);
    }

    public Task ShareFileAsync(string fileId, string email, CancellationToken ct = default)
    {
        Shares.Add((fileId, email));
        return Task.CompletedTask;
    }
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/SeasonServiceTests.cs
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class SeasonServiceTests
{
    private static (FakeConfigStore Config, FakeDriveClient Drive, SeasonService Service) Build()
    {
        var config = new FakeConfigStore
        {
            Seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", "active", DateTimeOffset.UtcNow) },
            Mappings = new Dictionary<string, Dictionary<string, TeamMapping>>
            {
                ["2026"] = new() { ["b-squared"] = new TeamMapping("2026 Keepers", "H8:H9", "C8:F9") }
            }
        };
        var drive = new FakeDriveClient { NextCopyId = "sheet-2027" };
        return (config, drive, new SeasonService(config, drive, "commissioner@example.com"));
    }

    [Fact]
    public async Task CreateNewSeasonAsync_CopiesActiveSheetAndShares()
    {
        var (_, drive, service) = Build();

        await service.CreateNewSeasonAsync("2027 Season");

        var copy = Assert.Single(drive.Copies);
        Assert.Equal("sheet-1", copy.FileId);
        Assert.Equal("2027 Season", copy.NewTitle);

        var share = Assert.Single(drive.Shares);
        Assert.Equal("sheet-2027", share.FileId);
        Assert.Equal("commissioner@example.com", share.Email);
    }

    [Fact]
    public async Task CreateNewSeasonAsync_ArchivesOldSeasonAndActivatesNew()
    {
        var (config, _, service) = Build();

        var newSeason = await service.CreateNewSeasonAsync("2027 Season");

        var seasons = config.GetSeasons();
        Assert.Equal(2, seasons.Count);
        Assert.Equal("archived", seasons.Single(s => s.Id == "2026").Status);
        Assert.True(newSeason.IsActive);
        Assert.Equal("sheet-2027", newSeason.GoogleSheetId);
    }

    [Fact]
    public async Task CreateNewSeasonAsync_ClonesTeamMappings()
    {
        var (config, _, service) = Build();

        var newSeason = await service.CreateNewSeasonAsync("2027 Season");

        var mappings = config.GetTeamMappings(newSeason.Id);
        Assert.True(mappings.ContainsKey("b-squared"));
        Assert.Equal("C8:F9", mappings["b-squared"].NewContractsRange);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL to compile — `IDriveClient`, `SeasonService` don't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/IDriveClient.cs
namespace FantasyKeeper.Api.Services;

public interface IDriveClient
{
    Task<string> CopyFileAsync(string fileId, string newTitle, CancellationToken ct = default);
    Task ShareFileAsync(string fileId, string email, CancellationToken ct = default);
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/SeasonService.cs
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class SeasonService
{
    private readonly IConfigStore _configStore;
    private readonly IDriveClient _drive;
    private readonly string _commissionerEmail;

    public SeasonService(IConfigStore configStore, IDriveClient drive, string commissionerEmail)
    {
        _configStore = configStore;
        _drive = drive;
        _commissionerEmail = commissionerEmail;
    }

    public IReadOnlyList<Season> ListSeasons() => _configStore.GetSeasons();

    public async Task<Season> CreateNewSeasonAsync(string label, CancellationToken ct = default)
    {
        var seasons = _configStore.GetSeasons();
        var active = seasons.FirstOrDefault(s => s.IsActive);
        if (active is null)
        {
            throw new InvalidOperationException("No active season found to copy from.");
        }

        var newSheetId = await _drive.CopyFileAsync(active.GoogleSheetId, label, ct);
        await _drive.ShareFileAsync(newSheetId, _commissionerEmail, ct);

        var newSeasonId = Guid.NewGuid().ToString("N");
        var mappings = _configStore.GetTeamMappings(active.Id);
        _configStore.SaveTeamMappings(newSeasonId, mappings);

        var updatedSeasons = seasons
            .Select(s => s.Id == active.Id ? s with { Status = "archived" } : s)
            .Append(new Season(newSeasonId, label, newSheetId, "active", DateTimeOffset.UtcNow))
            .ToList();

        _configStore.SaveSeasons(updatedSeasons);

        return updatedSeasons.Single(s => s.Id == newSeasonId);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: add SeasonService for cloning and switching seasons"
```

---

## Task 7: Real Google Sheets/Drive clients

**Files:**
- Modify: `fantasy-keeper-app/backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj` (add packages)
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Google/GoogleCredentialLoader.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Google/RetryPolicy.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Google/GoogleSheetsClient.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Google/GoogleDriveClient.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/GoogleCredentialLoaderTests.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/RetryPolicyTests.cs`

**Interfaces:**
- Consumes: `ISheetsClient` (Task 5), `IDriveClient` (Task 6).
- Produces: `static GoogleCredential GoogleCredentialLoader.LoadFromFile(string keyFilePath, params string[] scopes)`; `static Task<T> RetryPolicy.WithOneRetryAsync<T>(Func<Task<T>> action, TimeSpan delay, CancellationToken ct = default)` and a `Task`-returning overload — the spec's "one retry with backoff" for transient Google API failures; `GoogleSheetsClient(GoogleCredential credential) : ISheetsClient`; `GoogleDriveClient(GoogleCredential credential) : IDriveClient` (both wired into DI in Task 9; their Google-calling bodies aren't exercised by unit tests since they need real Google credentials, but `RetryPolicy` itself is fully unit tested).

- [ ] **Step 1: Add the Google API packages**

```bash
dotnet add fantasy-keeper-app/backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj package Google.Apis.Sheets.v4
dotnet add fantasy-keeper-app/backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj package Google.Apis.Drive.v3
dotnet add fantasy-keeper-app/backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj package Google.Apis.Auth
```

- [ ] **Step 2: Write the failing tests**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/GoogleCredentialLoaderTests.cs
using FantasyKeeper.Api.Services.Google;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class GoogleCredentialLoaderTests
{
    [Fact]
    public void LoadFromFile_MissingFile_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "key.json");

        var ex = Assert.Throws<FileNotFoundException>(() =>
            GoogleCredentialLoader.LoadFromFile(missingPath, "https://www.googleapis.com/auth/spreadsheets"));

        Assert.Contains("service account key file not found", ex.Message);
    }
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/RetryPolicyTests.cs
using FantasyKeeper.Api.Services.Google;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class RetryPolicyTests
{
    [Fact]
    public async Task WithOneRetryAsync_FailsOnceThenSucceeds_ReturnsResult()
    {
        var attempts = 0;

        var result = await RetryPolicy.WithOneRetryAsync(() =>
        {
            attempts++;
            if (attempts == 1) throw new InvalidOperationException("transient");
            return Task.FromResult(42);
        }, TimeSpan.Zero);

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task WithOneRetryAsync_FailsTwice_ThrowsAfterSecondAttempt()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RetryPolicy.WithOneRetryAsync<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("still failing");
        }, TimeSpan.Zero));

        Assert.Equal(2, attempts);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL to compile — `GoogleCredentialLoader`, `RetryPolicy` don't exist yet.

- [ ] **Step 4: Write the implementation**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Google/GoogleCredentialLoader.cs
using Google.Apis.Auth.OAuth2;

namespace FantasyKeeper.Api.Services.Google;

public static class GoogleCredentialLoader
{
    public static GoogleCredential LoadFromFile(string keyFilePath, params string[] scopes)
    {
        if (!File.Exists(keyFilePath))
        {
            throw new FileNotFoundException(
                $"Google service account key file not found at '{keyFilePath}'. " +
                "See README.md 'Google Cloud setup' for how to create one.", keyFilePath);
        }

        using var stream = File.OpenRead(keyFilePath);
        return GoogleCredential.FromStream(stream).CreateScoped(scopes);
    }
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Google/RetryPolicy.cs
namespace FantasyKeeper.Api.Services.Google;

public static class RetryPolicy
{
    public static async Task<T> WithOneRetryAsync<T>(Func<Task<T>> action, TimeSpan delay, CancellationToken ct = default)
    {
        try
        {
            return await action();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await Task.Delay(delay, ct);
            return await action();
        }
    }

    public static async Task WithOneRetryAsync(Func<Task> action, TimeSpan delay, CancellationToken ct = default)
    {
        try
        {
            await action();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await Task.Delay(delay, ct);
            await action();
        }
    }
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Google/GoogleSheetsClient.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace FantasyKeeper.Api.Services.Google;

public class GoogleSheetsClient : ISheetsClient
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private readonly SheetsService _service;

    public GoogleSheetsClient(GoogleCredential credential)
    {
        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FantasyKeeper"
        });
    }

    public Task<IReadOnlyList<IReadOnlyList<string>>> GetRangeAsync(string spreadsheetId, string sheetTab, string range, CancellationToken ct = default) =>
        RetryPolicy.WithOneRetryAsync(async () =>
        {
            var request = _service.Spreadsheets.Values.Get(spreadsheetId, $"'{sheetTab}'!{range}");
            request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMATTEDVALUE;
            var response = await request.ExecuteAsync(ct);

            return (IReadOnlyList<IReadOnlyList<string>>)(response.Values ?? new List<IList<object>>())
                .Select(row => (IReadOnlyList<string>)row.Select(cell => cell?.ToString() ?? "").ToList())
                .ToList();
        }, RetryDelay, ct);

    public Task UpdateRangeAsync(string spreadsheetId, string sheetTab, string range, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken ct = default) =>
        RetryPolicy.WithOneRetryAsync(async () =>
        {
            var body = new ValueRange
            {
                Values = values.Select(row => (IList<object>)row.Select(v => (object)v).ToList()).ToList()
            };

            var request = _service.Spreadsheets.Values.Update(body, spreadsheetId, $"'{sheetTab}'!{range}");
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await request.ExecuteAsync(ct);
        }, RetryDelay, ct);
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Google/GoogleDriveClient.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using DrivePermission = Google.Apis.Drive.v3.Data.Permission;

namespace FantasyKeeper.Api.Services.Google;

public class GoogleDriveClient : IDriveClient
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private readonly DriveService _service;

    public GoogleDriveClient(GoogleCredential credential)
    {
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FantasyKeeper"
        });
    }

    public Task<string> CopyFileAsync(string fileId, string newTitle, CancellationToken ct = default) =>
        RetryPolicy.WithOneRetryAsync(async () =>
        {
            var copyMetadata = new DriveFile { Name = newTitle };
            var request = _service.Files.Copy(copyMetadata, fileId);
            var result = await request.ExecuteAsync(ct);
            return result.Id;
        }, RetryDelay, ct);

    public Task ShareFileAsync(string fileId, string email, CancellationToken ct = default) =>
        RetryPolicy.WithOneRetryAsync(async () =>
        {
            var permission = new DrivePermission { Type = "user", Role = "writer", EmailAddress = email };
            var request = _service.Permissions.Create(permission, fileId);
            request.SendNotificationEmail = false;
            await request.ExecuteAsync(ct);
        }, RetryDelay, ct);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: add real Google Sheets/Drive clients with one-retry backoff"
```

---

## Task 8: Dev (fake) Sheets/Drive clients for demo mode

**Files:**
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Dev/DevSheetsClient.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Dev/DevDriveClient.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/DevClientsTests.cs`

**Interfaces:**
- Consumes: `ISheetsClient` (Task 5), `IDriveClient` (Task 6).
- Produces: `DevSheetsClient : ISheetsClient` seeded with data for `spreadsheetId="dev-sheet-2026"`, `sheetTab="2026 Keepers"`, ranges `H8:N13` and `C8:F13`; `DevDriveClient : IDriveClient`. Both wired into DI in Task 9 when `Google:UseDevClients` is `true`. The seeded spreadsheet ID and ranges must match the seed config written in Task 10.

- [ ] **Step 1: Write the failing tests**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/DevClientsTests.cs
using FantasyKeeper.Api.Services.Dev;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class DevClientsTests
{
    [Fact]
    public async Task DevSheetsClient_GetRange_ReturnsSeededData()
    {
        var client = new DevSheetsClient();

        var values = await client.GetRangeAsync("dev-sheet-2026", "2026 Keepers", "C8:F13");

        Assert.Equal(6, values.Count);
        Assert.Equal("T. Story", values[0][0]);
    }

    [Fact]
    public async Task DevSheetsClient_UpdateThenGet_RoundTrips()
    {
        var client = new DevSheetsClient();
        var newValues = new List<IReadOnlyList<string>> { new List<string> { "New Guy", "1", "10", "2" } };

        await client.UpdateRangeAsync("dev-sheet-2026", "2026 Keepers", "C8:F8", newValues);
        var result = await client.GetRangeAsync("dev-sheet-2026", "2026 Keepers", "C8:F8");

        Assert.Equal("New Guy", result[0][0]);
    }

    [Fact]
    public async Task DevDriveClient_CopyFile_ReturnsDistinctIds()
    {
        var client = new DevDriveClient();

        var first = await client.CopyFileAsync("dev-sheet-2026", "2027 Season");
        var second = await client.CopyFileAsync("dev-sheet-2026", "2028 Season");

        Assert.NotEqual(first, second);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL to compile — `DevSheetsClient`, `DevDriveClient` don't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Dev/DevSheetsClient.cs
namespace FantasyKeeper.Api.Services.Dev;

public class DevSheetsClient : ISheetsClient
{
    private readonly Dictionary<string, List<List<string>>> _data = new();

    public DevSheetsClient()
    {
        Seed("dev-sheet-2026", "2026 Keepers", "H8:N13", new List<List<string>>
        {
            new() { "T. Story", "#1 - 2/3", "3", "8", "8", "281", "" },
            new() { "", "", "", "", "", "", "" },
            new() { "", "", "", "", "", "", "" },
            new() { "", "", "", "", "", "", "" },
            new() { "", "", "", "", "", "", "" },
            new() { "", "", "", "", "", "", "" }
        });

        Seed("dev-sheet-2026", "2026 Keepers", "C8:F13", new List<List<string>>
        {
            new() { "T. Story", "1", "14", "2" },
            new() { "", "", "", "" },
            new() { "", "", "", "" },
            new() { "", "", "", "" },
            new() { "", "", "", "" },
            new() { "", "", "", "" }
        });
    }

    private void Seed(string spreadsheetId, string tab, string range, List<List<string>> values) =>
        _data[Key(spreadsheetId, tab, range)] = values;

    public Task<IReadOnlyList<IReadOnlyList<string>>> GetRangeAsync(string spreadsheetId, string sheetTab, string range, CancellationToken ct = default)
    {
        var values = _data.TryGetValue(Key(spreadsheetId, sheetTab, range), out var v) ? v : new List<List<string>>();
        return Task.FromResult((IReadOnlyList<IReadOnlyList<string>>)values.Select(r => (IReadOnlyList<string>)r).ToList());
    }

    public Task UpdateRangeAsync(string spreadsheetId, string sheetTab, string range, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken ct = default)
    {
        _data[Key(spreadsheetId, sheetTab, range)] = values.Select(r => r.ToList()).ToList();
        return Task.CompletedTask;
    }

    private static string Key(string spreadsheetId, string tab, string range) => $"{spreadsheetId}|{tab}|{range}";
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Services/Dev/DevDriveClient.cs
namespace FantasyKeeper.Api.Services.Dev;

public class DevDriveClient : IDriveClient
{
    private int _copyCount;

    public Task<string> CopyFileAsync(string fileId, string newTitle, CancellationToken ct = default)
    {
        _copyCount++;
        return Task.FromResult($"dev-sheet-copy-{_copyCount}");
    }

    public Task ShareFileAsync(string fileId, string email, CancellationToken ct = default) => Task.CompletedTask;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: add in-memory Dev Sheets/Drive clients for credential-free demo mode"
```

---

## Task 9: Composition root, endpoints, and integration tests

**Files:**
- Modify: `fantasy-keeper-app/backend/FantasyKeeper.Api/Program.cs`
- Modify: `fantasy-keeper-app/backend/FantasyKeeper.Api/Properties/launchSettings.json`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/appsettings.json`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Endpoints/AuthEndpoints.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Endpoints/SeasonEndpoints.cs`
- Create: `fantasy-keeper-app/backend/FantasyKeeper.Api/Endpoints/KeeperEndpoints.cs`
- Test: `fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs`

**Interfaces:**
- Consumes: `AuthService` (Task 3), `KeepersService` (Task 5), `SeasonService` (Task 6), `GoogleSheetsClient`/`GoogleDriveClient` (Task 7), `DevSheetsClient`/`DevDriveClient` (Task 8), `JsonConfigStore` (Task 2).
- Produces: HTTP routes `POST /api/auth`, `GET /api/seasons`, `GET /api/keepers`, `PUT /api/keepers`, `POST /api/admin/seasons`, all consumed by the frontend starting Task 12.

- [ ] **Step 1: Write the failing integration tests**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeeperEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    // The server serializes responses with a camelCase naming policy
    // (Task 9's ConfigureHttpJsonOptions). HttpContent.ReadFromJsonAsync<T>()
    // defaults to case-sensitive matching when no options are passed, which
    // would silently leave PascalCase record properties unset — so every
    // response read below passes this explicit options instance.
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _configRoot;

    public KeeperEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(_configRoot, "team-mappings"));

        var configStore = new JsonConfigStore(_configRoot);
        configStore.SaveSeasons(new List<Season>
        {
            new("season-1", "2026", "dev-sheet-2026", "active", DateTimeOffset.UtcNow)
        });
        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"}]""");
        configStore.SaveTeamMappings("season-1", new Dictionary<string, TeamMapping>
        {
            ["b-squared"] = new TeamMapping("2026 Keepers", "H8:N13", "C8:F13")
        });

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConfigRoot"] = _configRoot,
                    ["Google:UseDevClients"] = "true",
                    ["AdminPin"] = "9999"
                });
            });
        });
    }

    public void Dispose() => Directory.Delete(_configRoot, recursive: true);

    [Fact]
    public async Task GetKeepers_WithValidTeamPin_ReturnsTeamData()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=1111");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("B Squared", data!.TeamName);
    }

    [Fact]
    public async Task GetKeepers_WithInvalidPin_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=0000");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_WithInvalidContractType_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(Enumerable.Range(0, 6)
            .Select(i => i == 0 ? new KeeperRow("New Guy", 3, 10, 2) : new KeeperRow("", null, null, null))
            .ToList());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111&seasonId=season-1", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAdminSeasons_WithOwnerPin_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/seasons?pin=1111", new { label = "2027" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAdminSeasons_WithAdminPin_CreatesSeason()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/seasons?pin=9999", new { label = "2027" });
        response.EnsureSuccessStatusCode();
        var season = await response.Content.ReadFromJsonAsync<Season>(ResponseJsonOptions);
        Assert.Equal("2027", season!.Label);
        Assert.True(season.IsActive);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: FAIL — routes return 404, `appsettings.json` config keys missing.

- [ ] **Step 3: Write the endpoint modules**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Endpoints/AuthEndpoints.cs
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class AuthEndpoints
{
    public record AuthRequest(string Pin);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth", (AuthRequest request, AuthService authService) =>
        {
            var result = authService.ResolvePin(request.Pin);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });
    }
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Endpoints/SeasonEndpoints.cs
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class SeasonEndpoints
{
    public record CreateSeasonRequest(string Label);

    public static void MapSeasonEndpoints(this WebApplication app)
    {
        app.MapGet("/api/seasons", (string pin, AuthService authService, SeasonService seasonService) =>
        {
            var auth = authService.ResolvePin(pin);
            return auth is null ? Results.Unauthorized() : Results.Ok(seasonService.ListSeasons());
        });

        app.MapPost("/api/admin/seasons", async (string pin, CreateSeasonRequest request, AuthService authService, SeasonService seasonService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            var season = await seasonService.CreateNewSeasonAsync(request.Label);
            return Results.Ok(season);
        });
    }
}
```

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Endpoints/KeeperEndpoints.cs
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class KeeperEndpoints
{
    public static void MapKeeperEndpoints(this WebApplication app)
    {
        app.MapGet("/api/keepers", async (string pin, string? seasonId, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Owner || auth.TeamId is null) return Results.Unauthorized();

            var targetSeasonId = seasonId ?? auth.SeasonId!;

            try
            {
                return Results.Ok(await keepersService.GetKeeperDataAsync(targetSeasonId, auth.TeamId));
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPut("/api/keepers", async (string pin, string seasonId, KeeperSubmission submission, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Owner || auth.TeamId is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(await keepersService.UpdateKeeperDataAsync(seasonId, auth.TeamId, submission));
            }
            catch (SeasonNotActiveException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (KeeperValidationException ex)
            {
                return Results.BadRequest(new { errors = ex.Errors });
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });
    }
}
```

- [ ] **Step 4: Rewrite Program.cs as the composition root**

```csharp
// fantasy-keeper-app/backend/FantasyKeeper.Api/Program.cs
using System.Text.Json.Serialization;
using FantasyKeeper.Api.Endpoints;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Services.Dev;
using FantasyKeeper.Api.Services.Google;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var configRoot = Path.GetFullPath(builder.Configuration["ConfigRoot"] ?? "config");
Directory.CreateDirectory(Path.Combine(configRoot, "team-mappings"));
builder.Services.AddSingleton<IConfigStore>(new JsonConfigStore(configRoot));

var useDevClients = builder.Configuration.GetValue<bool>("Google:UseDevClients");
if (useDevClients)
{
    builder.Services.AddSingleton<ISheetsClient, DevSheetsClient>();
    builder.Services.AddSingleton<IDriveClient, DevDriveClient>();
}
else
{
    var keyPath = builder.Configuration["Google:ServiceAccountKeyPath"]
        ?? throw new InvalidOperationException("Google:ServiceAccountKeyPath must be set when Google:UseDevClients is false.");
    var credential = GoogleCredentialLoader.LoadFromFile(
        keyPath,
        "https://www.googleapis.com/auth/spreadsheets",
        "https://www.googleapis.com/auth/drive");
    builder.Services.AddSingleton(credential);
    builder.Services.AddSingleton<ISheetsClient, GoogleSheetsClient>();
    builder.Services.AddSingleton<IDriveClient, GoogleDriveClient>();
}

var adminPin = builder.Configuration["AdminPin"] ?? throw new InvalidOperationException("AdminPin must be configured.");
var commissionerEmail = builder.Configuration["Google:CommissionerEmail"] ?? "";

builder.Services.AddSingleton(sp => new AuthService(sp.GetRequiredService<IConfigStore>(), adminPin));
builder.Services.AddSingleton<KeepersService>();
builder.Services.AddSingleton(sp => new SeasonService(sp.GetRequiredService<IConfigStore>(), sp.GetRequiredService<IDriveClient>(), commissionerEmail));

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

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapSeasonEndpoints();
app.MapKeeperEndpoints();

app.Run();

public partial class Program { }
```

- [ ] **Step 5: Add appsettings.json with non-secret defaults**

```json
// fantasy-keeper-app/backend/FantasyKeeper.Api/appsettings.json
{
  "ConfigRoot": "../../config",
  "Google": {
    "UseDevClients": true,
    "ServiceAccountKeyPath": "",
    "CommissionerEmail": ""
  },
  "AdminPin": "0000",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [ ] **Step 6: Pin the Development launch URL**

Edit `fantasy-keeper-app/backend/FantasyKeeper.Api/Properties/launchSettings.json` — set the `"http"` profile's `"applicationUrl"` to `"http://localhost:5080"`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test fantasy-keeper-app/backend/FantasyKeeper.Api.Tests`
Expected: PASS (all tests across Tasks 1–9)

- [ ] **Step 8: Commit**

```bash
git add fantasy-keeper-app/backend
git commit -m "feat: wire composition root and HTTP endpoints"
```

---

## Task 10: Seed config, secrets hygiene, and README

**Files:**
- Create: `fantasy-keeper-app/config/seasons.json`
- Create: `fantasy-keeper-app/config/teams.json`
- Create: `fantasy-keeper-app/config/team-mappings/season-1.json`
- Create: `fantasy-keeper-app/.gitignore`
- Create: `fantasy-keeper-app/README.md`

**Interfaces:**
- Consumes: `Season`/`Team`/`TeamMapping` JSON shapes (Task 2), `DevSheetsClient`'s seeded spreadsheet id `dev-sheet-2026` and ranges `H8:N13`/`C8:F13` (Task 8).

- [ ] **Step 1: Write the seed config files**

```json
// fantasy-keeper-app/config/seasons.json
[
  {
    "id": "season-1",
    "label": "2026 Season",
    "googleSheetId": "dev-sheet-2026",
    "status": "active",
    "createdAt": "2026-08-22T00:00:00Z"
  }
]
```

```json
// fantasy-keeper-app/config/teams.json
[
  { "teamId": "b-squared", "name": "B Squared", "pin": "1111" }
]
```

```json
// fantasy-keeper-app/config/team-mappings/season-1.json
{
  "b-squared": {
    "sheetTab": "2026 Keepers",
    "existingContractsRange": "H8:N13",
    "newContractsRange": "C8:F13"
  }
}
```

- [ ] **Step 2: Write .gitignore**

```
# fantasy-keeper-app/.gitignore
**/bin/
**/obj/
**/node_modules/
**/dist/
backend/FantasyKeeper.Api/wwwroot/
backend/FantasyKeeper.Api/appsettings.Development.json
secrets/
```

- [ ] **Step 3: Write README.md**

```markdown
# Fantasy Keeper App

Friendly keeper-submission UI for the Worm Burners Dynasty League, backed
by the league's live Google Sheet. See
`docs/superpowers/specs/2026-08-21-fantasy-keeper-app-design.md` in the
repo root for the full design.

## Running locally (no Google account needed)

By default the backend runs against in-memory fake Sheets/Drive clients
seeded with data shaped like the real `2026 Keepers` tab — no Google
Cloud setup required to develop or demo the app.

```bash
dotnet run --project backend/FantasyKeeper.Api
```

The app listens on `http://localhost:5080`. Try:

```bash
curl "http://localhost:5080/api/keepers?pin=1111"
```

Seeded PINs: team PIN `1111` (B Squared), admin PIN `0000`.

## Going live against your real Google Sheet

1. In Google Cloud Console, create a project and enable the **Google
   Sheets API** and **Google Drive API**.
2. Create a service account, then create and download a JSON key for it.
   Save it as `fantasy-keeper-app/secrets/service-account.json` (this
   path is gitignored).
3. Open your league's Google Sheet, click Share, and add the service
   account's email address (from the key file) as an **Editor**.
4. Create `backend/FantasyKeeper.Api/appsettings.Development.json`
   (gitignored) with:

```json
{
  "Google": {
    "UseDevClients": false,
    "ServiceAccountKeyPath": "../../secrets/service-account.json",
    "CommissionerEmail": "you@example.com"
  },
  "AdminPin": "<pick a real admin PIN>"
}
```

5. Update `config/seasons.json` with the real Sheet's file ID (from its
   URL) and `config/team-mappings/season-1.json` with each team's actual
   cell ranges in the sheet.
6. Update `config/teams.json` with real team PINs.
```

- [ ] **Step 4: Manual verification**

Run: `dotnet run --project fantasy-keeper-app/backend/FantasyKeeper.Api`
Then: `curl "http://localhost:5080/api/keepers?pin=1111"`
Expected: JSON body with `"teamName":"B Squared"` and the seeded contract rows.

- [ ] **Step 5: Commit**

```bash
git add fantasy-keeper-app/config fantasy-keeper-app/.gitignore fantasy-keeper-app/README.md
git commit -m "feat: add seed config, secrets hygiene, and setup docs"
```

---

## Task 11: Frontend scaffold

**Files:**
- Create: `fantasy-keeper-app/frontend/` (Vite React-TS template)

**Interfaces:**
- Produces: a buildable Vite + React + TypeScript project at `fantasy-keeper-app/frontend`, consumed by all later frontend tasks.

- [ ] **Step 1: Scaffold the project**

```bash
npm create vite@latest fantasy-keeper-app/frontend -- --template react-ts
cd fantasy-keeper-app/frontend && npm install && cd -
```

- [ ] **Step 2: Verify it builds**

Run: `npm run build --prefix fantasy-keeper-app/frontend`
Expected: succeeds, producing `fantasy-keeper-app/frontend/dist/index.html`.

- [ ] **Step 3: Commit**

```bash
git add fantasy-keeper-app/frontend
git commit -m "feat: scaffold frontend with Vite + React + TypeScript"
```

---

## Task 12: Frontend types + API client

**Files:**
- Create: `fantasy-keeper-app/frontend/src/types.ts`
- Create: `fantasy-keeper-app/frontend/src/api/client.ts`
- Create: `fantasy-keeper-app/frontend/.env.development`

**Interfaces:**
- Consumes: JSON shapes produced by `AuthResult` (Task 3), `Season` (Task 2), `KeeperRow`/`ExistingContractRow`/`KeeperTeamData` (Task 5), routes from Task 9.
- Produces: `authenticate`, `getSeasons`, `getKeepers`, `updateKeepers`, `createSeason`, `ApiError` — used by Tasks 13–15.

- [ ] **Step 1: Write shared types**

```typescript
// fantasy-keeper-app/frontend/src/types.ts
export type AuthRole = "Owner" | "Admin";

export interface AuthResult {
  role: AuthRole;
  teamId: string | null;
  seasonId: string | null;
}

export interface Season {
  id: string;
  label: string;
  googleSheetId: string;
  status: "active" | "archived";
  createdAt: string;
}

export interface KeeperRow {
  player: string;
  contractType: number | null;
  salary: number | null;
  keeperYears: number | null;
}

export interface ExistingContractRow {
  player: string;
  contractInfo: string;
  lastYearSalary: number | null;
  leagueValue: number | null;
  thisYearSalary: number | null;
}

export interface KeeperTeamData {
  teamName: string;
  readOnly: boolean;
  existingContracts: ExistingContractRow[];
  newContracts: KeeperRow[];
}
```

- [ ] **Step 2: Write the API client**

```typescript
// fantasy-keeper-app/frontend/src/api/client.ts
import type { AuthResult, Season, KeeperTeamData, KeeperRow } from "../types";

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

export class ApiError extends Error {
  constructor(public status: number, public body: unknown) {
    super(`API request failed with status ${status}`);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
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

export function authenticate(pin: string): Promise<AuthResult> {
  return request<AuthResult>("/api/auth", {
    method: "POST",
    body: JSON.stringify({ pin })
  });
}

export function getSeasons(pin: string): Promise<Season[]> {
  return request<Season[]>(`/api/seasons?pin=${encodeURIComponent(pin)}`);
}

export function getKeepers(pin: string, seasonId?: string): Promise<KeeperTeamData> {
  const query = seasonId ? `&seasonId=${encodeURIComponent(seasonId)}` : "";
  return request<KeeperTeamData>(`/api/keepers?pin=${encodeURIComponent(pin)}${query}`);
}

export function updateKeepers(pin: string, seasonId: string, newContracts: KeeperRow[]): Promise<KeeperTeamData> {
  return request<KeeperTeamData>(
    `/api/keepers?pin=${encodeURIComponent(pin)}&seasonId=${encodeURIComponent(seasonId)}`,
    { method: "PUT", body: JSON.stringify({ newContracts }) }
  );
}

export function createSeason(pin: string, label: string): Promise<Season> {
  return request<Season>(`/api/admin/seasons?pin=${encodeURIComponent(pin)}`, {
    method: "POST",
    body: JSON.stringify({ label })
  });
}
```

- [ ] **Step 3: Point the dev client at the backend**

```
# fantasy-keeper-app/frontend/.env.development
VITE_API_BASE_URL=http://localhost:5080
```

- [ ] **Step 4: Verify it builds**

Run: `npm run build --prefix fantasy-keeper-app/frontend`
Expected: succeeds with no type errors.

- [ ] **Step 5: Commit**

```bash
git add fantasy-keeper-app/frontend
git commit -m "feat: add frontend types and typed API client"
```

---

## Task 13: useAuth hook + PinEntryScreen

**Files:**
- Create: `fantasy-keeper-app/frontend/src/state/useAuth.ts`
- Create: `fantasy-keeper-app/frontend/src/screens/PinEntryScreen.tsx`

**Interfaces:**
- Consumes: `authenticate`, `ApiError` (Task 12), `AuthResult` (Task 12).
- Produces: `useAuth()` returning `{ pin, auth, login, logout, error, isLoading }`; `<PinEntryScreen onSubmit error isLoading />` — both consumed by Task 16's `App.tsx`.

- [ ] **Step 1: Write the auth hook**

```typescript
// fantasy-keeper-app/frontend/src/state/useAuth.ts
import { useCallback, useState } from "react";
import { authenticate, ApiError } from "../api/client";
import type { AuthResult } from "../types";

interface AuthState {
  pin: string;
  auth: AuthResult;
}

const STORAGE_KEY = "fantasy-keeper-auth";

function loadStoredAuth(): AuthState | null {
  const raw = sessionStorage.getItem(STORAGE_KEY);
  return raw ? (JSON.parse(raw) as AuthState) : null;
}

export function useAuth() {
  const [state, setState] = useState<AuthState | null>(loadStoredAuth);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const login = useCallback(async (pin: string) => {
    setIsLoading(true);
    setError(null);
    try {
      const auth = await authenticate(pin);
      const next = { pin, auth };
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next));
      setState(next);
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 401
          ? "That PIN wasn't recognized. Check with your commissioner."
          : "Something went wrong logging in. Try again."
      );
    } finally {
      setIsLoading(false);
    }
  }, []);

  const logout = useCallback(() => {
    sessionStorage.removeItem(STORAGE_KEY);
    setState(null);
  }, []);

  return { pin: state?.pin ?? null, auth: state?.auth ?? null, login, logout, error, isLoading };
}
```

- [ ] **Step 2: Write the PIN entry screen**

```typescript
// fantasy-keeper-app/frontend/src/screens/PinEntryScreen.tsx
import { useState, type FormEvent } from "react";

interface Props {
  onSubmit: (pin: string) => void;
  error: string | null;
  isLoading: boolean;
}

export function PinEntryScreen({ onSubmit, error, isLoading }: Props) {
  const [pin, setPin] = useState("");

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (pin.trim()) {
      onSubmit(pin.trim());
    }
  }

  return (
    <form onSubmit={handleSubmit} className="pin-entry">
      <h1>Worm Burners Keepers</h1>
      <label htmlFor="pin">Enter your team PIN</label>
      <input
        id="pin"
        type="password"
        inputMode="numeric"
        value={pin}
        onChange={(event) => setPin(event.target.value)}
        autoFocus
      />
      <button type="submit" disabled={isLoading || !pin.trim()}>
        {isLoading ? "Checking..." : "Continue"}
      </button>
      {error && <p role="alert">{error}</p>}
    </form>
  );
}
```

- [ ] **Step 3: Verify it builds**

Run: `npm run build --prefix fantasy-keeper-app/frontend`
Expected: succeeds with no type errors.

- [ ] **Step 4: Commit**

```bash
git add fantasy-keeper-app/frontend
git commit -m "feat: add PIN-based auth hook and entry screen"
```

---

## Task 14: KeeperFormScreen

**Files:**
- Create: `fantasy-keeper-app/frontend/src/screens/KeeperFormScreen.tsx`

**Interfaces:**
- Consumes: `getKeepers`, `getSeasons`, `updateKeepers`, `ApiError` (Task 12); `KeeperRow`, `KeeperTeamData`, `Season` (Task 12).
- Produces: `<KeeperFormScreen pin defaultSeasonId />` — consumed by Task 16's `App.tsx`.

- [ ] **Step 1: Write the screen**

```typescript
// fantasy-keeper-app/frontend/src/screens/KeeperFormScreen.tsx
import { useCallback, useEffect, useState } from "react";
import { getKeepers, getSeasons, updateKeepers, ApiError } from "../api/client";
import type { KeeperRow, KeeperTeamData, Season } from "../types";

interface Props {
  pin: string;
  defaultSeasonId: string;
}

export function KeeperFormScreen({ pin, defaultSeasonId }: Props) {
  const [seasons, setSeasons] = useState<Season[]>([]);
  const [seasonId, setSeasonId] = useState(defaultSeasonId);
  const [data, setData] = useState<KeeperTeamData | null>(null);
  const [rows, setRows] = useState<KeeperRow[]>([]);
  const [status, setStatus] = useState<"idle" | "loading" | "saving" | "error">("loading");
  const [message, setMessage] = useState<string | null>(null);

  const loadKeepers = useCallback(async (targetSeasonId: string) => {
    setStatus("loading");
    setMessage(null);
    try {
      const result = await getKeepers(pin, targetSeasonId);
      setData(result);
      setRows(result.newContracts);
      setStatus("idle");
    } catch {
      setStatus("error");
      setMessage("Couldn't load your keepers. Try again.");
    }
  }, [pin]);

  useEffect(() => {
    getSeasons(pin).then(setSeasons).catch(() => setSeasons([]));
  }, [pin]);

  useEffect(() => {
    loadKeepers(seasonId);
  }, [seasonId, loadKeepers]);

  function updateRow(index: number, field: keyof KeeperRow, value: string) {
    setRows((prev) =>
      prev.map((row, i) => {
        if (i !== index) return row;
        if (field === "player") return { ...row, player: value };
        return { ...row, [field]: value === "" ? null : Number(value) };
      })
    );
  }

  async function handleSave() {
    setStatus("saving");
    setMessage(null);
    try {
      const result = await updateKeepers(pin, seasonId, rows);
      setData(result);
      setRows(result.newContracts);
      setStatus("idle");
      setMessage("Saved.");
    } catch (err) {
      setStatus("idle");
      if (err instanceof ApiError && err.status === 400) {
        const body = err.body as { errors?: string[] };
        setMessage((body.errors ?? ["Some fields are invalid."]).join(" "));
      } else if (err instanceof ApiError && err.status === 409) {
        setMessage("This season is no longer open for edits.");
      } else {
        setMessage("Couldn't save. Try again.");
      }
    }
  }

  if (status === "loading" || !data) {
    return <p>Loading...</p>;
  }

  const readOnly = data.readOnly;

  return (
    <div className="keeper-form">
      <h1>{data.teamName} — Keepers</h1>

      <label htmlFor="season">Season</label>
      <select id="season" value={seasonId} onChange={(event) => setSeasonId(event.target.value)}>
        {seasons.map((season) => (
          <option key={season.id} value={season.id}>
            {season.label} {season.status === "archived" ? "(archived)" : ""}
          </option>
        ))}
      </select>

      <h2>Existing Contracts</h2>
      <table>
        <thead>
          <tr><th>Player</th><th>Contract</th><th>Last Year</th><th>League Value</th><th>This Year</th></tr>
        </thead>
        <tbody>
          {data.existingContracts.map((row, i) => (
            <tr key={i}>
              <td>{row.player}</td>
              <td>{row.contractInfo}</td>
              <td>{row.lastYearSalary ?? ""}</td>
              <td>{row.leagueValue ?? ""}</td>
              <td>{row.thisYearSalary ?? ""}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2>New Contracts</h2>
      <table>
        <thead>
          <tr><th>Player</th><th>Contract 1 or 2</th><th>Salary</th><th>Keeper Years</th></tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              <td><input value={row.player} disabled={readOnly} onChange={(e) => updateRow(i, "player", e.target.value)} /></td>
              <td><input value={row.contractType ?? ""} disabled={readOnly} onChange={(e) => updateRow(i, "contractType", e.target.value)} /></td>
              <td><input value={row.salary ?? ""} disabled={readOnly} onChange={(e) => updateRow(i, "salary", e.target.value)} /></td>
              <td><input value={row.keeperYears ?? ""} disabled={readOnly} onChange={(e) => updateRow(i, "keeperYears", e.target.value)} /></td>
            </tr>
          ))}
        </tbody>
      </table>

      {!readOnly && (
        <button onClick={handleSave} disabled={status === "saving"}>
          {status === "saving" ? "Saving..." : "Save Keepers"}
        </button>
      )}
      {message && <p role="status">{message}</p>}
    </div>
  );
}
```

- [ ] **Step 2: Verify it builds**

Run: `npm run build --prefix fantasy-keeper-app/frontend`
Expected: succeeds with no type errors.

- [ ] **Step 3: Commit**

```bash
git add fantasy-keeper-app/frontend
git commit -m "feat: add keeper form screen with season switcher"
```

---

## Task 15: AdminPanel

**Files:**
- Create: `fantasy-keeper-app/frontend/src/screens/AdminPanel.tsx`

**Interfaces:**
- Consumes: `getSeasons`, `createSeason` (Task 12); `Season` (Task 12).
- Produces: `<AdminPanel pin />` — consumed by Task 16's `App.tsx`.

- [ ] **Step 1: Write the screen**

```typescript
// fantasy-keeper-app/frontend/src/screens/AdminPanel.tsx
import { useEffect, useState, type FormEvent } from "react";
import { getSeasons, createSeason } from "../api/client";
import type { Season } from "../types";

interface Props {
  pin: string;
}

export function AdminPanel({ pin }: Props) {
  const [seasons, setSeasons] = useState<Season[]>([]);
  const [label, setLabel] = useState("");
  const [status, setStatus] = useState<"idle" | "creating">("idle");
  const [message, setMessage] = useState<string | null>(null);

  function refresh() {
    getSeasons(pin).then(setSeasons).catch(() => setSeasons([]));
  }

  useEffect(refresh, [pin]);

  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    if (!label.trim()) return;
    setStatus("creating");
    setMessage(null);
    try {
      await createSeason(pin, label.trim());
      setLabel("");
      setMessage("New season created.");
      refresh();
    } catch {
      setMessage("Couldn't create the new season. Try again.");
    } finally {
      setStatus("idle");
    }
  }

  return (
    <div className="admin-panel">
      <h1>Season Administration</h1>
      <ul>
        {seasons.map((season) => (
          <li key={season.id}>{season.label} — {season.status}</li>
        ))}
      </ul>
      <form onSubmit={handleCreate}>
        <label htmlFor="label">New season label</label>
        <input id="label" value={label} onChange={(event) => setLabel(event.target.value)} />
        <button type="submit" disabled={status === "creating" || !label.trim()}>
          {status === "creating" ? "Creating..." : "Start New Season"}
        </button>
      </form>
      {message && <p role="status">{message}</p>}
    </div>
  );
}
```

- [ ] **Step 2: Verify it builds**

Run: `npm run build --prefix fantasy-keeper-app/frontend`
Expected: succeeds with no type errors.

- [ ] **Step 3: Commit**

```bash
git add fantasy-keeper-app/frontend
git commit -m "feat: add admin panel for season creation"
```

---

## Task 16: App wiring + manual end-to-end verification

**Files:**
- Modify: `fantasy-keeper-app/frontend/src/App.tsx`

**Interfaces:**
- Consumes: `useAuth` (Task 13), `PinEntryScreen` (Task 13), `KeeperFormScreen` (Task 14), `AdminPanel` (Task 15).

- [ ] **Step 1: Wire the screens together**

```typescript
// fantasy-keeper-app/frontend/src/App.tsx
import { useAuth } from "./state/useAuth";
import { PinEntryScreen } from "./screens/PinEntryScreen";
import { KeeperFormScreen } from "./screens/KeeperFormScreen";
import { AdminPanel } from "./screens/AdminPanel";

export default function App() {
  const { pin, auth, login, logout, error, isLoading } = useAuth();

  if (!pin || !auth) {
    return <PinEntryScreen onSubmit={login} error={error} isLoading={isLoading} />;
  }

  return (
    <div>
      <button onClick={logout}>Log out</button>
      {auth.role === "Admin" ? (
        <AdminPanel pin={pin} />
      ) : (
        <KeeperFormScreen pin={pin} defaultSeasonId={auth.seasonId!} />
      )}
    </div>
  );
}
```

- [ ] **Step 2: Verify it builds**

Run: `npm run build --prefix fantasy-keeper-app/frontend`
Expected: succeeds with no type errors.

- [ ] **Step 3: Manual end-to-end verification (browser)**

In one terminal: `dotnet run --project fantasy-keeper-app/backend/FantasyKeeper.Api`
In another: `npm run dev --prefix fantasy-keeper-app/frontend`

Open the Vite dev URL in a browser:
1. Enter PIN `1111` → confirm the keeper form loads with "B Squared" and the seeded "T. Story" row.
2. Edit a blank row (player/contract/salary/years), click "Save Keepers" → confirm "Saved." appears and the row persists on reload.
3. Try an invalid contract type (e.g. `3`) → confirm a validation message appears and nothing is saved.
4. Log out, log back in with admin PIN `0000` → confirm the Season Administration panel loads.
5. Enter a new season label and click "Start New Season" → confirm it appears in the season list.
6. Log back in with `1111`, use the season selector to switch to the new season → confirm the form is editable there and the old season now shows read-only inputs.

- [ ] **Step 4: Commit**

```bash
git add fantasy-keeper-app/frontend
git commit -m "feat: wire PIN entry, keeper form, and admin panel into App"
```

---

## Task 17: Single-process deployment (serve SPA from the backend)

**Files:**
- Modify: `fantasy-keeper-app/frontend/vite.config.ts`
- Modify: `fantasy-keeper-app/backend/FantasyKeeper.Api/Program.cs`

**Interfaces:**
- Consumes: built frontend output (Task 16), `Program.cs` composition root (Task 9).

- [ ] **Step 1: Point the frontend build at the backend's wwwroot**

```typescript
// fantasy-keeper-app/frontend/vite.config.ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../backend/FantasyKeeper.Api/wwwroot",
    emptyOutDir: true
  }
});
```

- [ ] **Step 2: Serve static files and fall back to index.html**

In `fantasy-keeper-app/backend/FantasyKeeper.Api/Program.cs`, add immediately after `var app = builder.Build();`:

```csharp
app.UseStaticFiles();
```

And immediately before `app.Run();`, add:

```csharp
app.MapFallbackToFile("index.html");
```

- [ ] **Step 3: Build the frontend into the backend and verify single-process serving**

```bash
npm run build --prefix fantasy-keeper-app/frontend
dotnet run --project fantasy-keeper-app/backend/FantasyKeeper.Api
```

In another terminal:

```bash
curl -s http://localhost:5080/ | grep -q 'id="root"' && echo "SPA served OK"
curl -s "http://localhost:5080/api/keepers?pin=1111" | grep -q teamName && echo "API served OK"
```

Expected: both echo lines print, confirming one process serves both the SPA and the API.

- [ ] **Step 4: Commit**

```bash
git add fantasy-keeper-app/frontend/vite.config.ts fantasy-keeper-app/backend/FantasyKeeper.Api/Program.cs
git commit -m "feat: serve built SPA from the backend for single-process deployment"
```
