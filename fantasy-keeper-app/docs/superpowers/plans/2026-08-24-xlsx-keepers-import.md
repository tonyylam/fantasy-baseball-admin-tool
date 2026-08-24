# xlsx Keepers Import/Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the live Google Sheets/Drive integration with an admin-uploaded xlsx file as the source of truth for keeper data, including an import review screen, xlsx export, and a delete-checkbox affordance in the team edit form.

**Architecture:** The backend parses an uploaded xlsx by locating a stable header-row pattern that anchors each team's block (no hand-configured cell ranges), holds an admin review step before committing, stores parsed state as JSON plus the original xlsx bytes on disk (no database), and writes only the New Contracts cells back into those original bytes on export. The frontend drops the season concept entirely (single current dataset) and adds an admin import/review/export UI plus a delete-checkbox on each team's edit row.

**Tech Stack:** ASP.NET Core minimal API (.NET 8), ClosedXML for xlsx read/write, React + TypeScript (Vite), xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-xlsx-keepers-import-design.md` (relative to `fantasy-keeper-app/`)

## Global Constraints

- No database — all state is JSON files and the stashed xlsx on the server's disk (matches the project's existing "no database" convention).
- No live Google Sheets/Drive connection — all such code is removed by the end of this plan.
- Exactly one current working dataset at a time — no season list, no season history browsing in the app.
- Export only ever rewrites the New Contracts cells (`C`–`F`) inside each team's already-detected block; every other cell, formula, tab, and style in the uploaded file is left untouched.
- Backend services are synchronous where the underlying work is local file I/O — no `async`/`Task` ceremony for work that doesn't need it.
- Frontend has no test framework (matches existing project convention) — frontend tasks are verified manually via the Vite dev server, not with automated tests.
- JSON over the wire uses camelCase (already configured in `Program.cs` via `ConfigureHttpJsonOptions`); every new C# record's property names should read naturally in camelCase on the frontend.

---

## Task 1: Keepers data store

**Files:**
- Create: `backend/FantasyKeeper.Api/Models/KeepersData.cs`
- Create: `backend/FantasyKeeper.Api/Services/IKeepersDataStore.cs`
- Create: `backend/FantasyKeeper.Api/Services/FileKeepersDataStore.cs`
- Create: `backend/FantasyKeeper.Api.Tests/Fakes/FakeKeepersDataStore.cs`
- Create: `backend/FantasyKeeper.Api.Tests/FileKeepersDataStoreTests.cs`
- Modify: `backend/FantasyKeeper.Api/Program.cs`
- Modify: `backend/FantasyKeeper.Api/appsettings.json`
- Modify: `fantasy-keeper-app/.gitignore` (add `data/`)

**Interfaces:**
- Produces: `StoredTeamKeepers(string RawNameInSheet, int HeaderRow, IReadOnlyList<int> NewContractsRows, IReadOnlyList<KeeperRow> NewContracts, IReadOnlyList<ExistingContractRow> ExistingContracts)`, `KeepersData(string SourceFileName, string SheetName, DateTimeOffset LastUpdatedUtc, IReadOnlyDictionary<string, StoredTeamKeepers> Teams)`, `IKeepersDataStore { KeepersData? LoadData(); void SaveData(KeepersData data); void SaveWorkbook(byte[] bytes); byte[]? LoadWorkbook(); }`, `FileKeepersDataStore(string dataRoot)` implementing it, and `FakeKeepersDataStore` (test double with public `Data`/`Workbook` fields).
- Consumes: `KeeperRow`, `ExistingContractRow` (existing models, unchanged).

- [ ] **Step 1: Write the failing round-trip test**

Create `backend/FantasyKeeper.Api.Tests/FileKeepersDataStoreTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class FileKeepersDataStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileKeepersDataStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadData_WhenFileMissing_ReturnsNull()
    {
        var store = new FileKeepersDataStore(_tempDir);
        Assert.Null(store.LoadData());
    }

    [Fact]
    public void SaveAndLoadData_RoundTrips()
    {
        var store = new FileKeepersDataStore(_tempDir);
        var data = new KeepersData(
            "test.xlsx",
            "2026 Keepers",
            DateTimeOffset.UtcNow,
            new Dictionary<string, StoredTeamKeepers>
            {
                ["b-squared"] = new StoredTeamKeepers(
                    "B Squared",
                    7,
                    new List<int> { 8, 9 },
                    new List<KeeperRow> { new("T. Story", 1, 14, 2) },
                    new List<ExistingContractRow> { new("J. Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m) })
            });

        store.SaveData(data);
        var loaded = store.LoadData();

        Assert.NotNull(loaded);
        Assert.Equal("test.xlsx", loaded!.SourceFileName);
        Assert.Equal("T. Story", loaded.Teams["b-squared"].NewContracts[0].Player);
    }

    [Fact]
    public void LoadWorkbook_WhenFileMissing_ReturnsNull()
    {
        var store = new FileKeepersDataStore(_tempDir);
        Assert.Null(store.LoadWorkbook());
    }

    [Fact]
    public void SaveAndLoadWorkbook_RoundTrips()
    {
        var store = new FileKeepersDataStore(_tempDir);
        var bytes = new byte[] { 1, 2, 3, 4 };

        store.SaveWorkbook(bytes);
        var loaded = store.LoadWorkbook();

        Assert.Equal(bytes, loaded);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter FileKeepersDataStoreTests`
Expected: build error — `FileKeepersDataStore`, `KeepersData`, `StoredTeamKeepers` do not exist yet.

- [ ] **Step 3: Create the models**

Create `backend/FantasyKeeper.Api/Models/KeepersData.cs`:

```csharp
namespace FantasyKeeper.Api.Models;

public record StoredTeamKeepers(
    string RawNameInSheet,
    int HeaderRow,
    IReadOnlyList<int> NewContractsRows,
    IReadOnlyList<KeeperRow> NewContracts,
    IReadOnlyList<ExistingContractRow> ExistingContracts);

public record KeepersData(
    string SourceFileName,
    string SheetName,
    DateTimeOffset LastUpdatedUtc,
    IReadOnlyDictionary<string, StoredTeamKeepers> Teams);
```

- [ ] **Step 4: Create the store interface and file-backed implementation**

Create `backend/FantasyKeeper.Api/Services/IKeepersDataStore.cs`:

```csharp
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public interface IKeepersDataStore
{
    KeepersData? LoadData();
    void SaveData(KeepersData data);
    void SaveWorkbook(byte[] bytes);
    byte[]? LoadWorkbook();
}
```

Create `backend/FantasyKeeper.Api/Services/FileKeepersDataStore.cs`:

```csharp
using System.Text.Json;
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class FileKeepersDataStore : IKeepersDataStore
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileKeepersDataStore(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string DataPath => Path.Combine(_dataRoot, "current-keepers.json");
    private string WorkbookPath => Path.Combine(_dataRoot, "current-keepers.xlsx");

    public KeepersData? LoadData()
    {
        if (!File.Exists(DataPath)) return null;
        return JsonSerializer.Deserialize<KeepersData>(File.ReadAllText(DataPath), JsonOptions);
    }

    public void SaveData(KeepersData data)
    {
        Directory.CreateDirectory(_dataRoot);
        File.WriteAllText(DataPath, JsonSerializer.Serialize(data, JsonOptions));
    }

    public void SaveWorkbook(byte[] bytes)
    {
        Directory.CreateDirectory(_dataRoot);
        File.WriteAllBytes(WorkbookPath, bytes);
    }

    public byte[]? LoadWorkbook() => File.Exists(WorkbookPath) ? File.ReadAllBytes(WorkbookPath) : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter FileKeepersDataStoreTests`
Expected: 4 tests PASS.

- [ ] **Step 6: Add the test fake**

Create `backend/FantasyKeeper.Api.Tests/Fakes/FakeKeepersDataStore.cs`:

```csharp
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeKeepersDataStore : IKeepersDataStore
{
    public KeepersData? Data { get; set; }
    public byte[]? Workbook { get; set; }

    public KeepersData? LoadData() => Data;
    public void SaveData(KeepersData data) => Data = data;
    public void SaveWorkbook(byte[] bytes) => Workbook = bytes;
    public byte[]? LoadWorkbook() => Workbook;
}
```

- [ ] **Step 7: Wire the store into DI**

In `backend/FantasyKeeper.Api/Program.cs`, add this block immediately after the existing `IConfigStore` registration (right before the `GoogleCredential` registration block):

```csharp
builder.Services.AddSingleton<IKeepersDataStore>(sp =>
{
    var dataRoot = Path.GetFullPath(sp.GetRequiredService<IConfiguration>()["DataRoot"] ?? "data");
    Directory.CreateDirectory(dataRoot);
    return new FileKeepersDataStore(dataRoot);
});
```

- [ ] **Step 8: Add the `DataRoot` default to appsettings**

In `backend/FantasyKeeper.Api/appsettings.json`, add a `DataRoot` entry alongside `ConfigRoot`:

```json
{
  "ConfigRoot": "../../config",
  "DataRoot": "../../data",
  ...
```

- [ ] **Step 9: Ignore the runtime data directory**

In `fantasy-keeper-app/.gitignore`, add a new line:

```
data/
```

- [ ] **Step 10: Build and run the full backend test suite**

Run: `dotnet build backend/FantasyKeeper.Api.sln` then `dotnet test backend/FantasyKeeper.Api.Tests`
Expected: builds clean, all existing tests still PASS (nothing else references the new store yet).

- [ ] **Step 11: Commit**

```bash
git add backend/FantasyKeeper.Api/Models/KeepersData.cs backend/FantasyKeeper.Api/Services/IKeepersDataStore.cs backend/FantasyKeeper.Api/Services/FileKeepersDataStore.cs backend/FantasyKeeper.Api.Tests/Fakes/FakeKeepersDataStore.cs backend/FantasyKeeper.Api.Tests/FileKeepersDataStoreTests.cs backend/FantasyKeeper.Api/Program.cs backend/FantasyKeeper.Api/appsettings.json .gitignore
git commit -m "Add file-backed keepers data store"
```

---

## Task 2: xlsx parser

**Files:**
- Modify: `backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj` (add ClosedXML)
- Create: `backend/FantasyKeeper.Api/Models/ImportModels.cs`
- Create: `backend/FantasyKeeper.Api/Models/KeeperExceptions.cs` addition (see below — edits existing file)
- Create: `backend/FantasyKeeper.Api/Services/KeeperWorkbookParser.cs`
- Create: `backend/FantasyKeeper.Api.Tests/KeeperWorkbookParserTests.cs`

**Interfaces:**
- Consumes: `StoredTeamKeepers`, `KeeperRow`, `ExistingContractRow` (from Task 1 / existing models).
- Produces: `ParsedWorkbook(string SheetName, IReadOnlyList<StoredTeamKeepers> Teams)`, `InvalidWorkbookException(string message)`, `KeeperWorkbookParser.Parse(Stream xlsxStream): ParsedWorkbook`.

- [ ] **Step 1: Add the ClosedXML package**

Run: `dotnet add backend/FantasyKeeper.Api package ClosedXML --version 0.104.1`
Expected: `FantasyKeeper.Api.csproj` gains a `<PackageReference Include="ClosedXML" ...>` line alongside the existing Google packages (those stay for now — removed in Task 8).

- [ ] **Step 2: Write the failing parser tests**

Create `backend/FantasyKeeper.Api.Tests/KeeperWorkbookParserTests.cs`:

```csharp
using System;
using System.IO;
using ClosedXML.Excel;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeeperWorkbookParserTests
{
    private static byte[] BuildWorkbook(Action<IXLWorksheet> populate)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("2026 Keepers");
        populate(sheet);
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteTeamBlock(IXLWorksheet sheet, int teamNameRow, string teamName)
    {
        sheet.Cell(teamNameRow, "A").Value = teamName;
        var headerRow = teamNameRow + 1;
        sheet.Cell(headerRow, "C").Value = "Player";
        sheet.Cell(headerRow, "D").Value = "Contract 1 or 2?";
        sheet.Cell(headerRow, "E").Value = "This Year's Salary";
        sheet.Cell(headerRow, "F").Value = "Keeper Years Assigned";
        sheet.Cell(headerRow, "G").Value = "Existing Contracts";
        sheet.Cell(headerRow, "H").Value = "Player";
        sheet.Cell(headerRow, "I").Value = "Contract# - yr/length";
        sheet.Cell(headerRow, "J").Value = "Last year's salary";
        sheet.Cell(headerRow, "L").Value = "League value";
        sheet.Cell(headerRow, "M").Value = "This year's Salary";

        sheet.Cell(headerRow + 1, "C").Value = "T. Story";
        sheet.Cell(headerRow + 1, "D").Value = 1;
        sheet.Cell(headerRow + 1, "E").Value = 14;
        sheet.Cell(headerRow + 1, "F").Value = 2;
        sheet.Cell(headerRow + 1, "H").Value = "Jasson Dominguez";
        sheet.Cell(headerRow + 1, "I").Value = "#1 - 2/3";
        sheet.Cell(headerRow + 1, "J").Value = 3;
        sheet.Cell(headerRow + 1, "L").Value = 1.34;
        sheet.Cell(headerRow + 1, "M").Value = 1.34;
        // headerRow + 2 is left blank on purpose — a slot with no data.
    }

    [Fact]
    public void Parse_SingleTeam_ExtractsBlock()
    {
        var bytes = BuildWorkbook(sheet => WriteTeamBlock(sheet, teamNameRow: 6, teamName: "B Squared"));

        using var ms = new MemoryStream(bytes);
        var parsed = KeeperWorkbookParser.Parse(ms);

        Assert.Equal("2026 Keepers", parsed.SheetName);
        var team = Assert.Single(parsed.Teams);
        Assert.Equal("B Squared", team.RawNameInSheet);
        Assert.Equal(7, team.HeaderRow);
        Assert.Equal("T. Story", team.NewContracts[0].Player);
        Assert.Equal(1, team.NewContracts[0].ContractType);
        Assert.Equal(14, team.NewContracts[0].Salary);
        Assert.Equal(2, team.NewContracts[0].KeeperYears);
        Assert.Equal("Jasson Dominguez", team.ExistingContracts[0].Player);
        Assert.Equal("#1 - 2/3", team.ExistingContracts[0].ContractInfo);
    }

    [Fact]
    public void Parse_SingleTeam_NewContractsRowsMatchNewContractsCount()
    {
        var bytes = BuildWorkbook(sheet => WriteTeamBlock(sheet, teamNameRow: 6, teamName: "B Squared"));

        using var ms = new MemoryStream(bytes);
        var parsed = KeeperWorkbookParser.Parse(ms);

        var team = parsed.Teams[0];
        Assert.Equal(team.NewContractsRows.Count, team.NewContracts.Count);
        Assert.Contains(8, team.NewContractsRows);
    }

    [Fact]
    public void Parse_TwoTeams_SplitsAtNextAnchor()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            WriteTeamBlock(sheet, teamNameRow: 6, teamName: "B Squared");
            WriteTeamBlock(sheet, teamNameRow: 20, teamName: "BA Bombers");
        });

        using var ms = new MemoryStream(bytes);
        var parsed = KeeperWorkbookParser.Parse(ms);

        Assert.Equal(2, parsed.Teams.Count);
        Assert.Equal("B Squared", parsed.Teams[0].RawNameInSheet);
        Assert.Equal("BA Bombers", parsed.Teams[1].RawNameInSheet);
        Assert.All(parsed.Teams[0].NewContractsRows, row => Assert.True(row < parsed.Teams[1].HeaderRow));
    }

    [Fact]
    public void Parse_NoHeaderAnchorFound_ThrowsInvalidWorkbook()
    {
        var bytes = BuildWorkbook(sheet => sheet.Cell(1, "A").Value = "Not a keepers sheet");

        using var ms = new MemoryStream(bytes);
        Assert.Throws<InvalidWorkbookException>(() => KeeperWorkbookParser.Parse(ms));
    }

    [Fact]
    public void Parse_CorruptFile_ThrowsInvalidWorkbook()
    {
        using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        Assert.Throws<InvalidWorkbookException>(() => KeeperWorkbookParser.Parse(ms));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail to compile**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperWorkbookParserTests`
Expected: build error — `KeeperWorkbookParser`, `InvalidWorkbookException`, `ParsedWorkbook` do not exist yet.

- [ ] **Step 4: Add `InvalidWorkbookException`**

In `backend/FantasyKeeper.Api/Models/KeeperExceptions.cs`, add this class alongside the existing ones:

```csharp
public class InvalidWorkbookException : Exception
{
    public InvalidWorkbookException(string message) : base(message) { }
}
```

- [ ] **Step 5: Add `ParsedWorkbook`**

Create `backend/FantasyKeeper.Api/Models/ImportModels.cs`:

```csharp
namespace FantasyKeeper.Api.Models;

public record ParsedWorkbook(string SheetName, IReadOnlyList<StoredTeamKeepers> Teams);
```

- [ ] **Step 6: Implement the parser**

Create `backend/FantasyKeeper.Api/Services/KeeperWorkbookParser.cs`:

```csharp
using System.Globalization;
using ClosedXML.Excel;
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public static class KeeperWorkbookParser
{
    public static ParsedWorkbook Parse(Stream xlsxStream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(xlsxStream);
        }
        catch (Exception)
        {
            throw new InvalidWorkbookException("That file couldn't be read as an xlsx workbook.");
        }

        using (workbook)
        {
            foreach (var worksheet in workbook.Worksheets)
            {
                var anchorRows = FindAnchorRows(worksheet);
                if (anchorRows.Count == 0) continue;

                var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? anchorRows[^1];
                var teams = new List<StoredTeamKeepers>();

                for (var i = 0; i < anchorRows.Count; i++)
                {
                    var headerRow = anchorRows[i];
                    var teamNameRow = headerRow - 1;
                    var rawName = worksheet.Cell(teamNameRow, "A").GetString().Trim();

                    var startDataRow = headerRow + 1;
                    var endDataRow = i + 1 < anchorRows.Count ? anchorRows[i + 1] - 2 : lastRow;

                    var newContractsRows = new List<int>();
                    var newContracts = new List<KeeperRow>();
                    var existingContracts = new List<ExistingContractRow>();

                    for (var row = startDataRow; row <= endDataRow; row++)
                    {
                        newContractsRows.Add(row);
                        newContracts.Add(new KeeperRow(
                            worksheet.Cell(row, "C").GetString().Trim(),
                            ParseInt(worksheet.Cell(row, "D")),
                            ParseDecimal(worksheet.Cell(row, "E")),
                            ParseInt(worksheet.Cell(row, "F"))));

                        var existingPlayer = worksheet.Cell(row, "H").GetString().Trim();
                        if (existingPlayer.Length > 0)
                        {
                            existingContracts.Add(new ExistingContractRow(
                                existingPlayer,
                                worksheet.Cell(row, "I").GetString().Trim(),
                                ParseDecimal(worksheet.Cell(row, "J")),
                                ParseDecimal(worksheet.Cell(row, "L")),
                                ParseDecimal(worksheet.Cell(row, "M"))));
                        }
                    }

                    teams.Add(new StoredTeamKeepers(rawName, headerRow, newContractsRows, newContracts, existingContracts));
                }

                return new ParsedWorkbook(worksheet.Name, teams);
            }
        }

        throw new InvalidWorkbookException("Couldn't find a keepers table in this file.");
    }

    private static List<int> FindAnchorRows(IXLWorksheet worksheet)
    {
        var anchors = new List<int>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var row = 1; row <= lastRow; row++)
        {
            var c = worksheet.Cell(row, "C").GetString().Trim();
            var d = worksheet.Cell(row, "D").GetString().Trim();
            var g = worksheet.Cell(row, "G").GetString().Trim();
            if (c == "Player"
                && d.StartsWith("Contract", StringComparison.OrdinalIgnoreCase)
                && g.Contains("Existing", StringComparison.OrdinalIgnoreCase))
            {
                anchors.Add(row);
            }
        }
        return anchors;
    }

    private static int? ParseInt(IXLCell cell)
    {
        var text = cell.GetString().Trim();
        if (int.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return value;
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec) ? (int)dec : null;
    }

    private static decimal? ParseDecimal(IXLCell cell)
    {
        var text = cell.GetString().Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperWorkbookParserTests`
Expected: 5 tests PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj backend/FantasyKeeper.Api/Models/ImportModels.cs backend/FantasyKeeper.Api/Models/KeeperExceptions.cs backend/FantasyKeeper.Api/Services/KeeperWorkbookParser.cs backend/FantasyKeeper.Api.Tests/KeeperWorkbookParserTests.cs
git commit -m "Add header-anchor xlsx keepers parser"
```

---

## Task 3: xlsx writer

**Files:**
- Create: `backend/FantasyKeeper.Api/Services/KeeperWorkbookWriter.cs`
- Create: `backend/FantasyKeeper.Api.Tests/KeeperWorkbookWriterTests.cs`

**Interfaces:**
- Consumes: `StoredTeamKeepers`, `KeeperRow` (existing/Task 1 models).
- Produces: `KeeperWorkbookWriter.WriteNewContracts(byte[] originalWorkbookBytes, string sheetName, IReadOnlyDictionary<string, StoredTeamKeepers> teams): byte[]`.

- [ ] **Step 1: Write the failing writer tests**

Create `backend/FantasyKeeper.Api.Tests/KeeperWorkbookWriterTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeeperWorkbookWriterTests
{
    private static byte[] BuildWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("2026 Keepers");
        sheet.Cell(6, "A").Value = "B Squared";
        sheet.Cell(7, "C").Value = "Player";
        sheet.Cell(8, "C").Value = "Old Player";
        sheet.Cell(8, "D").Value = 1;
        sheet.Cell(8, "E").Value = 5;
        sheet.Cell(8, "F").Value = 1;
        sheet.Cell(8, "A").Value = "keep-me";
        sheet.Cell(1, "A").Value = "Link to Projection / CBS Salary Cap Values";
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public void WriteNewContracts_UpdatesOnlyMappedCells()
    {
        var original = BuildWorkbook();
        var teams = new Dictionary<string, StoredTeamKeepers>
        {
            ["b-squared"] = new StoredTeamKeepers(
                "B Squared",
                7,
                new List<int> { 8 },
                new List<KeeperRow> { new("New Player", 2, 10, 3) },
                new List<ExistingContractRow>())
        };

        var result = KeeperWorkbookWriter.WriteNewContracts(original, "2026 Keepers", teams);

        using var ms = new MemoryStream(result);
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheet("2026 Keepers");

        Assert.Equal("New Player", sheet.Cell(8, "C").GetString());
        Assert.Equal("2", sheet.Cell(8, "D").GetString());
        Assert.Equal("10", sheet.Cell(8, "E").GetString());
        Assert.Equal("3", sheet.Cell(8, "F").GetString());
        Assert.Equal("keep-me", sheet.Cell(8, "A").GetString());
        Assert.Equal("Link to Projection / CBS Salary Cap Values", sheet.Cell(1, "A").GetString());
    }

    [Fact]
    public void WriteNewContracts_BlankRow_ClearsCells()
    {
        var original = BuildWorkbook();
        var teams = new Dictionary<string, StoredTeamKeepers>
        {
            ["b-squared"] = new StoredTeamKeepers(
                "B Squared",
                7,
                new List<int> { 8 },
                new List<KeeperRow> { new("", null, null, null) },
                new List<ExistingContractRow>())
        };

        var result = KeeperWorkbookWriter.WriteNewContracts(original, "2026 Keepers", teams);

        using var ms = new MemoryStream(result);
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheet("2026 Keepers");

        Assert.Equal("", sheet.Cell(8, "C").GetString());
        Assert.Equal("", sheet.Cell(8, "D").GetString());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperWorkbookWriterTests`
Expected: build error — `KeeperWorkbookWriter` does not exist yet.

- [ ] **Step 3: Implement the writer**

Create `backend/FantasyKeeper.Api/Services/KeeperWorkbookWriter.cs`:

```csharp
using ClosedXML.Excel;
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public static class KeeperWorkbookWriter
{
    public static byte[] WriteNewContracts(byte[] originalWorkbookBytes, string sheetName, IReadOnlyDictionary<string, StoredTeamKeepers> teams)
    {
        using var input = new MemoryStream(originalWorkbookBytes);
        using var workbook = new XLWorkbook(input);
        var worksheet = workbook.Worksheet(sheetName);

        foreach (var team in teams.Values)
        {
            for (var i = 0; i < team.NewContractsRows.Count; i++)
            {
                var row = team.NewContractsRows[i];
                var contract = i < team.NewContracts.Count ? team.NewContracts[i] : new KeeperRow("", null, null, null);

                SetText(worksheet.Cell(row, "C"), contract.Player);
                SetNumber(worksheet.Cell(row, "D"), contract.ContractType);
                SetNumber(worksheet.Cell(row, "E"), contract.Salary);
                SetNumber(worksheet.Cell(row, "F"), contract.KeeperYears);
            }
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static void SetText(IXLCell cell, string? value)
    {
        if (string.IsNullOrEmpty(value)) cell.Clear(XLClearOptions.Contents);
        else cell.SetValue(value);
    }

    private static void SetNumber(IXLCell cell, decimal? value)
    {
        if (value.HasValue) cell.SetValue(value.Value);
        else cell.Clear(XLClearOptions.Contents);
    }

    private static void SetNumber(IXLCell cell, int? value)
    {
        if (value.HasValue) cell.SetValue(value.Value);
        else cell.Clear(XLClearOptions.Contents);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperWorkbookWriterTests`
Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyKeeper.Api/Services/KeeperWorkbookWriter.cs backend/FantasyKeeper.Api.Tests/KeeperWorkbookWriterTests.cs
git commit -m "Add xlsx keepers writer that preserves untouched cells"
```

---

## Task 4: KeepersService rewrite

**Files:**
- Modify: `backend/FantasyKeeper.Api/Models/KeeperTeamData.cs`
- Modify: `backend/FantasyKeeper.Api/Services/KeepersService.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs`

**Interfaces:**
- Consumes: `IKeepersDataStore`, `StoredTeamKeepers`, `KeepersData` (Task 1); `IConfigStore.GetTeams()`, `Team` (existing, unchanged in this task).
- Produces: `KeeperTeamData(string TeamName, IReadOnlyList<ExistingContractRow> ExistingContracts, IReadOnlyList<KeeperRow> NewContracts)`, `KeepersService(IKeepersDataStore store, IConfigStore configStore)` with `GetKeeperData(string teamId): KeeperTeamData` and `UpdateKeeperData(string teamId, KeeperSubmission submission): KeeperTeamData` (both synchronous, no `Async` suffix).

- [ ] **Step 1: Replace `KeepersServiceTests.cs` with the new-shape failing tests**

Replace the full contents of `backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeepersServiceTests
{
    private static (FakeConfigStore Config, FakeKeepersDataStore Store, KeepersService Service) Build()
    {
        var config = new FakeConfigStore
        {
            Teams = new List<Team> { new("b-squared", "B Squared", "1111") }
        };

        var store = new FakeKeepersDataStore
        {
            Data = new KeepersData(
                "test.xlsx",
                "2026 Keepers",
                DateTimeOffset.UtcNow,
                new Dictionary<string, StoredTeamKeepers>
                {
                    ["b-squared"] = new StoredTeamKeepers(
                        "B Squared",
                        7,
                        new List<int> { 8, 9 },
                        new List<KeeperRow>
                        {
                            new("T. Story", 1, 14, 2),
                            new("", null, null, null)
                        },
                        new List<ExistingContractRow>
                        {
                            new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                        })
                })
        };

        return (config, store, new KeepersService(store, config));
    }

    [Fact]
    public void GetKeeperData_ReturnsStoredRows()
    {
        var (_, _, service) = Build();

        var data = service.GetKeeperData("b-squared");

        Assert.Equal("B Squared", data.TeamName);
        Assert.Equal("T. Story", data.NewContracts[0].Player);
        Assert.Equal(1, data.NewContracts[0].ContractType);
        Assert.Equal(14, data.NewContracts[0].Salary);
        Assert.Equal("Jasson Dominguez", data.ExistingContracts[0].Player);
    }

    [Fact]
    public void GetKeeperData_NoDataImported_Throws()
    {
        var config = new FakeConfigStore { Teams = new List<Team> { new("b-squared", "B Squared", "1111") } };
        var store = new FakeKeepersDataStore();
        var service = new KeepersService(store, config);

        Assert.Throws<NotFoundException>(() => service.GetKeeperData("b-squared"));
    }

    [Fact]
    public void UpdateKeeperData_ValidSubmission_SavesAndReturnsUpdatedRows()
    {
        var (_, store, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        var result = service.UpdateKeeperData("b-squared", submission);

        Assert.Equal("New Guy", result.NewContracts[0].Player);
        Assert.Equal("New Guy", store.Data!.Teams["b-squared"].NewContracts[0].Player);
    }

    [Fact]
    public void UpdateKeeperData_BumpsLastUpdatedUtc()
    {
        var (_, store, service) = Build();
        var before = store.Data!.LastUpdatedUtc;
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        service.UpdateKeeperData("b-squared", submission);

        Assert.True(store.Data!.LastUpdatedUtc > before);
    }

    [Fact]
    public void UpdateKeeperData_InvalidContractType_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 3, 10, 2),
            new("", null, null, null)
        });

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Fact]
    public void UpdateKeeperData_WrongRowCount_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow> { new("New Guy", 1, 10, 2) });

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Theory]
    [InlineData("=ARRAYFORMULA(A1:A10)")]
    [InlineData("+1+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    public void UpdateKeeperData_PlayerNameStartsWithFormulaChar_Throws(string playerName)
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new(playerName, 1, 10, 2),
            new("", null, null, null)
        });

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Fact]
    public void UpdateKeeperData_UnknownTeam_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>());

        Assert.Throws<NotFoundException>(() => service.UpdateKeeperData("nobody", submission));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeepersServiceTests`
Expected: build error — `KeepersService` constructor and method shapes don't match yet.

- [ ] **Step 3: Trim `KeeperTeamData`**

Replace the contents of `backend/FantasyKeeper.Api/Models/KeeperTeamData.cs`:

```csharp
namespace FantasyKeeper.Api.Models;

public record KeeperTeamData(string TeamName, IReadOnlyList<ExistingContractRow> ExistingContracts, IReadOnlyList<KeeperRow> NewContracts);
```

- [ ] **Step 4: Rewrite `KeepersService`**

Replace the contents of `backend/FantasyKeeper.Api/Services/KeepersService.cs`:

```csharp
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class KeepersService
{
    private readonly IKeepersDataStore _store;
    private readonly IConfigStore _configStore;

    public KeepersService(IKeepersDataStore store, IConfigStore configStore)
    {
        _store = store;
        _configStore = configStore;
    }

    public KeeperTeamData GetKeeperData(string teamId)
    {
        var team = FindTeam(teamId);
        var stored = FindStoredTeam(teamId);
        return new KeeperTeamData(team.Name, stored.ExistingContracts, stored.NewContracts);
    }

    public KeeperTeamData UpdateKeeperData(string teamId, KeeperSubmission submission)
    {
        var team = FindTeam(teamId);
        var data = _store.LoadData() ?? throw new NotFoundException("No keeper data has been imported yet.");
        if (!data.Teams.TryGetValue(teamId, out var stored))
        {
            throw new NotFoundException($"No keeper data found for team '{teamId}'.");
        }

        var errors = ValidateSubmission(submission, stored.NewContractsRows.Count);
        if (errors.Count > 0)
        {
            throw new KeeperValidationException(errors);
        }

        var updatedStored = stored with { NewContracts = submission.NewContracts };
        var updatedTeams = new Dictionary<string, StoredTeamKeepers>(data.Teams) { [teamId] = updatedStored };
        var updatedData = data with { Teams = updatedTeams, LastUpdatedUtc = DateTimeOffset.UtcNow };
        _store.SaveData(updatedData);

        return new KeeperTeamData(team.Name, updatedStored.ExistingContracts, updatedStored.NewContracts);
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
            else if (row.Player.TrimStart()[0] is '=' or '+' or '-' or '@')
            {
                errors.Add($"Row {i + 1}: player name cannot start with '=', '+', '-', or '@'.");
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

    private Team FindTeam(string teamId)
    {
        var team = _configStore.GetTeams().FirstOrDefault(t => t.TeamId == teamId);
        if (team is null) throw new NotFoundException($"Team '{teamId}' not found.");
        return team;
    }

    private StoredTeamKeepers FindStoredTeam(string teamId)
    {
        var data = _store.LoadData() ?? throw new NotFoundException("No keeper data has been imported yet.");
        if (!data.Teams.TryGetValue(teamId, out var stored))
        {
            throw new NotFoundException($"No keeper data found for team '{teamId}'.");
        }
        return stored;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeepersServiceTests`
Expected: 8 tests PASS.

- [ ] **Step 6: Commit**

Note: `backend/FantasyKeeper.Api.sln` will not build as a whole after this
commit — `Endpoints/KeeperEndpoints.cs` still calls the old
`GetKeeperDataAsync`/`UpdateKeeperDataAsync(seasonId, ...)` signatures.
That's expected and fixed immediately in Task 5; this task's own tests
(filtered to `KeepersServiceTests`, run in Step 5) are what verify its
deliverable.

```bash
git add backend/FantasyKeeper.Api/Models/KeeperTeamData.cs backend/FantasyKeeper.Api/Services/KeepersService.cs backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs
git commit -m "Rewrite KeepersService to read/write the file-backed data store"
```

---

## Task 5: KeeperEndpoints update

**Files:**
- Modify: `backend/FantasyKeeper.Api/Endpoints/KeeperEndpoints.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs`

**Interfaces:**
- Consumes: `KeepersService.GetKeeperData(string teamId)`, `KeepersService.UpdateKeeperData(string teamId, KeeperSubmission submission)` (Task 4); `AuthService.ResolvePin`, `AuthResult.TeamId` (existing, unchanged in this task — `AuthResult.SeasonId` still exists but is no longer read here).
- Produces: `GET /api/keepers?pin=...` and `PUT /api/keepers?pin=...` with no `seasonId` parameter.

- [ ] **Step 1: Replace `KeeperEndpointsTests.cs` with the new-shape failing tests**

Replace the full contents of `backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeeperEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _configRoot;
    private readonly string _dataRoot;

    public KeeperEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _dataRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_configRoot);
        Directory.CreateDirectory(_dataRoot);

        var configStore = new JsonConfigStore(_configRoot);
        // AuthService isn't updated until Task 8 and still requires an
        // active season to authenticate a team PIN — seeded here purely to
        // satisfy that; unrelated to the new keepers data path below.
        configStore.SaveSeasons(new List<Season>
        {
            new("season-1", "2026", "dev-sheet-2026", "active", DateTimeOffset.UtcNow)
        });
        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"}]""");

        var dataStore = new FileKeepersDataStore(_dataRoot);
        dataStore.SaveData(new KeepersData(
            "test.xlsx",
            "2026 Keepers",
            DateTimeOffset.UtcNow,
            new Dictionary<string, StoredTeamKeepers>
            {
                ["b-squared"] = new StoredTeamKeepers(
                    "B Squared",
                    7,
                    new List<int> { 8, 9, 10, 11, 12, 13 },
                    new List<KeeperRow>
                    {
                        new("T. Story", 1, 14, 2),
                        new("", null, null, null),
                        new("", null, null, null),
                        new("", null, null, null),
                        new("", null, null, null),
                        new("", null, null, null)
                    },
                    new List<ExistingContractRow>
                    {
                        new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                    })
            }));

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConfigRoot"] = _configRoot,
                    ["DataRoot"] = _dataRoot,
                    ["Google:UseDevClients"] = "true",
                    ["AdminPin"] = "9999"
                });
            });
        });
    }

    public void Dispose()
    {
        Directory.Delete(_configRoot, recursive: true);
        Directory.Delete(_dataRoot, recursive: true);
    }

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

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_ValidSubmission_PersistsAndReturnsUpdatedData()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(Enumerable.Range(0, 6)
            .Select(i => i == 0 ? new KeeperRow("New Guy", 1, 10, 2) : new KeeperRow("", null, null, null))
            .ToList());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111", payload);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("New Guy", data!.NewContracts[0].Player);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperEndpointsTests`
Expected: FAIL/build error — endpoint still expects `seasonId` and calls the old async methods.

- [ ] **Step 3: Rewrite `KeeperEndpoints.cs`**

Replace the full contents of `backend/FantasyKeeper.Api/Endpoints/KeeperEndpoints.cs`:

```csharp
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class KeeperEndpoints
{
    public static void MapKeeperEndpoints(this WebApplication app)
    {
        app.MapGet("/api/keepers", (string pin, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Owner || auth.TeamId is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(keepersService.GetKeeperData(auth.TeamId));
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPut("/api/keepers", (string pin, KeeperSubmission submission, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Owner || auth.TeamId is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(keepersService.UpdateKeeperData(auth.TeamId, submission));
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

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperEndpointsTests`
Expected: 4 tests PASS.

- [ ] **Step 5: Build and run the full backend test suite**

Run: `dotnet build backend/FantasyKeeper.Api.sln` then `dotnet test backend/FantasyKeeper.Api.Tests`
Expected: builds clean, all tests PASS (the `SeasonEndpoints`/`SeasonService` path is untouched and still works — it's deleted in Task 8).

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyKeeper.Api/Endpoints/KeeperEndpoints.cs backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs
git commit -m "Drop seasonId from the keepers endpoints"
```

---

## Task 6: Import service

**Files:**
- Modify: `backend/FantasyKeeper.Api/Models/ImportModels.cs`
- Create: `backend/FantasyKeeper.Api/Services/KeepersImportService.cs`
- Create: `backend/FantasyKeeper.Api.Tests/KeepersImportServiceTests.cs`

**Interfaces:**
- Consumes: `KeeperWorkbookParser.Parse` (Task 2), `KeeperWorkbookWriter.WriteNewContracts` (Task 3), `IKeepersDataStore`, `KeepersData` (Task 1), `IConfigStore.GetTeams()`, `Team`.
- Produces: `ImportBlockPreview(int BlockIndex, string RawNameInSheet, string? SuggestedTeamId)`, `ImportPreview(string FileName, IReadOnlyList<ImportBlockPreview> Blocks)`, `BlockAssignment(int BlockIndex, string? TeamId)`, `ConfirmImportRequest(IReadOnlyList<BlockAssignment> Assignments)`, `KeepersImportService(IKeepersDataStore store, IConfigStore configStore)` with `StartImport(byte[] fileBytes, string fileName): ImportPreview`, `ConfirmImport(IReadOnlyList<BlockAssignment> assignments): KeepersData`, `Export(): byte[]`.

- [ ] **Step 1: Write the failing import service tests**

Create `backend/FantasyKeeper.Api.Tests/KeepersImportServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeepersImportServiceTests
{
    private static byte[] BuildWorkbook(params string[] teamNames)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("2026 Keepers");
        var row = 6;
        foreach (var name in teamNames)
        {
            sheet.Cell(row, "A").Value = name;
            var headerRow = row + 1;
            sheet.Cell(headerRow, "C").Value = "Player";
            sheet.Cell(headerRow, "D").Value = "Contract 1 or 2?";
            sheet.Cell(headerRow, "G").Value = "Existing Contracts";
            sheet.Cell(headerRow + 1, "C").Value = "Some Player";
            sheet.Cell(headerRow + 1, "D").Value = 1;
            sheet.Cell(headerRow + 1, "E").Value = 5;
            sheet.Cell(headerRow + 1, "F").Value = 1;
            row = headerRow + 5;
        }
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static (FakeConfigStore Config, FakeKeepersDataStore Store, KeepersImportService Service) Build(List<Team>? teams = null)
    {
        var config = new FakeConfigStore { Teams = teams ?? new List<Team> { new("b-squared", "B Squared", "1111") } };
        var store = new FakeKeepersDataStore();
        return (config, store, new KeepersImportService(store, config));
    }

    [Fact]
    public void StartImport_KnownTeamName_SuggestsMatch()
    {
        var (_, _, service) = Build();
        var bytes = BuildWorkbook("B Squared");

        var preview = service.StartImport(bytes, "keepers.xlsx");

        var block = Assert.Single(preview.Blocks);
        Assert.Equal("B Squared", block.RawNameInSheet);
        Assert.Equal("b-squared", block.SuggestedTeamId);
    }

    [Fact]
    public void StartImport_UnknownTeamName_SuggestsNull()
    {
        var (_, _, service) = Build();
        var bytes = BuildWorkbook("Some Random Name");

        var preview = service.StartImport(bytes, "keepers.xlsx");

        Assert.Null(Assert.Single(preview.Blocks).SuggestedTeamId);
    }

    [Fact]
    public void StartImport_InvalidFile_Throws()
    {
        var (_, _, service) = Build();

        Assert.Throws<InvalidWorkbookException>(() => service.StartImport(new byte[] { 1, 2, 3 }, "bad.xlsx"));
    }

    [Fact]
    public void ConfirmImport_WithoutPendingImport_Throws()
    {
        var (_, _, service) = Build();

        Assert.Throws<InvalidWorkbookException>(() => service.ConfirmImport(new List<BlockAssignment>()));
    }

    [Fact]
    public void ConfirmImport_ValidAssignment_SavesDataAndWorkbook()
    {
        var (_, store, service) = Build();
        var bytes = BuildWorkbook("B Squared");
        service.StartImport(bytes, "keepers.xlsx");

        var data = service.ConfirmImport(new List<BlockAssignment> { new(0, "b-squared") });

        Assert.Equal("keepers.xlsx", data.SourceFileName);
        Assert.True(data.Teams.ContainsKey("b-squared"));
        Assert.NotNull(store.Data);
        Assert.NotNull(store.Workbook);
    }

    [Fact]
    public void ConfirmImport_SkippedBlock_IsExcluded()
    {
        var (_, _, service) = Build();
        var bytes = BuildWorkbook("B Squared");
        service.StartImport(bytes, "keepers.xlsx");

        var data = service.ConfirmImport(new List<BlockAssignment> { new(0, null) });

        Assert.Empty(data.Teams);
    }

    [Fact]
    public void ConfirmImport_DuplicateTeamAssignment_Throws()
    {
        var (_, _, service) = Build(new List<Team>
        {
            new("b-squared", "B Squared", "1111"),
            new("other", "Other Team", "2222")
        });
        var bytes = BuildWorkbook("B Squared", "Other Team");
        service.StartImport(bytes, "keepers.xlsx");

        Assert.Throws<KeeperValidationException>(() =>
            service.ConfirmImport(new List<BlockAssignment> { new(0, "b-squared"), new(1, "b-squared") }));
    }

    [Fact]
    public void ConfirmImport_UnresolvedBlockCount_Throws()
    {
        var (_, _, service) = Build();
        var bytes = BuildWorkbook("B Squared");
        service.StartImport(bytes, "keepers.xlsx");

        Assert.Throws<KeeperValidationException>(() => service.ConfirmImport(new List<BlockAssignment>()));
    }

    [Fact]
    public void Export_NoDataImported_Throws()
    {
        var (_, _, service) = Build();

        Assert.Throws<NotFoundException>(() => service.Export());
    }

    [Fact]
    public void Export_AfterConfirmedImport_ReturnsWorkbookBytes()
    {
        var (_, _, service) = Build();
        var bytes = BuildWorkbook("B Squared");
        service.StartImport(bytes, "keepers.xlsx");
        service.ConfirmImport(new List<BlockAssignment> { new(0, "b-squared") });

        var exported = service.Export();

        Assert.NotEmpty(exported);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeepersImportServiceTests`
Expected: build error — `KeepersImportService`, `BlockAssignment`, etc. do not exist yet.

- [ ] **Step 3: Add the remaining import models**

Append to `backend/FantasyKeeper.Api/Models/ImportModels.cs` (keep the existing `ParsedWorkbook` record):

```csharp
public record ImportBlockPreview(int BlockIndex, string RawNameInSheet, string? SuggestedTeamId);

public record ImportPreview(string FileName, IReadOnlyList<ImportBlockPreview> Blocks);

public record BlockAssignment(int BlockIndex, string? TeamId);

public record ConfirmImportRequest(IReadOnlyList<BlockAssignment> Assignments);
```

- [ ] **Step 4: Implement `KeepersImportService`**

Create `backend/FantasyKeeper.Api/Services/KeepersImportService.cs`:

```csharp
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class KeepersImportService
{
    private readonly IKeepersDataStore _store;
    private readonly IConfigStore _configStore;
    private readonly object _lock = new();
    private PendingImport? _pending;

    private record PendingImport(string SourceFileName, byte[] WorkbookBytes, ParsedWorkbook Parsed);

    public KeepersImportService(IKeepersDataStore store, IConfigStore configStore)
    {
        _store = store;
        _configStore = configStore;
    }

    public ImportPreview StartImport(byte[] fileBytes, string fileName)
    {
        ParsedWorkbook parsed;
        using (var ms = new MemoryStream(fileBytes))
        {
            parsed = KeeperWorkbookParser.Parse(ms);
        }

        var teams = _configStore.GetTeams();
        var blocks = new List<ImportBlockPreview>();
        for (var i = 0; i < parsed.Teams.Count; i++)
        {
            blocks.Add(new ImportBlockPreview(i, parsed.Teams[i].RawNameInSheet, SuggestTeamId(parsed.Teams[i].RawNameInSheet, teams)));
        }

        lock (_lock)
        {
            _pending = new PendingImport(fileName, fileBytes, parsed);
        }

        return new ImportPreview(fileName, blocks);
    }

    public KeepersData ConfirmImport(IReadOnlyList<BlockAssignment> assignments)
    {
        PendingImport pending;
        lock (_lock)
        {
            pending = _pending ?? throw new InvalidWorkbookException("No pending import to confirm. Upload a file first.");
        }

        if (assignments.Count != pending.Parsed.Teams.Count)
        {
            throw new KeeperValidationException(new List<string> { "Every detected team must be resolved before confirming." });
        }

        var errors = new List<string>();
        var seenBlockIndexes = new HashSet<int>();
        var seenTeamIds = new HashSet<string>();
        var teams = new Dictionary<string, StoredTeamKeepers>();
        var validTeamIds = _configStore.GetTeams().Select(t => t.TeamId).ToHashSet();

        foreach (var assignment in assignments)
        {
            if (assignment.BlockIndex < 0 || assignment.BlockIndex >= pending.Parsed.Teams.Count)
            {
                errors.Add($"Block index {assignment.BlockIndex} is not a detected team.");
                continue;
            }
            if (!seenBlockIndexes.Add(assignment.BlockIndex))
            {
                errors.Add($"Block index {assignment.BlockIndex} was assigned more than once.");
                continue;
            }
            if (assignment.TeamId is null)
            {
                continue;
            }
            if (!validTeamIds.Contains(assignment.TeamId))
            {
                errors.Add($"'{assignment.TeamId}' is not a known team.");
                continue;
            }
            if (!seenTeamIds.Add(assignment.TeamId))
            {
                errors.Add($"Team '{assignment.TeamId}' was assigned to more than one block.");
                continue;
            }
            teams[assignment.TeamId] = pending.Parsed.Teams[assignment.BlockIndex];
        }

        if (errors.Count > 0)
        {
            throw new KeeperValidationException(errors);
        }

        var data = new KeepersData(pending.SourceFileName, pending.Parsed.SheetName, DateTimeOffset.UtcNow, teams);
        _store.SaveData(data);
        _store.SaveWorkbook(pending.WorkbookBytes);

        lock (_lock)
        {
            _pending = null;
        }

        return data;
    }

    public byte[] Export()
    {
        var data = _store.LoadData() ?? throw new NotFoundException("No keeper data has been imported yet.");
        var workbookBytes = _store.LoadWorkbook() ?? throw new NotFoundException("No keeper data has been imported yet.");
        return KeeperWorkbookWriter.WriteNewContracts(workbookBytes, data.SheetName, data.Teams);
    }

    private static string? SuggestTeamId(string rawName, IReadOnlyList<Team> teams)
    {
        var normalizedRaw = Normalize(rawName);
        return teams.FirstOrDefault(t => Normalize(t.Name) == normalizedRaw)?.TeamId;
    }

    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeepersImportServiceTests`
Expected: 9 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/FantasyKeeper.Api/Models/ImportModels.cs backend/FantasyKeeper.Api/Services/KeepersImportService.cs backend/FantasyKeeper.Api.Tests/KeepersImportServiceTests.cs
git commit -m "Add KeepersImportService orchestrating parse/confirm/export"
```

---

## Task 7: AdminKeepersEndpoints

**Files:**
- Create: `backend/FantasyKeeper.Api/Endpoints/AdminKeepersEndpoints.cs`
- Create: `backend/FantasyKeeper.Api.Tests/AdminKeepersEndpointsTests.cs`
- Modify: `backend/FantasyKeeper.Api/Program.cs`

**Interfaces:**
- Consumes: `KeepersImportService` (Task 6), `IKeepersDataStore.LoadData()` (Task 1), `IConfigStore.GetTeams()`, `AuthService.ResolvePin`.
- Produces: `GET /api/admin/teams?pin=...`, `POST /api/admin/keepers/import?pin=...` (multipart, field `file`), `POST /api/admin/keepers/import/confirm?pin=...`, `GET /api/admin/keepers/export?pin=...`, `GET /api/admin/keepers/status?pin=...`.

- [ ] **Step 1: Write the failing integration tests**

Create `backend/FantasyKeeper.Api.Tests/AdminKeepersEndpointsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class AdminKeepersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _configRoot;
    private readonly string _dataRoot;

    public AdminKeepersEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _dataRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_configRoot);
        Directory.CreateDirectory(_dataRoot);

        var configStore = new JsonConfigStore(_configRoot);
        // AuthService isn't updated until Task 8 and still requires an
        // active season to authenticate a team PIN — seeded here purely to
        // satisfy that; unrelated to the new keepers data path below.
        configStore.SaveSeasons(new List<Season>
        {
            new("season-1", "2026", "dev-sheet-2026", "active", DateTimeOffset.UtcNow)
        });
        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"}]""");

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConfigRoot"] = _configRoot,
                    ["DataRoot"] = _dataRoot,
                    ["Google:UseDevClients"] = "true",
                    ["AdminPin"] = "9999"
                });
            });
        });
    }

    public void Dispose()
    {
        Directory.Delete(_configRoot, recursive: true);
        Directory.Delete(_dataRoot, recursive: true);
    }

    private static byte[] BuildWorkbook(string teamName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("2026 Keepers");
        sheet.Cell(6, "A").Value = teamName;
        sheet.Cell(7, "C").Value = "Player";
        sheet.Cell(7, "D").Value = "Contract 1 or 2?";
        sheet.Cell(7, "G").Value = "Existing Contracts";
        sheet.Cell(8, "C").Value = "Some Player";
        sheet.Cell(8, "D").Value = 1;
        sheet.Cell(8, "E").Value = 5;
        sheet.Cell(8, "F").Value = 1;
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task ImportKeepers_WithOwnerPin_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(BuildWorkbook("B Squared")), "file", "keepers.xlsx" }
        };

        var response = await client.PostAsync("/api/admin/keepers/import?pin=1111", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ImportThenConfirm_ThenGetKeepers_ReturnsImportedData()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(BuildWorkbook("B Squared")), "file", "keepers.xlsx" }
        };

        var importResponse = await client.PostAsync("/api/admin/keepers/import?pin=9999", content);
        importResponse.EnsureSuccessStatusCode();
        var preview = await importResponse.Content.ReadFromJsonAsync<ImportPreview>(ResponseJsonOptions);
        Assert.Equal("b-squared", preview!.Blocks[0].SuggestedTeamId);

        var confirmRequest = new ConfirmImportRequest(new List<BlockAssignment> { new(0, "b-squared") });
        var confirmResponse = await client.PostAsJsonAsync("/api/admin/keepers/import/confirm?pin=9999", confirmRequest);
        confirmResponse.EnsureSuccessStatusCode();

        var keepersResponse = await client.GetAsync("/api/keepers?pin=1111");
        keepersResponse.EnsureSuccessStatusCode();
        var data = await keepersResponse.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("Some Player", data!.NewContracts[0].Player);
    }

    [Fact]
    public async Task ExportKeepers_BeforeAnyImport_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/keepers/export?pin=9999");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_BeforeAnyImport_ReturnsNullTimestamp()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/keepers/status?pin=9999");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lastUpdatedUtc").ValueKind);
    }

    [Fact]
    public async Task GetAdminTeams_WithAdminPin_ReturnsTeams()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/teams?pin=9999");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("b-squared", json[0].GetProperty("teamId").GetString());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter AdminKeepersEndpointsTests`
Expected: FAIL (404s) — the endpoints and DI registrations don't exist yet.

- [ ] **Step 3: Implement `AdminKeepersEndpoints`**

Create `backend/FantasyKeeper.Api/Endpoints/AdminKeepersEndpoints.cs`:

```csharp
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class AdminKeepersEndpoints
{
    public static void MapAdminKeepersEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/teams", (string pin, AuthService authService, IConfigStore configStore) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            var teams = configStore.GetTeams().Select(t => new { teamId = t.TeamId, name = t.Name });
            return Results.Ok(teams);
        });

        app.MapPost("/api/admin/keepers/import", (string pin, IFormFile file, AuthService authService, KeepersImportService importService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            using var stream = file.OpenReadStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            try
            {
                return Results.Ok(importService.StartImport(ms.ToArray(), file.FileName));
            }
            catch (InvalidWorkbookException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/admin/keepers/import/confirm", (string pin, ConfirmImportRequest request, AuthService authService, KeepersImportService importService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            try
            {
                return Results.Ok(importService.ConfirmImport(request.Assignments));
            }
            catch (InvalidWorkbookException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (KeeperValidationException ex)
            {
                return Results.BadRequest(new { errors = ex.Errors });
            }
        });

        app.MapGet("/api/admin/keepers/export", (string pin, AuthService authService, KeepersImportService importService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            try
            {
                var bytes = importService.Export();
                return Results.File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "keepers-export.xlsx");
            }
            catch (NotFoundException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapGet("/api/admin/keepers/status", (string pin, AuthService authService, IKeepersDataStore store) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            var data = store.LoadData();
            return Results.Ok(new { lastUpdatedUtc = data?.LastUpdatedUtc, sourceFileName = data?.SourceFileName });
        });
    }
}
```

- [ ] **Step 4: Register `KeepersImportService` and map the new endpoints**

In `backend/FantasyKeeper.Api/Program.cs`, add this line right after the existing `builder.Services.AddSingleton<KeepersService>();`:

```csharp
builder.Services.AddSingleton<KeepersImportService>();
```

And add this line right after the existing `app.MapKeeperEndpoints();`:

```csharp
app.MapAdminKeepersEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter AdminKeepersEndpointsTests`
Expected: 5 tests PASS.

- [ ] **Step 6: Build and run the full backend test suite**

Run: `dotnet build backend/FantasyKeeper.Api.sln` then `dotnet test backend/FantasyKeeper.Api.Tests`
Expected: builds clean, all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/FantasyKeeper.Api/Endpoints/AdminKeepersEndpoints.cs backend/FantasyKeeper.Api.Tests/AdminKeepersEndpointsTests.cs backend/FantasyKeeper.Api/Program.cs
git commit -m "Add admin import/confirm/export/status endpoints"
```

---

## Task 8: Remove obsolete Google Sheets/season code

**Files:**
- Delete: `backend/FantasyKeeper.Api/Models/Season.cs`, `backend/FantasyKeeper.Api/Models/TeamMapping.cs`, `backend/FantasyKeeper.Api/Services/ISheetsClient.cs`, `backend/FantasyKeeper.Api/Services/IDriveClient.cs`, `backend/FantasyKeeper.Api/Services/SeasonService.cs`, `backend/FantasyKeeper.Api/Services/A1Range.cs`, `backend/FantasyKeeper.Api/Services/Google/GoogleCredentialLoader.cs`, `backend/FantasyKeeper.Api/Services/Google/GoogleDriveClient.cs`, `backend/FantasyKeeper.Api/Services/Google/GoogleSheetsClient.cs`, `backend/FantasyKeeper.Api/Services/Google/RetryPolicy.cs`, `backend/FantasyKeeper.Api/Services/Dev/DevSheetsClient.cs`, `backend/FantasyKeeper.Api/Services/Dev/DevDriveClient.cs`, `backend/FantasyKeeper.Api/Endpoints/SeasonEndpoints.cs`, `backend/FantasyKeeper.Api.Tests/SeasonServiceTests.cs`, `backend/FantasyKeeper.Api.Tests/GoogleCredentialLoaderTests.cs`, `backend/FantasyKeeper.Api.Tests/DevClientsTests.cs`, `backend/FantasyKeeper.Api.Tests/RetryPolicyTests.cs`, `backend/FantasyKeeper.Api.Tests/A1RangeTests.cs`, `backend/FantasyKeeper.Api.Tests/Fakes/FakeSheetsClient.cs`, `backend/FantasyKeeper.Api.Tests/Fakes/FakeDriveClient.cs`, `config/seasons.json`, `config/team-mappings/` (whole directory)
- Modify: `backend/FantasyKeeper.Api/Models/AuthResult.cs`, `backend/FantasyKeeper.Api/Services/IConfigStore.cs`, `backend/FantasyKeeper.Api/Services/JsonConfigStore.cs`, `backend/FantasyKeeper.Api/Services/AuthService.cs`, `backend/FantasyKeeper.Api/Program.cs`, `backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj`, `backend/FantasyKeeper.Api/appsettings.json`
- Modify: `backend/FantasyKeeper.Api.Tests/Fakes/FakeConfigStore.cs`, `backend/FantasyKeeper.Api.Tests/AuthServiceTests.cs`, `backend/FantasyKeeper.Api.Tests/JsonConfigStoreTests.cs`, `backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs`, `backend/FantasyKeeper.Api.Tests/AdminKeepersEndpointsTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `AuthResult(AuthRole Role, string? TeamId)` (no `SeasonId`), `IConfigStore { IReadOnlyList<Team> GetTeams(); }` (no season/mapping methods).

- [ ] **Step 1: Delete the obsolete backend files**

```bash
git rm backend/FantasyKeeper.Api/Models/Season.cs backend/FantasyKeeper.Api/Models/TeamMapping.cs backend/FantasyKeeper.Api/Services/ISheetsClient.cs backend/FantasyKeeper.Api/Services/IDriveClient.cs backend/FantasyKeeper.Api/Services/SeasonService.cs backend/FantasyKeeper.Api/Services/A1Range.cs backend/FantasyKeeper.Api/Services/Google/GoogleCredentialLoader.cs backend/FantasyKeeper.Api/Services/Google/GoogleDriveClient.cs backend/FantasyKeeper.Api/Services/Google/GoogleSheetsClient.cs backend/FantasyKeeper.Api/Services/Google/RetryPolicy.cs backend/FantasyKeeper.Api/Services/Dev/DevSheetsClient.cs backend/FantasyKeeper.Api/Services/Dev/DevDriveClient.cs backend/FantasyKeeper.Api/Endpoints/SeasonEndpoints.cs backend/FantasyKeeper.Api.Tests/SeasonServiceTests.cs backend/FantasyKeeper.Api.Tests/GoogleCredentialLoaderTests.cs backend/FantasyKeeper.Api.Tests/DevClientsTests.cs backend/FantasyKeeper.Api.Tests/RetryPolicyTests.cs backend/FantasyKeeper.Api.Tests/A1RangeTests.cs backend/FantasyKeeper.Api.Tests/Fakes/FakeSheetsClient.cs backend/FantasyKeeper.Api.Tests/Fakes/FakeDriveClient.cs
git rm config/seasons.json
git rm -r config/team-mappings
```

- [ ] **Step 2: Trim `AuthResult`**

Replace the contents of `backend/FantasyKeeper.Api/Models/AuthResult.cs`:

```csharp
namespace FantasyKeeper.Api.Models;

public enum AuthRole { Owner, Admin }

public record AuthResult(AuthRole Role, string? TeamId);
```

- [ ] **Step 3: Trim `IConfigStore`**

Replace the contents of `backend/FantasyKeeper.Api/Services/IConfigStore.cs`:

```csharp
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public interface IConfigStore
{
    IReadOnlyList<Team> GetTeams();
}
```

- [ ] **Step 4: Trim `JsonConfigStore`**

Replace the contents of `backend/FantasyKeeper.Api/Services/JsonConfigStore.cs`:

```csharp
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

    public IReadOnlyList<Team> GetTeams() =>
        ReadJson<List<Team>>(Path.Combine(_configRoot, "teams.json")) ?? new List<Team>();

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
```

- [ ] **Step 5: Trim `AuthService`**

Replace the contents of `backend/FantasyKeeper.Api/Services/AuthService.cs`:

```csharp
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
            return new AuthResult(AuthRole.Admin, null);
        }

        var team = _configStore.GetTeams().FirstOrDefault(t => t.Pin == pin);
        return team is null ? null : new AuthResult(AuthRole.Owner, team.TeamId);
    }
}
```

- [ ] **Step 6: Trim `FakeConfigStore`**

Replace the contents of `backend/FantasyKeeper.Api.Tests/Fakes/FakeConfigStore.cs`:

```csharp
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeConfigStore : IConfigStore
{
    public List<Team> Teams { get; set; } = new();

    public IReadOnlyList<Team> GetTeams() => Teams;
}
```

- [ ] **Step 7: Update `AuthServiceTests`**

Replace the contents of `backend/FantasyKeeper.Api.Tests/AuthServiceTests.cs`:

```csharp
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class AuthServiceTests
{
    private static FakeConfigStore BuildStore() => new()
    {
        Teams = new List<Team> { new("b-squared", "B Squared", "1111") }
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
    public void ResolvePin_TeamPin_ReturnsOwnerWithTeamId()
    {
        var service = new AuthService(BuildStore(), "9999");
        var result = service.ResolvePin("1111");

        Assert.NotNull(result);
        Assert.Equal(AuthRole.Owner, result!.Role);
        Assert.Equal("b-squared", result.TeamId);
    }

    [Fact]
    public void ResolvePin_UnknownPin_ReturnsNull()
    {
        var service = new AuthService(BuildStore(), "9999");
        Assert.Null(service.ResolvePin("0000"));
    }
}
```

- [ ] **Step 8: Update `JsonConfigStoreTests`**

Replace the contents of `backend/FantasyKeeper.Api.Tests/JsonConfigStoreTests.cs`:

```csharp
using System;
using System.IO;
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
    public void GetTeams_WhenFileMissing_ReturnsEmptyList()
    {
        var store = new JsonConfigStore(_tempDir);
        Assert.Empty(store.GetTeams());
    }
}
```

- [ ] **Step 9: Remove the now-dead season seeding from `KeeperEndpointsTests` and `AdminKeepersEndpointsTests`**

In `backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs`, replace:

```csharp
        var configStore = new JsonConfigStore(_configRoot);
        // AuthService isn't updated until Task 8 and still requires an
        // active season to authenticate a team PIN — seeded here purely to
        // satisfy that; unrelated to the new keepers data path below.
        configStore.SaveSeasons(new List<Season>
        {
            new("season-1", "2026", "dev-sheet-2026", "active", DateTimeOffset.UtcNow)
        });
        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
```

with:

```csharp
        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
```

Do the same replacement in `backend/FantasyKeeper.Api.Tests/AdminKeepersEndpointsTests.cs` (its constructor has the identical block — replace that whole block the same way; the `using FantasyKeeper.Api.Services;` and `using System.Collections.Generic;` lines stay since `JsonConfigStore` and `Team`/`Season` types are still used elsewhere in that file).

- [ ] **Step 10: Rewrite `Program.cs`**

Replace the full contents of `backend/FantasyKeeper.Api/Program.cs`:

```csharp
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
```

- [ ] **Step 11: Remove the Google packages**

In `backend/FantasyKeeper.Api/FantasyKeeper.Api.csproj`, remove the three `Google.Apis.*` `<PackageReference>` lines, leaving only `ClosedXML`:

```xml
  <ItemGroup>
    <PackageReference Include="ClosedXML" Version="0.104.1" />
  </ItemGroup>
```

- [ ] **Step 12: Trim `appsettings.json`**

Replace the contents of `backend/FantasyKeeper.Api/appsettings.json`:

```json
{
  "ConfigRoot": "../../config",
  "DataRoot": "../../data",
  "AdminPin": "0000",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [ ] **Step 13: Build and run the full backend test suite**

Run: `dotnet build backend/FantasyKeeper.Api.sln` then `dotnet test backend/FantasyKeeper.Api.Tests`
Expected: builds clean with no leftover references to deleted types, all tests PASS.

- [ ] **Step 14: Commit**

```bash
git add -A backend/ config/
git commit -m "Remove obsolete Google Sheets/season code"
```

---

## Task 9: Frontend types and API client

**Files:**
- Modify: `frontend/src/types.ts`
- Modify: `frontend/src/api/client.ts`

**Interfaces:**
- Consumes: the backend response shapes from Tasks 4–7 (`KeeperTeamData`, `ImportPreview`, `ImportBlockPreview`, `KeepersData`'s `lastUpdatedUtc`/`sourceFileName` status shape).
- Produces: `AuthResult`, `KeeperRow`, `ExistingContractRow`, `KeeperTeamData`, `TeamSummary`, `ImportBlockPreview`, `ImportPreview`, `BlockAssignment`, `KeepersStatus` TS types; `authenticate`, `getKeepers`, `updateKeepers`, `getAdminTeams`, `importKeepers`, `confirmImport`, `exportKeepers`, `getKeepersStatus` client functions.

- [ ] **Step 1: Rewrite `types.ts`**

Replace the full contents of `frontend/src/types.ts`:

```ts
export type AuthRole = "Owner" | "Admin";

export interface AuthResult {
  role: AuthRole;
  teamId: string | null;
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
  existingContracts: ExistingContractRow[];
  newContracts: KeeperRow[];
}

export interface TeamSummary {
  teamId: string;
  name: string;
}

export interface ImportBlockPreview {
  blockIndex: number;
  rawNameInSheet: string;
  suggestedTeamId: string | null;
}

export interface ImportPreview {
  fileName: string;
  blocks: ImportBlockPreview[];
}

export interface BlockAssignment {
  blockIndex: number;
  teamId: string | null;
}

export interface KeepersStatus {
  lastUpdatedUtc: string | null;
  sourceFileName: string | null;
}
```

- [ ] **Step 2: Rewrite `api/client.ts`**

Replace the full contents of `frontend/src/api/client.ts`:

```ts
import type {
  AuthResult,
  KeeperTeamData,
  KeeperRow,
  TeamSummary,
  ImportPreview,
  BlockAssignment,
  KeepersStatus
} from "../types";

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export class ApiError extends Error {
  status: number;
  body: unknown;

  constructor(status: number, body: unknown) {
    super(`API request failed with status ${status}`);
    this.status = status;
    this.body = body;
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

export function getKeepers(pin: string): Promise<KeeperTeamData> {
  return request<KeeperTeamData>(`/api/keepers?pin=${encodeURIComponent(pin)}`);
}

export function updateKeepers(pin: string, newContracts: KeeperRow[]): Promise<KeeperTeamData> {
  return request<KeeperTeamData>(`/api/keepers?pin=${encodeURIComponent(pin)}`, {
    method: "PUT",
    body: JSON.stringify({ newContracts })
  });
}

export function getAdminTeams(pin: string): Promise<TeamSummary[]> {
  return request<TeamSummary[]>(`/api/admin/teams?pin=${encodeURIComponent(pin)}`);
}

export async function importKeepers(pin: string, file: File): Promise<ImportPreview> {
  const formData = new FormData();
  formData.append("file", file);

  const response = await fetch(`${BASE_URL}/api/admin/keepers/import?pin=${encodeURIComponent(pin)}`, {
    method: "POST",
    body: formData
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body);
  }

  return response.json() as Promise<ImportPreview>;
}

export function confirmImport(pin: string, assignments: BlockAssignment[]): Promise<void> {
  return request<void>(`/api/admin/keepers/import/confirm?pin=${encodeURIComponent(pin)}`, {
    method: "POST",
    body: JSON.stringify({ assignments })
  });
}

export async function exportKeepers(pin: string): Promise<Blob> {
  const response = await fetch(`${BASE_URL}/api/admin/keepers/export?pin=${encodeURIComponent(pin)}`);

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body);
  }

  return response.blob();
}

export function getKeepersStatus(pin: string): Promise<KeepersStatus> {
  return request<KeepersStatus>(`/api/admin/keepers/status?pin=${encodeURIComponent(pin)}`);
}
```

- [ ] **Step 3: Type-check**

Run: `npm run build --prefix frontend`
Expected: FAILS — `KeeperFormScreen.tsx`, `AdminPanel.tsx`, and `App.tsx` still reference the removed `Season` type and old function signatures. That's expected; they're fixed in Tasks 10–12. Confirm the errors are only in those three files.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/types.ts frontend/src/api/client.ts
git commit -m "Update frontend types and API client for the new keepers endpoints"
```

---

## Task 10: KeeperFormScreen and App wiring

**Files:**
- Modify: `frontend/src/screens/KeeperFormScreen.tsx`
- Modify: `frontend/src/App.tsx`

**Interfaces:**
- Consumes: `getKeepers(pin)`, `updateKeepers(pin, newContracts)` (Task 9), `KeeperTeamData`, `KeeperRow` (Task 9).
- Produces: `KeeperFormScreen({ pin }: { pin: string })` (no `defaultSeasonId` prop).

- [ ] **Step 1: Rewrite `KeeperFormScreen.tsx`**

Replace the full contents of `frontend/src/screens/KeeperFormScreen.tsx`:

```tsx
import { useCallback, useEffect, useState } from "react";
import { getKeepers, updateKeepers, ApiError } from "../api/client";
import type { KeeperRow, KeeperTeamData } from "../types";

interface Props {
  pin: string;
}

export function KeeperFormScreen({ pin }: Props) {
  const [data, setData] = useState<KeeperTeamData | null>(null);
  const [rows, setRows] = useState<KeeperRow[]>([]);
  const [deletedIndices, setDeletedIndices] = useState<Set<number>>(new Set());
  const [status, setStatus] = useState<"idle" | "loading" | "saving" | "error">("loading");
  const [message, setMessage] = useState<string | null>(null);

  const loadKeepers = useCallback(async () => {
    setStatus("loading");
    setMessage(null);
    try {
      const result = await getKeepers(pin);
      setData(result);
      setRows(result.newContracts);
      setDeletedIndices(new Set());
      setStatus("idle");
    } catch {
      setStatus("error");
      setMessage("Couldn't load your keepers. Try again.");
    }
  }, [pin]);

  useEffect(() => {
    loadKeepers();
  }, [loadKeepers]);

  function updateRow(index: number, field: keyof KeeperRow, value: string) {
    setRows((prev) =>
      prev.map((row, i) => {
        if (i !== index) return row;
        if (field === "player") return { ...row, player: value };
        return { ...row, [field]: value === "" ? null : Number(value) };
      })
    );
  }

  function toggleDelete(index: number) {
    setDeletedIndices((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  }

  async function handleSave() {
    setStatus("saving");
    setMessage(null);
    try {
      const submission = rows.map((row, i) =>
        deletedIndices.has(i) ? { player: "", contractType: null, salary: null, keeperYears: null } : row
      );
      const result = await updateKeepers(pin, submission);
      setData(result);
      setRows(result.newContracts);
      setDeletedIndices(new Set());
      setStatus("idle");
      setMessage("Saved.");
    } catch (err) {
      setStatus("idle");
      if (err instanceof ApiError && err.status === 400) {
        const body = err.body as { errors?: string[] };
        setMessage((body.errors ?? ["Some fields are invalid."]).join(" "));
      } else {
        setMessage("Couldn't save. Try again.");
      }
    }
  }

  if (status === "error") {
    return (
      <div>
        <p role="status">{message}</p>
        <button onClick={() => loadKeepers()}>Retry</button>
      </div>
    );
  }

  if (status === "loading" || !data) {
    return <p>Loading...</p>;
  }

  return (
    <div className="keeper-form">
      <h1>{data.teamName} — Keepers</h1>

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
          <tr><th>Player</th><th>Contract 1 or 2</th><th>Salary</th><th>Keeper Years</th><th>Delete</th></tr>
        </thead>
        <tbody>
          {rows.map((row, i) => {
            const isDeleted = deletedIndices.has(i);
            const rowStyle = isDeleted ? { textDecoration: "line-through", opacity: 0.5 } : undefined;
            return (
              <tr key={i} style={rowStyle}>
                <td><input style={{ width: "100%", boxSizing: "border-box" }} value={row.player} onChange={(e) => updateRow(i, "player", e.target.value)} /></td>
                <td>
                  <select style={{ width: "100%", boxSizing: "border-box" }} value={row.contractType ?? ""} onChange={(e) => updateRow(i, "contractType", e.target.value)}>
                    <option value="">--</option>
                    <option value="1">1</option>
                    <option value="2">2</option>
                  </select>
                </td>
                <td><input style={{ width: "100%", boxSizing: "border-box" }} value={row.salary ?? ""} onChange={(e) => updateRow(i, "salary", e.target.value)} /></td>
                <td><input style={{ width: "100%", boxSizing: "border-box" }} value={row.keeperYears ?? ""} onChange={(e) => updateRow(i, "keeperYears", e.target.value)} /></td>
                <td>
                  <input type="checkbox" checked={isDeleted} onChange={() => toggleDelete(i)} aria-label={`Delete contract for row ${i + 1}`} />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      <button onClick={handleSave} disabled={status === "saving"}>
        {status === "saving" ? "Saving..." : "Save Keepers"}
      </button>
      {message && <p role="status">{message}</p>}
    </div>
  );
}
```

- [ ] **Step 2: Drop the `defaultSeasonId` prop in `App.tsx`**

In `frontend/src/App.tsx`, change:

```tsx
        <KeeperFormScreen pin={pin} defaultSeasonId={auth.seasonId!} />
```

to:

```tsx
        <KeeperFormScreen pin={pin} />
```

- [ ] **Step 3: Type-check**

Run: `npm run build --prefix frontend`
Expected: FAILS only in `AdminPanel.tsx` now (still references removed `Season`/`getSeasons`/`createSeason`). Confirm `KeeperFormScreen.tsx` and `App.tsx` no longer error.

- [ ] **Step 4: Manual verification**

Run: `dotnet run --project backend/FantasyKeeper.Api` in one terminal and `npm run dev --prefix frontend` in another.
- [ ] Using a REST client or `curl`, seed a team (e.g. reuse `config/teams.json`'s `b-squared`/`1111`), then manually PUT a `KeepersData` blob via a throwaway script, OR skip live data and just confirm the "loading"/"error" states render correctly for a team PIN with no data yet imported (expect the friendly "Couldn't load your keepers" message from the 404 path).
- [ ] Once real data exists (after Task 12 lets you import through the UI), confirm: existing contracts render read-only, new contract rows are editable text inputs plus a Contract 1/2 dropdown, checking "Delete" strikes the row through but keeps it editable, and unchecking restores it, and saving with a row checked makes that row blank afterward.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/screens/KeeperFormScreen.tsx frontend/src/App.tsx
git commit -m "Drop season selector and add delete-contract checkbox to the keeper form"
```

---

## Task 11: AdminPanel status and export

**Files:**
- Modify: `frontend/src/screens/AdminPanel.tsx`

**Interfaces:**
- Consumes: `getKeepersStatus(pin)`, `exportKeepers(pin)` (Task 9).
- Produces: `AdminPanel({ pin }: { pin: string })` rendering last-updated status and an export button (import/review comes in Task 12).

- [ ] **Step 1: Rewrite `AdminPanel.tsx`**

Replace the full contents of `frontend/src/screens/AdminPanel.tsx`:

```tsx
import { useEffect, useState } from "react";
import { getKeepersStatus, exportKeepers } from "../api/client";
import type { KeepersStatus } from "../types";

interface Props {
  pin: string;
}

export function AdminPanel({ pin }: Props) {
  const [status, setStatus] = useState<KeepersStatus | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  function refreshStatus() {
    getKeepersStatus(pin).then(setStatus).catch(() => setStatus(null));
  }

  useEffect(refreshStatus, [pin]);

  async function handleExport() {
    setMessage(null);
    try {
      const blob = await exportKeepers(pin);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = "keepers-export.xlsx";
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      setMessage("Couldn't export. Make sure keeper data has been imported.");
    }
  }

  return (
    <div className="admin-panel">
      <h1>Keepers Administration</h1>

      <p>
        {status?.lastUpdatedUtc
          ? `Last updated: ${new Date(status.lastUpdatedUtc).toLocaleString()} (from ${status.sourceFileName})`
          : "No keeper data has been imported yet."}
      </p>

      <button onClick={handleExport} disabled={!status?.lastUpdatedUtc}>Export current data</button>

      {message && <p role="status">{message}</p>}
    </div>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `npm run build --prefix frontend`
Expected: PASSES with no errors across the whole frontend (import/review comes in Task 12, but this file is self-contained and complete as far as its current feature set goes).

- [ ] **Step 3: Manual verification**

Run: `dotnet run --project backend/FantasyKeeper.Api` and `npm run dev --prefix frontend`, log in with the admin PIN.
- [ ] Confirm the panel shows "No keeper data has been imported yet." and the Export button is disabled before any import exists.
- [ ] After Task 12 adds import, re-verify here: clicking Export downloads an `keepers-export.xlsx` file and the status line updates to show a timestamp and source filename.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/screens/AdminPanel.tsx
git commit -m "Add keeper data status display and export to AdminPanel"
```

---

## Task 12: AdminPanel import and review flow

**Files:**
- Modify: `frontend/src/screens/AdminPanel.tsx`

**Interfaces:**
- Consumes: `getAdminTeams(pin)`, `importKeepers(pin, file)`, `confirmImport(pin, assignments)` (Task 9); `handleExport` pattern from Task 11 (reused inside the overwrite warning).
- Produces: `AdminPanel` with full import → overwrite-warning → review → confirm flow.

- [ ] **Step 1: Rewrite `AdminPanel.tsx`**

Replace the full contents of `frontend/src/screens/AdminPanel.tsx`:

```tsx
import { useEffect, useState, type ChangeEvent } from "react";
import { getAdminTeams, importKeepers, confirmImport, getKeepersStatus, exportKeepers } from "../api/client";
import type { TeamSummary, ImportPreview, BlockAssignment, KeepersStatus } from "../types";

interface Props {
  pin: string;
}

const SKIP = "__skip__";

export function AdminPanel({ pin }: Props) {
  const [teams, setTeams] = useState<TeamSummary[]>([]);
  const [status, setStatus] = useState<KeepersStatus | null>(null);
  const [pendingFile, setPendingFile] = useState<File | null>(null);
  const [showOverwriteWarning, setShowOverwriteWarning] = useState(false);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [assignments, setAssignments] = useState<Record<number, string>>({});
  const [phase, setPhase] = useState<"idle" | "importing" | "confirming">("idle");
  const [message, setMessage] = useState<string | null>(null);

  function refresh() {
    getAdminTeams(pin).then(setTeams).catch(() => setTeams([]));
    getKeepersStatus(pin).then(setStatus).catch(() => setStatus(null));
  }

  useEffect(refresh, [pin]);

  async function handleExport() {
    setMessage(null);
    try {
      const blob = await exportKeepers(pin);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = "keepers-export.xlsx";
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      setMessage("Couldn't export. Make sure keeper data has been imported.");
    }
  }

  function handleFileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    setPendingFile(file);
    setMessage(null);
    if (status?.lastUpdatedUtc) {
      setShowOverwriteWarning(true);
    } else {
      void startImport(file);
    }
  }

  async function startImport(file: File) {
    setShowOverwriteWarning(false);
    setPhase("importing");
    setMessage(null);
    try {
      const result = await importKeepers(pin, file);
      setPreview(result);
      const initial: Record<number, string> = {};
      for (const block of result.blocks) {
        initial[block.blockIndex] = block.suggestedTeamId ?? "";
      }
      setAssignments(initial);
    } catch {
      setMessage("Couldn't read that file. Make sure it's the league's xlsx export.");
    } finally {
      setPhase("idle");
      setPendingFile(null);
    }
  }

  function cancelReview() {
    setPreview(null);
    setAssignments({});
  }

  const chosenTeamIds = Object.values(assignments).filter((v) => v !== "" && v !== SKIP);
  const hasDuplicates = new Set(chosenTeamIds).size !== chosenTeamIds.length;
  const hasUnresolved = preview?.blocks.some((b) => assignments[b.blockIndex] === "") ?? true;
  const canConfirm = !!preview && !hasUnresolved && !hasDuplicates;

  async function handleConfirm() {
    if (!preview) return;
    setPhase("confirming");
    setMessage(null);
    try {
      const payload: BlockAssignment[] = preview.blocks.map((b) => ({
        blockIndex: b.blockIndex,
        teamId: assignments[b.blockIndex] === SKIP ? null : assignments[b.blockIndex]
      }));
      await confirmImport(pin, payload);
      setPreview(null);
      setAssignments({});
      setMessage("Import confirmed.");
      refresh();
    } catch {
      setMessage("Couldn't confirm the import. Try again.");
    } finally {
      setPhase("idle");
    }
  }

  return (
    <div className="admin-panel">
      <h1>Keepers Administration</h1>

      <p>
        {status?.lastUpdatedUtc
          ? `Last updated: ${new Date(status.lastUpdatedUtc).toLocaleString()} (from ${status.sourceFileName})`
          : "No keeper data has been imported yet."}
      </p>

      <button onClick={handleExport} disabled={!status?.lastUpdatedUtc}>Export current data</button>

      {!preview && (
        <div>
          <label htmlFor="import-file">Import season xlsx</label>
          <input id="import-file" type="file" accept=".xlsx" onChange={handleFileSelected} disabled={phase === "importing"} />
        </div>
      )}

      {showOverwriteWarning && pendingFile && (
        <div role="alertdialog">
          <p>Importing will overwrite all current keeper data. Consider exporting a backup first.</p>
          <button onClick={handleExport}>Export current data</button>
          <button onClick={() => void startImport(pendingFile)}>Continue import</button>
          <button onClick={() => { setShowOverwriteWarning(false); setPendingFile(null); }}>Cancel</button>
        </div>
      )}

      {preview && (
        <div>
          <h2>Confirm teams for "{preview.fileName}"</h2>
          <table>
            <thead>
              <tr><th>Detected in sheet</th><th>Team</th></tr>
            </thead>
            <tbody>
              {preview.blocks.map((block) => (
                <tr key={block.blockIndex}>
                  <td>{block.rawNameInSheet}</td>
                  <td>
                    <select
                      value={assignments[block.blockIndex] ?? ""}
                      onChange={(e) =>
                        setAssignments((prev) => ({ ...prev, [block.blockIndex]: e.target.value }))
                      }
                    >
                      <option value="">-- Choose --</option>
                      <option value={SKIP}>-- Skip this block --</option>
                      {teams.map((team) => (
                        <option key={team.teamId} value={team.teamId}>{team.name}</option>
                      ))}
                    </select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {hasDuplicates && <p role="status">The same team is assigned to more than one block.</p>}
          <button onClick={handleConfirm} disabled={!canConfirm || phase === "confirming"}>
            {phase === "confirming" ? "Confirming..." : "Confirm Import"}
          </button>
          <button onClick={cancelReview}>Cancel</button>
        </div>
      )}

      {message && <p role="status">{message}</p>}
    </div>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `npm run build --prefix frontend`
Expected: PASSES with no errors.

- [ ] **Step 3: Manual verification**

Run: `dotnet run --project backend/FantasyKeeper.Api` and `npm run dev --prefix frontend`, log in with the admin PIN.
- [ ] Build a small xlsx by hand (or reuse the league's real export) with a `2026 Keepers`-style tab and at least one team block matching a `teamId` in `config/teams.json`. Import it — confirm the review screen shows the detected name with a pre-selected team match.
- [ ] Leave a block unresolved — confirm "Confirm Import" stays disabled. Assign the same team to two blocks — confirm the duplicate warning shows and Confirm stays disabled. Resolve everything and confirm — verify the status line updates and a team owner can now see the imported data via `KeeperFormScreen`.
- [ ] Import a second file — confirm the overwrite warning appears with an Export button, and that Cancel returns to the idle state without touching existing data.
- [ ] Upload a non-xlsx file (or a xlsx with no matching tab) — confirm a friendly error message appears instead of a crash.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/screens/AdminPanel.tsx
git commit -m "Add import and team-review flow to AdminPanel"
```

---

## Task 13: README update

**Files:**
- Modify: `fantasy-keeper-app/README.md`

**Interfaces:**
- Consumes: nothing (documentation only).
- Produces: nothing (documentation only).

- [ ] **Step 1: Rewrite the README**

Replace the full contents of `fantasy-keeper-app/README.md`:

```markdown
# Fantasy Keeper App

Friendly keeper-submission UI for the Worm Burners Dynasty League. The
admin uploads the league's season xlsx export as the source of truth;
team owners submit their keepers through a focused web form; the admin
can export the current state back to xlsx at any time. See
`docs/superpowers/specs/2026-08-24-xlsx-keepers-import-design.md` for
the full design (supersedes the earlier live-Google-Sheets design in
`docs/superpowers/specs/2026-08-21-fantasy-keeper-app-design.md`).

## Running locally

```bash
dotnet run --project backend/FantasyKeeper.Api
```

The app listens on `http://localhost:5080`. Try:

```bash
curl "http://localhost:5080/api/keepers?pin=1111"
```

(Returns 404 until an xlsx has been imported through the Admin panel —
see "Importing keeper data" below.)

Seeded PINs come from `config/teams.json`; the admin PIN comes from
`AdminPin` in `backend/FantasyKeeper.Api/appsettings.json` (defaults to
`0000`).

The command above starts the backend only, which serves a JSON API but
no UI. See "Frontend" below to also run/build the React app.

## Frontend

The React/TypeScript UI lives in `frontend/`. It talks to the backend over
HTTP using the `VITE_API_BASE_URL` it's built with.

**Local dev with hot reload** — run the backend and frontend as two
separate servers:

```bash
dotnet run --project backend/FantasyKeeper.Api
```

```bash
npm install --prefix frontend
npm run dev --prefix frontend
```

The Vite dev server proxies API calls to `http://localhost:5080` via the
`VITE_API_BASE_URL` set in `frontend/.env.development`. Open the URL Vite
prints (typically `http://localhost:5173`).

**Single-process deployment** — build the frontend into the backend's
static file directory, then run the backend alone:

```bash
npm run build --prefix frontend
```

This populates `backend/FantasyKeeper.Api/wwwroot` (gitignored, rebuilt
each time) with the production bundle. Once it exists:

```bash
dotnet run --project backend/FantasyKeeper.Api
```

serves both the API and the UI from `http://localhost:5080` — no separate
frontend server needed. This is the mode used in production, so the build
must be re-run whenever frontend source changes.

## Importing keeper data

1. Log in to the Admin panel with the admin PIN.
2. Under "Import season xlsx," choose the league's current xlsx export
   (the tab holding each team's Existing/New Contracts tables is
   located automatically — no configuration needed).
3. If keeper data has been imported before, you'll be warned that
   importing overwrites it; use "Export current data" there first if
   you want a backup.
4. Review the detected teams. Each row shows the name found in the
   sheet next to a dropdown of known teams (pre-filled with a best
   guess) — confirm or correct every row, or mark it "Skip," then
   click "Confirm Import."
5. Team owners can now log in with their PINs and submit keepers.
   Admin can click "Export current data" at any time to download the
   current state as xlsx.

## Config

- `config/teams.json` — `{ teamId, name, pin }` per team. Hand-edit to
  add/remove teams or change PINs.
- `AdminPin` in `appsettings.json` (or the `AdminPin` environment
  variable in production) — the commissioner's PIN.
- `data/` (gitignored) — holds the last-imported xlsx and its parsed
  state; not meant to be hand-edited.
```

- [ ] **Step 2: Commit**

```bash
git add fantasy-keeper-app/README.md
git commit -m "Update README for the xlsx import/export workflow"
```
