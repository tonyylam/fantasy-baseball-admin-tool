# Team Navigation & Contract Deletion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a left-side team navigation menu (every team browsable, read-only for teams you don't own, full access for admins), and add the ability to delete an existing contract — marked with strikethrough on export rather than cleared, since the source workbook stays commissioner-maintained.

**Architecture:** The backend adds row-position tracking for Existing Contracts (mirroring how New Contracts rows are already tracked), a `Deleted` flag per existing contract, and a `CanEdit` flag on the team-data response computed by the endpoint from the caller's role and the team being viewed — `KeepersService` itself stays unaware of roles. The frontend adds a persistent sidebar and turns the single "your own team" screen into a generic team-viewing/editing screen driven by that `CanEdit` flag.

**Tech Stack:** ASP.NET Core minimal API (.NET 8), ClosedXML, React + TypeScript (Vite), xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-team-navigation-and-contract-deletion-design.md` (relative to `fantasy-keeper-app/`)

## Global Constraints

- No router/URL-based navigation — the selected team/view is plain React state, matching the project's existing "no state-management or routing library" choice.
- No migration for already-imported data — `ExistingContractsRows` is only populated by imports that happen after this ships; deleting an existing contract requires a re-import on older data.
- No undo/audit history — every save sends the full current set of deleted-existing-contract indices (not just newly-deleted ones), so unchecking a previously-deleted row and saving fully restores it.
- Deleting an existing contract never clears its values, in the app or in the export — only a `Deleted` flag changes, and export turns that into a font strikethrough on the H/I/J/L/M cells. This is deliberately different from New Contracts deletion, which still clears to blank on save (unchanged, existing behavior) — the two mechanisms coexist by design, do not unify them.
- `teamId` is a required query parameter on `GET`/`PUT /api/keepers` — there is no implicit default to the caller's own team.
- `KeepersService` does not know about roles or permissions. `CanEdit` is computed by the endpoint (`auth.Role == Admin || teamId == auth.TeamId`) and passed into `GetKeeperData`; `UpdateKeeperData` is only ever reachable once the endpoint has already confirmed the caller may edit.

---

## Task 1: Existing-contract row tracking and strikethrough writer

**Files:**
- Modify: `backend/FantasyKeeper.Api/Models/ExistingContractRow.cs`
- Modify: `backend/FantasyKeeper.Api/Models/KeepersData.cs`
- Modify: `backend/FantasyKeeper.Api/Services/KeeperWorkbookParser.cs`
- Modify: `backend/FantasyKeeper.Api/Services/KeeperWorkbookWriter.cs`
- Modify: `backend/FantasyKeeper.Api/Services/KeepersImportService.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/KeeperWorkbookParserTests.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/KeeperWorkbookWriterTests.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/FileKeepersDataStoreTests.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs` (only its `StoredTeamKeepers` construction sites — no other change in this task)
- Modify: `backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs` (only its `StoredTeamKeepers` construction site — no other change in this task)

**Interfaces:**
- Consumes: nothing new.
- Produces: `ExistingContractRow(string Player, string ContractInfo, decimal? LastYearSalary, decimal? LeagueValue, decimal? ThisYearSalary, bool Deleted = false)`, `StoredTeamKeepers(string RawNameInSheet, int HeaderRow, IReadOnlyList<int> NewContractsRows, IReadOnlyList<KeeperRow> NewContracts, IReadOnlyList<int> ExistingContractsRows, IReadOnlyList<ExistingContractRow> ExistingContracts)`, `KeeperWorkbookWriter.WriteKeepers(byte[] originalWorkbookBytes, string sheetName, IReadOnlyDictionary<string, StoredTeamKeepers> teams): byte[]` (renamed from `WriteNewContracts`).

This task deliberately does NOT touch `KeeperTeamData`, `KeeperSubmission`, `KeepersService`'s public method signatures, or any endpoint — those come in Task 2. The full solution must build and all existing tests must still pass at the end of this task; only the shape of `StoredTeamKeepers`/`ExistingContractRow` and the writer's name/behavior change.

- [ ] **Step 1: Add `Deleted` to `ExistingContractRow`**

In `backend/FantasyKeeper.Api/Models/ExistingContractRow.cs`, replace the contents:

```csharp
namespace FantasyKeeper.Api.Models;

public record ExistingContractRow(string Player, string ContractInfo, decimal? LastYearSalary, decimal? LeagueValue, decimal? ThisYearSalary, bool Deleted = false);
```

The `= false` default means every existing 5-argument call site across the test suite keeps compiling unchanged.

- [ ] **Step 2: Add `ExistingContractsRows` to `StoredTeamKeepers`**

In `backend/FantasyKeeper.Api/Models/KeepersData.cs`, replace the contents:

```csharp
namespace FantasyKeeper.Api.Models;

public record StoredTeamKeepers(
    string RawNameInSheet,
    int HeaderRow,
    IReadOnlyList<int> NewContractsRows,
    IReadOnlyList<KeeperRow> NewContracts,
    IReadOnlyList<int> ExistingContractsRows,
    IReadOnlyList<ExistingContractRow> ExistingContracts);

public record KeepersData(
    string SourceFileName,
    string SheetName,
    DateTimeOffset LastUpdatedUtc,
    IReadOnlyDictionary<string, StoredTeamKeepers> Teams);
```

This has no default (a `IReadOnlyList<int>` can't be a compile-time-constant default), so every existing `new StoredTeamKeepers(...)` call site needs an extra argument — Steps 6-10 below fix every one of them.

- [ ] **Step 3: Run the whole solution to see the expected breakage**

Run: `dotnet build backend/FantasyKeeper.sln`
Expected: build ERRORS in exactly these files, each because it has a `new StoredTeamKeepers(...)` call site now missing the `ExistingContractsRows` argument: `KeeperWorkbookParser.cs`, `KeeperWorkbookWriterTests.cs`, `FileKeepersDataStoreTests.cs`, `KeepersServiceTests.cs`, `KeeperEndpointsTests.cs`. Fixed by Steps 4 and 9 below. (`KeepersImportService.cs`'s call to the not-yet-renamed `WriteNewContracts` is still valid at this point — that rename happens in Steps 6-7, after this checkpoint — so it does NOT appear in this error list.)

- [ ] **Step 4: Record existing-contract row numbers in the parser**

In `backend/FantasyKeeper.Api/Services/KeeperWorkbookParser.cs`, replace:

```csharp
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
```

with:

```csharp
                    var newContractsRows = new List<int>();
                    var newContracts = new List<KeeperRow>();
                    var existingContractsRows = new List<int>();
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
                            existingContractsRows.Add(row);
                            existingContracts.Add(new ExistingContractRow(
                                existingPlayer,
                                worksheet.Cell(row, "I").GetString().Trim(),
                                ParseDecimal(worksheet.Cell(row, "J")),
                                ParseDecimal(worksheet.Cell(row, "L")),
                                ParseDecimal(worksheet.Cell(row, "M"))));
                        }
                    }

                    teams.Add(new StoredTeamKeepers(rawName, headerRow, newContractsRows, newContracts, existingContractsRows, existingContracts));
```

- [ ] **Step 5: Add a parser test for the new row tracking**

In `backend/FantasyKeeper.Api.Tests/KeeperWorkbookParserTests.cs`, add this test right after `Parse_SingleTeam_NewContractsRowsMatchNewContractsCount`:

```csharp
    [Fact]
    public void Parse_SingleTeam_ExistingContractsRowsMatchExistingContractsCount()
    {
        var bytes = BuildWorkbook(sheet => WriteTeamBlock(sheet, teamNameRow: 6, teamName: "B Squared"));

        using var ms = new MemoryStream(bytes);
        var parsed = KeeperWorkbookParser.Parse(ms);

        var team = parsed.Teams[0];
        Assert.Equal(team.ExistingContractsRows.Count, team.ExistingContracts.Count);
        Assert.Equal(8, team.ExistingContractsRows[0]);
    }
```

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperWorkbookParserTests`
Expected: 6/6 PASS (the 5 existing parser tests plus this new one).

- [ ] **Step 6: Rename the writer to `WriteKeepers` and add the strikethrough pass**

Replace the full contents of `backend/FantasyKeeper.Api/Services/KeeperWorkbookWriter.cs`:

```csharp
using ClosedXML.Excel;
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public static class KeeperWorkbookWriter
{
    private static readonly string[] ExistingContractColumns = { "H", "I", "J", "L", "M" };

    public static byte[] WriteKeepers(byte[] originalWorkbookBytes, string sheetName, IReadOnlyDictionary<string, StoredTeamKeepers> teams)
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

            for (var i = 0; i < team.ExistingContractsRows.Count; i++)
            {
                if (i >= team.ExistingContracts.Count || !team.ExistingContracts[i].Deleted)
                {
                    continue;
                }

                var row = team.ExistingContractsRows[i];
                foreach (var column in ExistingContractColumns)
                {
                    worksheet.Cell(row, column).Style.Font.Strikethrough = true;
                }
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

- [ ] **Step 7: Update the writer's only production call site**

In `backend/FantasyKeeper.Api/Services/KeepersImportService.cs`, find the `Export()` method and replace:

```csharp
        return KeeperWorkbookWriter.WriteNewContracts(workbookBytes, data.SheetName, data.Teams);
```

with:

```csharp
        return KeeperWorkbookWriter.WriteKeepers(workbookBytes, data.SheetName, data.Teams);
```

- [ ] **Step 8: Rewrite the writer tests — update call sites, rename, and add the strikethrough test**

Replace the full contents of `backend/FantasyKeeper.Api.Tests/KeeperWorkbookWriterTests.cs`:

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
    public void WriteKeepers_UpdatesOnlyMappedCells()
    {
        var original = BuildWorkbook();
        var teams = new Dictionary<string, StoredTeamKeepers>
        {
            ["b-squared"] = new StoredTeamKeepers(
                "B Squared",
                7,
                new List<int> { 8 },
                new List<KeeperRow> { new("New Player", 2, 10, 3) },
                new List<int>(),
                new List<ExistingContractRow>())
        };

        var result = KeeperWorkbookWriter.WriteKeepers(original, "2026 Keepers", teams);

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
    public void WriteKeepers_PreservesFormulasOtherTabsAndStyles()
    {
        byte[] original;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("2026 Keepers");
            sheet.Cell(6, "A").Value = "B Squared";
            sheet.Cell(7, "C").Value = "Player";
            sheet.Cell(8, "C").Value = "Old Player";
            sheet.Cell(8, "D").Value = 1;
            sheet.Cell(8, "E").Value = 5;
            sheet.Cell(8, "F").Value = 1;
            sheet.Cell(9, "E").Value = 7;

            sheet.Cell(20, "E").FormulaA1 = "=SUM(E8:E9)";
            sheet.Cell(20, "A").Value = "Total salary";
            sheet.Cell(20, "A").Style.Font.Bold = true;
            sheet.Cell(20, "A").Style.Fill.BackgroundColor = XLColor.Yellow;

            var other = workbook.Worksheets.Add("Draft Notes");
            other.Cell(1, "A").Value = "Keep this tab intact";
            other.Cell(2, "B").Value = 42;

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            original = ms.ToArray();
        }

        var teams = new Dictionary<string, StoredTeamKeepers>
        {
            ["b-squared"] = new StoredTeamKeepers(
                "B Squared",
                7,
                new List<int> { 8 },
                new List<KeeperRow> { new("New Player", 2, 10, 3) },
                new List<int>(),
                new List<ExistingContractRow>())
        };

        var result = KeeperWorkbookWriter.WriteKeepers(original, "2026 Keepers", teams);

        using var resultStream = new MemoryStream(result);
        using var reopened = new XLWorkbook(resultStream);
        var target = reopened.Worksheet("2026 Keepers");

        Assert.Equal("New Player", target.Cell(8, "C").GetString());

        Assert.True(target.Cell(20, "E").HasFormula);
        Assert.Equal("SUM(E8:E9)", target.Cell(20, "E").FormulaA1.TrimStart('='));

        Assert.Equal("Total salary", target.Cell(20, "A").GetString());
        Assert.True(target.Cell(20, "A").Style.Font.Bold);
        Assert.Equal(XLColor.Yellow, target.Cell(20, "A").Style.Fill.BackgroundColor);

        Assert.Equal(2, reopened.Worksheets.Count);
        var otherSheet = reopened.Worksheet("Draft Notes");
        Assert.Equal("Keep this tab intact", otherSheet.Cell(1, "A").GetString());
        Assert.Equal(42, otherSheet.Cell(2, "B").GetValue<int>());
    }

    [Fact]
    public void WriteKeepers_BlankRow_ClearsCells()
    {
        var original = BuildWorkbook();
        var teams = new Dictionary<string, StoredTeamKeepers>
        {
            ["b-squared"] = new StoredTeamKeepers(
                "B Squared",
                7,
                new List<int> { 8 },
                new List<KeeperRow> { new("", null, null, null) },
                new List<int>(),
                new List<ExistingContractRow>())
        };

        var result = KeeperWorkbookWriter.WriteKeepers(original, "2026 Keepers", teams);

        using var ms = new MemoryStream(result);
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheet("2026 Keepers");

        Assert.Equal("", sheet.Cell(8, "C").GetString());
        Assert.Equal("", sheet.Cell(8, "D").GetString());
    }

    [Fact]
    public void WriteKeepers_DeletedExistingContract_AppliesStrikethroughWithoutClearingValues()
    {
        byte[] original;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("2026 Keepers");
            sheet.Cell(6, "A").Value = "B Squared";
            sheet.Cell(7, "C").Value = "Player";
            sheet.Cell(8, "H").Value = "Jasson Dominguez";
            sheet.Cell(8, "I").Value = "#1 - 2/3";
            sheet.Cell(8, "J").Value = 3;
            sheet.Cell(8, "L").Value = 1.34;
            sheet.Cell(8, "M").Value = 1.34;
            sheet.Cell(9, "H").Value = "Other Player";
            sheet.Cell(9, "I").Value = "#1 - 3/3";
            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            original = ms.ToArray();
        }

        var teams = new Dictionary<string, StoredTeamKeepers>
        {
            ["b-squared"] = new StoredTeamKeepers(
                "B Squared",
                7,
                new List<int>(),
                new List<KeeperRow>(),
                new List<int> { 8, 9 },
                new List<ExistingContractRow>
                {
                    new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m, Deleted: true),
                    new("Other Player", "#1 - 3/3", null, null, null, Deleted: false)
                })
        };

        var result = KeeperWorkbookWriter.WriteKeepers(original, "2026 Keepers", teams);

        using var ms2 = new MemoryStream(result);
        using var workbook2 = new XLWorkbook(ms2);
        var sheet2 = workbook2.Worksheet("2026 Keepers");

        Assert.Equal("Jasson Dominguez", sheet2.Cell(8, "H").GetString());
        Assert.True(sheet2.Cell(8, "H").Style.Font.Strikethrough);
        Assert.True(sheet2.Cell(8, "I").Style.Font.Strikethrough);
        Assert.True(sheet2.Cell(8, "J").Style.Font.Strikethrough);
        Assert.True(sheet2.Cell(8, "L").Style.Font.Strikethrough);
        Assert.True(sheet2.Cell(8, "M").Style.Font.Strikethrough);

        Assert.Equal("Other Player", sheet2.Cell(9, "H").GetString());
        Assert.False(sheet2.Cell(9, "H").Style.Font.Strikethrough);
    }
}
```

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperWorkbookWriterTests`
Expected: 4/4 PASS.

- [ ] **Step 9: Fix the remaining `StoredTeamKeepers` call sites**

In `backend/FantasyKeeper.Api.Tests/FileKeepersDataStoreTests.cs`, in `SaveAndLoadData_RoundTrips`, replace:

```csharp
                    new List<KeeperRow> { new("T. Story", 1, 14, 2) },
                    new List<ExistingContractRow> { new("J. Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m) })
```

with:

```csharp
                    new List<KeeperRow> { new("T. Story", 1, 14, 2) },
                    new List<int> { 8 },
                    new List<ExistingContractRow> { new("J. Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m) })
```

In `backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs`, there are two `new StoredTeamKeepers(...)` call sites. In the `Build()` helper method, replace:

```csharp
                        new List<KeeperRow>
                        {
                            new("T. Story", 1, 14, 2),
                            new("", null, null, null)
                        },
                        new List<ExistingContractRow>
                        {
                            new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                        })
```

with:

```csharp
                        new List<KeeperRow>
                        {
                            new("T. Story", 1, 14, 2),
                            new("", null, null, null)
                        },
                        new List<int>(),
                        new List<ExistingContractRow>
                        {
                            new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                        })
```

And in `UpdateKeeperData_ConcurrentCallers_DoNotInterleaveReadModifyWrite`, replace:

```csharp
                        new List<KeeperRow> { new("T. Story", 1, 14, 2) },
                        new List<ExistingContractRow>())
```

with:

```csharp
                        new List<KeeperRow> { new("T. Story", 1, 14, 2) },
                        new List<int>(),
                        new List<ExistingContractRow>())
```

In `backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs`, replace:

```csharp
                    new List<ExistingContractRow>
                    {
                        new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                    })
```

with:

```csharp
                    new List<int> { 20 },
                    new List<ExistingContractRow>
                    {
                        new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                    })
```

- [ ] **Step 10: Build and run the full backend test suite**

Run: `dotnet build backend/FantasyKeeper.sln` then `dotnet test backend/FantasyKeeper.Api.Tests`
Expected: builds clean with 0 warnings/errors, all tests PASS (56 total — 54 from before plus the 2 new ones added in this task).

- [ ] **Step 11: Commit**

```bash
git add backend/FantasyKeeper.Api/Models/ExistingContractRow.cs backend/FantasyKeeper.Api/Models/KeepersData.cs backend/FantasyKeeper.Api/Services/KeeperWorkbookParser.cs backend/FantasyKeeper.Api/Services/KeeperWorkbookWriter.cs backend/FantasyKeeper.Api/Services/KeepersImportService.cs backend/FantasyKeeper.Api.Tests/KeeperWorkbookParserTests.cs backend/FantasyKeeper.Api.Tests/KeeperWorkbookWriterTests.cs backend/FantasyKeeper.Api.Tests/FileKeepersDataStoreTests.cs backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs
git commit -m "Track existing-contract row positions and strikethrough deleted ones on export"
```

---

## Task 2: Cross-team authorization, `/api/teams`, and existing-contract deletion

**Files:**
- Modify: `backend/FantasyKeeper.Api/Models/KeeperTeamData.cs`
- Modify: `backend/FantasyKeeper.Api/Models/KeeperSubmission.cs`
- Modify: `backend/FantasyKeeper.Api/Services/KeepersService.cs`
- Modify: `backend/FantasyKeeper.Api/Endpoints/KeeperEndpoints.cs`
- Modify: `backend/FantasyKeeper.Api/Endpoints/AdminKeepersEndpoints.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs`
- Modify: `backend/FantasyKeeper.Api.Tests/AdminKeepersEndpointsTests.cs`

**Interfaces:**
- Consumes: `StoredTeamKeepers.ExistingContractsRows`, `ExistingContractRow.Deleted` (Task 1).
- Produces: `KeeperTeamData(string TeamName, bool CanEdit, IReadOnlyList<ExistingContractRow> ExistingContracts, IReadOnlyList<KeeperRow> NewContracts)`, `KeeperSubmission(IReadOnlyList<KeeperRow> NewContracts, IReadOnlyList<int> DeletedExistingContractIndices)`, `KeepersService.GetKeeperData(string teamId, bool canEdit): KeeperTeamData`, `KeepersService.UpdateKeeperData(string teamId, KeeperSubmission submission): KeeperTeamData` (unchanged signature, new behavior), `GET /api/keepers?pin=...&teamId=...` (teamId required, any authenticated pin), `PUT /api/keepers?pin=...&teamId=...` (403 if not admin and not own team), `GET /api/teams?pin=...` (any authenticated pin) replacing `/api/admin/teams`.

This task's full-solution build and test suite must be green at the end — Task 1 already left everything compiling, so this task's every step should keep it that way (no intentional broken-build waypoint this time, since nothing outside this task's own files depends on the pieces being changed here).

- [ ] **Step 1: Write the failing `KeepersServiceTests.cs`**

Replace the full contents of `backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
                        new List<int> { 10, 11 },
                        new List<ExistingContractRow>
                        {
                            new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m),
                            new("Other Player", "#1 - 3/3", 5, 2m, 2m)
                        })
                })
        };

        return (config, store, new KeepersService(store, config));
    }

    [Fact]
    public void GetKeeperData_ReturnsStoredRows()
    {
        var (_, _, service) = Build();

        var data = service.GetKeeperData("b-squared", canEdit: true);

        Assert.Equal("B Squared", data.TeamName);
        Assert.True(data.CanEdit);
        Assert.Equal("T. Story", data.NewContracts[0].Player);
        Assert.Equal(1, data.NewContracts[0].ContractType);
        Assert.Equal(14, data.NewContracts[0].Salary);
        Assert.Equal("Jasson Dominguez", data.ExistingContracts[0].Player);
        Assert.False(data.ExistingContracts[0].Deleted);
    }

    [Fact]
    public void GetKeeperData_ReadOnlyViewer_ReturnsCanEditFalse()
    {
        var (_, _, service) = Build();

        var data = service.GetKeeperData("b-squared", canEdit: false);

        Assert.False(data.CanEdit);
    }

    [Fact]
    public void GetKeeperData_NoDataImported_Throws()
    {
        var config = new FakeConfigStore { Teams = new List<Team> { new("b-squared", "B Squared", "1111") } };
        var store = new FakeKeepersDataStore();
        var service = new KeepersService(store, config);

        Assert.Throws<NotFoundException>(() => service.GetKeeperData("b-squared", canEdit: true));
    }

    [Fact]
    public void UpdateKeeperData_ValidSubmission_SavesAndReturnsUpdatedRows()
    {
        var (_, store, service) = Build();
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("New Guy", 1, 10, 2),
                new("", null, null, null)
            },
            new List<int>());

        var result = service.UpdateKeeperData("b-squared", submission);

        Assert.Equal("New Guy", result.NewContracts[0].Player);
        Assert.Equal("New Guy", store.Data!.Teams["b-squared"].NewContracts[0].Player);
    }

    [Fact]
    public void UpdateKeeperData_BumpsLastUpdatedUtc()
    {
        var (_, store, service) = Build();
        var before = store.Data!.LastUpdatedUtc;
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("New Guy", 1, 10, 2),
                new("", null, null, null)
            },
            new List<int>());

        service.UpdateKeeperData("b-squared", submission);

        Assert.True(store.Data!.LastUpdatedUtc > before);
    }

    [Fact]
    public void UpdateKeeperData_InvalidContractType_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("New Guy", 3, 10, 2),
                new("", null, null, null)
            },
            new List<int>());

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Fact]
    public void UpdateKeeperData_WrongRowCount_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow> { new("New Guy", 1, 10, 2) }, new List<int>());

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
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new(playerName, 1, 10, 2),
                new("", null, null, null)
            },
            new List<int>());

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Fact]
    public void UpdateKeeperData_UnknownTeam_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>(), new List<int>());

        Assert.Throws<NotFoundException>(() => service.UpdateKeeperData("nobody", submission));
    }

    [Fact]
    public void UpdateKeeperData_DeletedExistingContractIndex_MarksDeletedAndPersists()
    {
        var (_, store, service) = Build();
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("T. Story", 1, 14, 2),
                new("", null, null, null)
            },
            new List<int> { 0 });

        var result = service.UpdateKeeperData("b-squared", submission);

        Assert.True(result.ExistingContracts[0].Deleted);
        Assert.False(result.ExistingContracts[1].Deleted);
        Assert.Equal("Jasson Dominguez", result.ExistingContracts[0].Player);
        Assert.Equal(3, result.ExistingContracts[0].LastYearSalary);
        Assert.True(store.Data!.Teams["b-squared"].ExistingContracts[0].Deleted);
    }

    [Fact]
    public void UpdateKeeperData_ResubmitWithoutPreviouslyDeletedIndex_UndeletesIt()
    {
        var (_, store, service) = Build();
        var deleteSubmission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("T. Story", 1, 14, 2),
                new("", null, null, null)
            },
            new List<int> { 0 });
        service.UpdateKeeperData("b-squared", deleteSubmission);

        var undeleteSubmission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("T. Story", 1, 14, 2),
                new("", null, null, null)
            },
            new List<int>());
        var result = service.UpdateKeeperData("b-squared", undeleteSubmission);

        Assert.False(result.ExistingContracts[0].Deleted);
        Assert.False(store.Data!.Teams["b-squared"].ExistingContracts[0].Deleted);
    }

    [Fact]
    public void UpdateKeeperData_DeletedExistingContractIndexOutOfRange_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("T. Story", 1, 14, 2),
                new("", null, null, null)
            },
            new List<int> { 99 });

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    /// <summary>
    /// Instruments the load -> save window so a test can tell whether two callers were ever
    /// inside it at the same time (which is what causes a lost update).
    /// </summary>
    private class InterleaveDetectingStore : IKeepersDataStore
    {
        private int _insideLoadToSaveWindow;

        public KeepersData? Data { get; set; }
        public byte[]? Workbook { get; set; }
        public bool InterleavingDetected { get; private set; }

        public KeepersData? LoadData()
        {
            if (Interlocked.Increment(ref _insideLoadToSaveWindow) > 1)
            {
                InterleavingDetected = true;
            }
            Thread.Sleep(25);
            return Data;
        }

        public void SaveData(KeepersData data)
        {
            Data = data;
            Interlocked.Decrement(ref _insideLoadToSaveWindow);
        }

        public void SaveWorkbook(byte[] bytes) => Workbook = bytes;
        public byte[]? LoadWorkbook() => Workbook;
    }

    [Fact]
    public void UpdateKeeperData_ConcurrentCallers_DoNotInterleaveReadModifyWrite()
    {
        var config = new FakeConfigStore { Teams = new List<Team> { new("b-squared", "B Squared", "1111") } };
        var store = new InterleaveDetectingStore
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
                        new List<int> { 8 },
                        new List<KeeperRow> { new("T. Story", 1, 14, 2) },
                        new List<int>(),
                        new List<ExistingContractRow>())
                })
        };
        var service = new KeepersService(store, config);

        Parallel.For(0, 8, i =>
        {
            var submission = new KeeperSubmission(new List<KeeperRow> { new($"Player {i}", 1, 10, 2) }, new List<int>());
            service.UpdateKeeperData("b-squared", submission);
        });

        Assert.False(store.InterleavingDetected, "Two callers were inside the load->save window at once.");
        Assert.StartsWith("Player ", store.Data!.Teams["b-squared"].NewContracts[0].Player);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeepersServiceTests`
Expected: build error — `GetKeeperData`/`KeeperSubmission`/`KeeperTeamData` don't have the new shapes yet.

- [ ] **Step 3: Update `KeeperTeamData` and `KeeperSubmission`**

Replace the contents of `backend/FantasyKeeper.Api/Models/KeeperTeamData.cs`:

```csharp
namespace FantasyKeeper.Api.Models;

public record KeeperTeamData(string TeamName, bool CanEdit, IReadOnlyList<ExistingContractRow> ExistingContracts, IReadOnlyList<KeeperRow> NewContracts);
```

Replace the contents of `backend/FantasyKeeper.Api/Models/KeeperSubmission.cs`:

```csharp
namespace FantasyKeeper.Api.Models;

public record KeeperSubmission(IReadOnlyList<KeeperRow> NewContracts, IReadOnlyList<int> DeletedExistingContractIndices);
```

- [ ] **Step 4: Update `KeepersService`**

Replace the full contents of `backend/FantasyKeeper.Api/Services/KeepersService.cs`:

```csharp
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class KeepersService
{
    private readonly IKeepersDataStore _store;
    private readonly IConfigStore _configStore;

    // Serializes UpdateKeeperData's load -> validate -> mutate -> save sequence. Without it,
    // two owners saving near-simultaneously would each load the same snapshot and the second
    // save would silently clobber the first (lost update).
    private readonly object _lock = new();

    public KeepersService(IKeepersDataStore store, IConfigStore configStore)
    {
        _store = store;
        _configStore = configStore;
    }

    public KeeperTeamData GetKeeperData(string teamId, bool canEdit)
    {
        var team = FindTeam(teamId);
        var stored = FindStoredTeam(teamId);
        return new KeeperTeamData(team.Name, canEdit, stored.ExistingContracts, stored.NewContracts);
    }

    public KeeperTeamData UpdateKeeperData(string teamId, KeeperSubmission submission)
    {
        var team = FindTeam(teamId);

        lock (_lock)
        {
            var data = _store.LoadData() ?? throw new NotFoundException("No keeper data has been imported yet.");
            if (!data.Teams.TryGetValue(teamId, out var stored))
            {
                throw new NotFoundException($"No keeper data found for team '{teamId}'.");
            }

            var errors = ValidateSubmission(submission, stored.NewContractsRows.Count, stored.ExistingContracts.Count);
            if (errors.Count > 0)
            {
                throw new KeeperValidationException(errors);
            }

            var deletedIndices = submission.DeletedExistingContractIndices.ToHashSet();
            var updatedExisting = stored.ExistingContracts
                .Select((row, i) => row with { Deleted = deletedIndices.Contains(i) })
                .ToList();

            var updatedStored = stored with { NewContracts = submission.NewContracts, ExistingContracts = updatedExisting };
            var updatedTeams = new Dictionary<string, StoredTeamKeepers>(data.Teams) { [teamId] = updatedStored };
            var updatedData = data with { Teams = updatedTeams, LastUpdatedUtc = DateTimeOffset.UtcNow };
            _store.SaveData(updatedData);

            return new KeeperTeamData(team.Name, true, updatedStored.ExistingContracts, updatedStored.NewContracts);
        }
    }

    private static List<string> ValidateSubmission(KeeperSubmission submission, int expectedRows, int existingContractsCount)
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

        foreach (var index in submission.DeletedExistingContractIndices)
        {
            if (index < 0 || index >= existingContractsCount)
            {
                errors.Add($"Existing contract index {index} is out of range.");
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
Expected: 16/16 PASS (12 `[Fact]` methods plus the 4 `[InlineData]` cases on `UpdateKeeperData_PlayerNameStartsWithFormulaChar_Throws`).

- [ ] **Step 6: Write the failing `KeeperEndpointsTests.cs`**

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

        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"},{"teamId":"ba-bombers","name":"BA Bombers","pin":"2222"}]""");

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
                    new List<int> { 20 },
                    new List<ExistingContractRow>
                    {
                        new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                    }),
                ["ba-bombers"] = new StoredTeamKeepers(
                    "BA Bombers",
                    30,
                    new List<int> { 31 },
                    new List<KeeperRow> { new("", null, null, null) },
                    new List<int>(),
                    new List<ExistingContractRow>())
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
    public async Task GetKeepers_OwnTeam_ReturnsCanEditTrue()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=1111&teamId=b-squared");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("B Squared", data!.TeamName);
        Assert.True(data.CanEdit);
    }

    [Fact]
    public async Task GetKeepers_OtherTeam_ReturnsCanEditFalse()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=1111&teamId=ba-bombers");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("BA Bombers", data!.TeamName);
        Assert.False(data.CanEdit);
    }

    [Fact]
    public async Task GetKeepers_AdminAnyTeam_ReturnsCanEditTrue()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=9999&teamId=ba-bombers");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.True(data!.CanEdit);
    }

    [Fact]
    public async Task GetKeepers_WithInvalidPin_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=0000&teamId=b-squared");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetKeepers_MissingTeamId_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=1111");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_WithInvalidContractType_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(
            Enumerable.Range(0, 6)
                .Select(i => i == 0 ? new KeeperRow("New Guy", 3, 10, 2) : new KeeperRow("", null, null, null))
                .ToList(),
            new List<int>());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111&teamId=b-squared", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_ValidSubmission_PersistsAndReturnsUpdatedData()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(
            Enumerable.Range(0, 6)
                .Select(i => i == 0 ? new KeeperRow("New Guy", 1, 10, 2) : new KeeperRow("", null, null, null))
                .ToList(),
            new List<int>());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111&teamId=b-squared", payload);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("New Guy", data!.NewContracts[0].Player);
    }

    [Fact]
    public async Task PutKeepers_OtherTeam_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(new List<KeeperRow> { new("", null, null, null) }, new List<int>());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111&teamId=ba-bombers", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_AdminOtherTeam_Succeeds()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(new List<KeeperRow> { new("Admin Pick", 1, 5, 1) }, new List<int>());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=9999&teamId=ba-bombers", payload);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("Admin Pick", data!.NewContracts[0].Player);
    }

    [Fact]
    public async Task GetTeams_WithOwnerPin_ReturnsTeams()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/teams?pin=1111");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetArrayLength());
    }

    [Fact]
    public async Task GetTeams_WithAdminPin_ReturnsTeams()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/teams?pin=9999");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetArrayLength());
    }
}
```

- [ ] **Step 7: Run the tests to verify they fail**

Run: `dotnet test backend/FantasyKeeper.Api.Tests --filter KeeperEndpointsTests`
Expected: FAIL/build error — the endpoint still expects the old shape and there's no `/api/teams` route.

- [ ] **Step 8: Rewrite `KeeperEndpoints.cs`**

Replace the full contents of `backend/FantasyKeeper.Api/Endpoints/KeeperEndpoints.cs`:

```csharp
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class KeeperEndpoints
{
    public static void MapKeeperEndpoints(this WebApplication app)
    {
        app.MapGet("/api/keepers", (string pin, string teamId, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null) return Results.Unauthorized();

            var canEdit = auth.Role == AuthRole.Admin || teamId == auth.TeamId;

            try
            {
                return Results.Ok(keepersService.GetKeeperData(teamId, canEdit));
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPut("/api/keepers", (string pin, string teamId, KeeperSubmission submission, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null) return Results.Unauthorized();

            var canEdit = auth.Role == AuthRole.Admin || teamId == auth.TeamId;
            if (!canEdit)
            {
                return Results.Json(new { error = "You don't have permission to edit this team." }, statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                return Results.Ok(keepersService.UpdateKeeperData(teamId, submission));
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

        app.MapGet("/api/teams", (string pin, AuthService authService, IConfigStore configStore) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null) return Results.Unauthorized();

            var teams = configStore.GetTeams().Select(t => new { teamId = t.TeamId, name = t.Name });
            return Results.Ok(teams);
        });
    }
}
```

- [ ] **Step 9: Remove `/api/admin/teams` from `AdminKeepersEndpoints.cs`**

In `backend/FantasyKeeper.Api/Endpoints/AdminKeepersEndpoints.cs`, remove this block entirely (it's superseded by `/api/teams`):

```csharp
        app.MapGet("/api/admin/teams", (string pin, AuthService authService, IConfigStore configStore) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            var teams = configStore.GetTeams().Select(t => new { teamId = t.TeamId, name = t.Name });
            return Results.Ok(teams);
        });

```

(leave everything else in that file untouched — the import/confirm/export/status routes are unaffected by this task).

- [ ] **Step 10: Fix `AdminKeepersEndpointsTests.cs`**

In `backend/FantasyKeeper.Api.Tests/AdminKeepersEndpointsTests.cs`, replace:

```csharp
        var keepersResponse = await client.GetAsync("/api/keepers?pin=1111");
```

with:

```csharp
        var keepersResponse = await client.GetAsync("/api/keepers?pin=1111&teamId=b-squared");
```

Then remove this test entirely (its route no longer exists — replaced by `KeeperEndpointsTests.GetTeams_WithAdminPin_ReturnsTeams` from Step 6 above):

```csharp
    [Fact]
    public async Task GetAdminTeams_WithAdminPin_ReturnsTeams()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/teams?pin=9999");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("b-squared", json[0].GetProperty("teamId").GetString());
    }
```

- [ ] **Step 11: Build and run the full backend test suite**

Run: `dotnet build backend/FantasyKeeper.sln` then `dotnet test backend/FantasyKeeper.Api.Tests`
Expected: builds clean with 0 warnings/errors, all tests PASS.

- [ ] **Step 12: Commit**

```bash
git add backend/FantasyKeeper.Api/Models/KeeperTeamData.cs backend/FantasyKeeper.Api/Models/KeeperSubmission.cs backend/FantasyKeeper.Api/Services/KeepersService.cs backend/FantasyKeeper.Api/Endpoints/KeeperEndpoints.cs backend/FantasyKeeper.Api/Endpoints/AdminKeepersEndpoints.cs backend/FantasyKeeper.Api.Tests/KeepersServiceTests.cs backend/FantasyKeeper.Api.Tests/KeeperEndpointsTests.cs backend/FantasyKeeper.Api.Tests/AdminKeepersEndpointsTests.cs
git commit -m "Add cross-team authorization, /api/teams, and existing-contract deletion"
```

---

## Task 3: Team navigation sidebar and generic team page

**Files:**
- Modify: `frontend/src/types.ts`
- Modify: `frontend/src/api/client.ts`
- Create: `frontend/src/components/Sidebar.tsx`
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/screens/AdminPanel.tsx`
- Modify: `frontend/src/screens/KeeperFormScreen.tsx`

**Interfaces:**
- Consumes: `KeeperTeamData.canEdit`, `ExistingContractRow.deleted` (Task 2's response shape), `GET /api/teams`, `GET/PUT /api/keepers?teamId=...` (Task 2).
- Produces: `Sidebar({ teams, myTeamId, isAdmin, activeTeamId, onSelectTeam, onSelectAdminPanel })`, `KeeperFormScreen({ pin, teamId })` (adds the required `teamId` prop it didn't have before), `getTeams(pin): Promise<TeamSummary[]>` (replaces `getAdminTeams`).

No automated frontend test suite exists in this project (matches its existing convention) — verification is `npm run build --prefix frontend` plus manual checks via the dev server.

- [ ] **Step 1: Update `types.ts`**

In `frontend/src/types.ts`, replace:

```ts
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
```

with:

```ts
export interface ExistingContractRow {
  player: string;
  contractInfo: string;
  lastYearSalary: number | null;
  leagueValue: number | null;
  thisYearSalary: number | null;
  deleted: boolean;
}

export interface KeeperTeamData {
  teamName: string;
  canEdit: boolean;
  existingContracts: ExistingContractRow[];
  newContracts: KeeperRow[];
}
```

- [ ] **Step 2: Update `api/client.ts`**

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

export function getKeepers(pin: string, teamId: string): Promise<KeeperTeamData> {
  return request<KeeperTeamData>(`/api/keepers?pin=${encodeURIComponent(pin)}&teamId=${encodeURIComponent(teamId)}`);
}

export function updateKeepers(
  pin: string,
  teamId: string,
  newContracts: KeeperRow[],
  deletedExistingContractIndices: number[]
): Promise<KeeperTeamData> {
  return request<KeeperTeamData>(`/api/keepers?pin=${encodeURIComponent(pin)}&teamId=${encodeURIComponent(teamId)}`, {
    method: "PUT",
    body: JSON.stringify({ newContracts, deletedExistingContractIndices })
  });
}

export function getTeams(pin: string): Promise<TeamSummary[]> {
  return request<TeamSummary[]>(`/api/teams?pin=${encodeURIComponent(pin)}`);
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

- [ ] **Step 3: Create the `Sidebar` component**

Create `frontend/src/components/Sidebar.tsx`:

```tsx
import type { TeamSummary } from "../types";

interface Props {
  teams: TeamSummary[];
  myTeamId: string | null;
  isAdmin: boolean;
  activeTeamId: string | null;
  onSelectTeam: (teamId: string) => void;
  onSelectAdminPanel: () => void;
}

export function Sidebar({ teams, myTeamId, isAdmin, activeTeamId, onSelectTeam, onSelectAdminPanel }: Props) {
  return (
    <nav style={{ width: 200, flexShrink: 0, borderRight: "1px solid #ccc", padding: "0.5rem" }}>
      {isAdmin && (
        <button
          onClick={onSelectAdminPanel}
          style={{
            display: "block",
            width: "100%",
            textAlign: "left",
            fontWeight: activeTeamId === null ? "bold" : "normal"
          }}
        >
          Admin Panel
        </button>
      )}
      {teams.map((team) => (
        <button
          key={team.teamId}
          onClick={() => onSelectTeam(team.teamId)}
          style={{
            display: "block",
            width: "100%",
            textAlign: "left",
            fontWeight: activeTeamId === team.teamId ? "bold" : "normal"
          }}
        >
          {team.name}
          {team.teamId === myTeamId ? " (My Team)" : ""}
        </button>
      ))}
    </nav>
  );
}
```

- [ ] **Step 4: Restructure `App.tsx`**

Replace the full contents of `frontend/src/App.tsx`:

```tsx
import { useEffect, useState } from "react";
import { useAuth } from "./state/useAuth";
import { PinEntryScreen } from "./screens/PinEntryScreen";
import { KeeperFormScreen } from "./screens/KeeperFormScreen";
import { AdminPanel } from "./screens/AdminPanel";
import { Sidebar } from "./components/Sidebar";
import { getTeams } from "./api/client";
import type { TeamSummary } from "./types";

type View = { kind: "team"; teamId: string } | { kind: "admin" };

export default function App() {
  const { pin, auth, login, logout, error, isLoading } = useAuth();
  const [teams, setTeams] = useState<TeamSummary[]>([]);
  const [view, setView] = useState<View | null>(null);

  useEffect(() => {
    if (!pin) return;
    getTeams(pin).then(setTeams).catch(() => setTeams([]));
  }, [pin]);

  if (!pin || !auth) {
    return <PinEntryScreen onSubmit={login} error={error} isLoading={isLoading} />;
  }

  const isAdmin = auth.role === "Admin";
  const effectiveView: View = view ?? (isAdmin ? { kind: "admin" } : { kind: "team", teamId: auth.teamId! });

  return (
    <div style={{ display: "flex", minHeight: "100vh" }}>
      <Sidebar
        teams={teams}
        myTeamId={auth.teamId}
        isAdmin={isAdmin}
        activeTeamId={effectiveView.kind === "team" ? effectiveView.teamId : null}
        onSelectTeam={(teamId) => setView({ kind: "team", teamId })}
        onSelectAdminPanel={() => setView({ kind: "admin" })}
      />
      <div style={{ flex: 1, padding: "1rem" }}>
        <button onClick={logout}>Log out</button>
        {effectiveView.kind === "admin" ? (
          <AdminPanel pin={pin} />
        ) : (
          <KeeperFormScreen pin={pin} teamId={effectiveView.teamId} />
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Switch `AdminPanel.tsx` to `getTeams`**

In `frontend/src/screens/AdminPanel.tsx`, replace:

```tsx
import { getAdminTeams, importKeepers, confirmImport, getKeepersStatus, exportKeepers } from "../api/client";
```

with:

```tsx
import { getTeams, importKeepers, confirmImport, getKeepersStatus, exportKeepers } from "../api/client";
```

and replace:

```tsx
    getAdminTeams(pin).then(setTeams).catch(() => setTeams([]));
```

with:

```tsx
    getTeams(pin).then(setTeams).catch(() => setTeams([]));
```

- [ ] **Step 6: Type-check**

Run: `npm run build --prefix frontend`
Expected: FAILS only in `KeeperFormScreen.tsx` (it doesn't accept a `teamId` prop yet, and its `getKeepers`/`updateKeepers` calls are missing the new required arguments). Confirm no errors in `types.ts`, `api/client.ts`, `components/Sidebar.tsx`, `App.tsx`, or `AdminPanel.tsx`.

- [ ] **Step 7: Rewrite `KeeperFormScreen.tsx`**

Replace the full contents of `frontend/src/screens/KeeperFormScreen.tsx`:

```tsx
import { useCallback, useEffect, useState } from "react";
import { getKeepers, updateKeepers, ApiError } from "../api/client";
import type { KeeperRow, KeeperTeamData } from "../types";

interface Props {
  pin: string;
  teamId: string;
}

export function KeeperFormScreen({ pin, teamId }: Props) {
  const [data, setData] = useState<KeeperTeamData | null>(null);
  const [rows, setRows] = useState<KeeperRow[]>([]);
  const [deletedIndices, setDeletedIndices] = useState<Set<number>>(new Set());
  const [deletedExistingIndices, setDeletedExistingIndices] = useState<Set<number>>(new Set());
  const [status, setStatus] = useState<"idle" | "loading" | "saving" | "error">("loading");
  const [message, setMessage] = useState<string | null>(null);

  const loadKeepers = useCallback(async () => {
    setStatus("loading");
    setMessage(null);
    try {
      const result = await getKeepers(pin, teamId);
      setData(result);
      setRows(result.newContracts);
      setDeletedIndices(new Set());
      setDeletedExistingIndices(
        new Set(result.existingContracts.flatMap((row, i) => (row.deleted ? [i] : [])))
      );
      setStatus("idle");
    } catch {
      setStatus("error");
      setMessage("Couldn't load this team's keepers. Try again.");
    }
  }, [pin, teamId]);

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

  function toggleDeleteExisting(index: number) {
    setDeletedExistingIndices((prev) => {
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
      const result = await updateKeepers(pin, teamId, submission, Array.from(deletedExistingIndices));
      setData(result);
      setRows(result.newContracts);
      setDeletedIndices(new Set());
      setDeletedExistingIndices(
        new Set(result.existingContracts.flatMap((row, i) => (row.deleted ? [i] : [])))
      );
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
          <tr>
            <th>Player</th><th>Contract</th><th>Last Year</th><th>League Value</th><th>This Year</th>
            {data.canEdit && <th>Delete</th>}
          </tr>
        </thead>
        <tbody>
          {data.existingContracts.map((row, i) => {
            const isDeleted = deletedExistingIndices.has(i);
            const rowStyle = isDeleted ? { textDecoration: "line-through", opacity: 0.5 } : undefined;
            return (
              <tr key={i} style={rowStyle}>
                <td>{row.player}</td>
                <td>{row.contractInfo}</td>
                <td>{row.lastYearSalary ?? ""}</td>
                <td>{row.leagueValue ?? ""}</td>
                <td>{row.thisYearSalary ?? ""}</td>
                {data.canEdit && (
                  <td>
                    <input
                      type="checkbox"
                      checked={isDeleted}
                      onChange={() => toggleDeleteExisting(i)}
                      aria-label={`Delete existing contract for row ${i + 1}`}
                    />
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>

      <h2>New Contracts</h2>
      <table>
        <thead>
          <tr>
            <th>Player</th><th>Contract 1 or 2</th><th>Salary</th><th>Keeper Years</th>
            {data.canEdit && <th>Delete</th>}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => {
            const isDeleted = deletedIndices.has(i);
            const rowStyle = isDeleted ? { textDecoration: "line-through", opacity: 0.5 } : undefined;
            return (
              <tr key={i} style={rowStyle}>
                <td>
                  <input
                    style={{ width: "100%", boxSizing: "border-box" }}
                    value={row.player}
                    disabled={!data.canEdit}
                    onChange={(e) => updateRow(i, "player", e.target.value)}
                  />
                </td>
                <td>
                  <select
                    style={{ width: "100%", boxSizing: "border-box" }}
                    value={row.contractType ?? ""}
                    disabled={!data.canEdit}
                    onChange={(e) => updateRow(i, "contractType", e.target.value)}
                  >
                    <option value="">--</option>
                    <option value="1">1</option>
                    <option value="2">2</option>
                  </select>
                </td>
                <td>
                  <input
                    style={{ width: "100%", boxSizing: "border-box" }}
                    value={row.salary ?? ""}
                    disabled={!data.canEdit}
                    onChange={(e) => updateRow(i, "salary", e.target.value)}
                  />
                </td>
                <td>
                  <input
                    style={{ width: "100%", boxSizing: "border-box" }}
                    value={row.keeperYears ?? ""}
                    disabled={!data.canEdit}
                    onChange={(e) => updateRow(i, "keeperYears", e.target.value)}
                  />
                </td>
                {data.canEdit && (
                  <td>
                    <input
                      type="checkbox"
                      checked={isDeleted}
                      onChange={() => toggleDelete(i)}
                      aria-label={`Delete contract for row ${i + 1}`}
                    />
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>

      {data.canEdit && (
        <button onClick={handleSave} disabled={status === "saving"}>
          {status === "saving" ? "Saving..." : "Save Keepers"}
        </button>
      )}
      {message && <p role="status">{message}</p>}
    </div>
  );
}
```

- [ ] **Step 8: Type-check**

Run: `npm run build --prefix frontend`
Expected: PASSES with 0 errors across the whole frontend.

- [ ] **Step 9: Manual verification**

Run `dotnet run --project backend/FantasyKeeper.Api` and `npm run dev --prefix frontend`. Import a small xlsx with at least 2 teams through the Admin panel first (or reuse existing imported data if present), then:
- [ ] Log in as a team owner: confirm the sidebar lists every team, the owner's own team is labeled "(My Team)" and pre-selected, editing works there (New Contracts inputs enabled, Save button present, both delete-checkbox columns present).
- [ ] Click into a different team from the sidebar: confirm it loads read-only — every New Contracts input disabled, no delete-checkbox columns, no Save button.
- [ ] Log in as admin: confirm the sidebar defaults to "Admin Panel," and clicking any team loads it fully editable (inputs enabled, both delete-checkbox columns present, Save button present) regardless of which team it is.
- [ ] On an editable team, check an Existing Contract's delete checkbox, confirm it strikes through immediately without changing its values, save, confirm it stays struck-through and checked after reload. Uncheck it and save again, confirm it's no longer struck through.
- [ ] Export the data from the Admin panel and open the file: confirm the deleted existing contract's row shows strikethrough formatting on its Player/Contract#/Last year/League value/This year cells, with the values still present (not blanked), and that a still-active existing contract in the same team has no strikethrough.

- [ ] **Step 10: Commit**

```bash
git add frontend/src/types.ts frontend/src/api/client.ts frontend/src/components/Sidebar.tsx frontend/src/App.tsx frontend/src/screens/AdminPanel.tsx frontend/src/screens/KeeperFormScreen.tsx
git commit -m "Add team navigation sidebar and existing-contract deletion to the team page"
```
