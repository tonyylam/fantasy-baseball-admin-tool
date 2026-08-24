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
    public void Parse_SingleTeam_ExistingContractsRowsMatchExistingContractsCount()
    {
        var bytes = BuildWorkbook(sheet => WriteTeamBlock(sheet, teamNameRow: 6, teamName: "B Squared"));

        using var ms = new MemoryStream(bytes);
        var parsed = KeeperWorkbookParser.Parse(ms);

        var team = parsed.Teams[0];
        Assert.Equal(team.ExistingContractsRows.Count, team.ExistingContracts.Count);
        Assert.Equal(8, team.ExistingContractsRows[0]);
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
    public void Parse_LastTeamFollowedByFooterContent_DoesNotSwallowFooterRows()
    {
        // Regression: the last detected team's block used to run all the way to the
        // sheet's LastRowUsed(), swallowing footers/notes/totals into its editable
        // New Contracts slots — which the writer then cleared on export.
        var bytes = BuildWorkbook(sheet =>
        {
            WriteTeamBlock(sheet, teamNameRow: 6, teamName: "B Squared");
            WriteTeamBlock(sheet, teamNameRow: 20, teamName: "BA Bombers");

            // Trailing footer / notes content well past the last team's own data,
            // with irregular gaps between it and the last block.
            sheet.Cell(55, "A").Value = "League notes";
            sheet.Cell(59, "C").Value = "Grand total";
            sheet.Cell(58, "H").Value = "Footer note";
            sheet.Cell(60, "A").Value = "Totals";
            sheet.Cell(60, "E").FormulaA1 = "=SUM(E22:E32)";
        });

        using var ms = new MemoryStream(bytes);
        var parsed = KeeperWorkbookParser.Parse(ms);

        Assert.Equal(2, parsed.Teams.Count);
        var first = parsed.Teams[0];
        var last = parsed.Teams[1];

        // The last block must be capped near the height of the other detected block,
        // not extended down to the sheet's last used row.
        Assert.Equal(first.NewContractsRows.Count, last.NewContractsRows.Count);
        Assert.All(last.NewContractsRows, row => Assert.True(row < 55, $"Row {row} reaches into the footer region."));
        Assert.DoesNotContain(last.NewContracts, c => c.Player == "Grand total");
        Assert.DoesNotContain(last.ExistingContracts, c => c.Player == "Footer note");
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
