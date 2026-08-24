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

        Assert.Equal("2026 Keepers", preview.SheetName);
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
    public void Export_AfterConfirmedImport_ContainsStoredNewContractsAtMappedCells()
    {
        var (_, store, service) = Build();
        var bytes = BuildWorkbook("B Squared");
        service.StartImport(bytes, "keepers.xlsx");
        var data = service.ConfirmImport(new List<BlockAssignment> { new(0, "b-squared") });

        // Simulate a team owner editing their keepers after import, the way
        // KeepersService.UpdateKeeperData would.
        var stored = data.Teams["b-squared"];
        var edited = new List<KeeperRow> { new("Edited Player", 2, 17.5m, 3) };
        for (var i = edited.Count; i < stored.NewContractsRows.Count; i++)
        {
            edited.Add(new KeeperRow("", null, null, null));
        }
        var updatedStored = stored with { NewContracts = edited };
        store.Data = data with
        {
            Teams = new Dictionary<string, StoredTeamKeepers> { ["b-squared"] = updatedStored }
        };

        var exported = service.Export();

        Assert.NotEmpty(exported);
        using var ms = new MemoryStream(exported);
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheet(data.SheetName);

        for (var i = 0; i < updatedStored.NewContractsRows.Count; i++)
        {
            var row = updatedStored.NewContractsRows[i];
            var expected = updatedStored.NewContracts[i];

            Assert.Equal(expected.Player, sheet.Cell(row, "C").GetString());
            Assert.Equal(expected.ContractType?.ToString() ?? "", sheet.Cell(row, "D").GetString());
            Assert.Equal(expected.Salary?.ToString() ?? "", sheet.Cell(row, "E").GetString());
            Assert.Equal(expected.KeeperYears?.ToString() ?? "", sheet.Cell(row, "F").GetString());
        }
    }
}
