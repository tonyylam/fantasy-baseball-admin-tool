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
