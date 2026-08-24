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
    public void WriteNewContracts_PreservesFormulasOtherTabsAndStyles()
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

            // A footer formula on the target sheet, plus a styled cell.
            sheet.Cell(20, "E").FormulaA1 = "=SUM(E8:E9)";
            sheet.Cell(20, "A").Value = "Total salary";
            sheet.Cell(20, "A").Style.Font.Bold = true;
            sheet.Cell(20, "A").Style.Fill.BackgroundColor = XLColor.Yellow;

            // A second, unrelated worksheet.
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
                new List<ExistingContractRow>())
        };

        var result = KeeperWorkbookWriter.WriteNewContracts(original, "2026 Keepers", teams);

        using var resultStream = new MemoryStream(result);
        using var reopened = new XLWorkbook(resultStream);
        var target = reopened.Worksheet("2026 Keepers");

        // The mapped cell was written.
        Assert.Equal("New Player", target.Cell(8, "C").GetString());

        // The footer formula survives, as a formula.
        Assert.True(target.Cell(20, "E").HasFormula);
        Assert.Equal("SUM(E8:E9)", target.Cell(20, "E").FormulaA1.TrimStart('='));

        // The styled cell keeps its style.
        Assert.Equal("Total salary", target.Cell(20, "A").GetString());
        Assert.True(target.Cell(20, "A").Style.Font.Bold);
        Assert.Equal(XLColor.Yellow, target.Cell(20, "A").Style.Fill.BackgroundColor);

        // The second worksheet still exists with its original content.
        Assert.Equal(2, reopened.Worksheets.Count);
        var otherSheet = reopened.Worksheet("Draft Notes");
        Assert.Equal("Keep this tab intact", otherSheet.Cell(1, "A").GetString());
        Assert.Equal(42, otherSheet.Cell(2, "B").GetValue<int>());
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
