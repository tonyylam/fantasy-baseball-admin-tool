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
