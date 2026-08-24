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
