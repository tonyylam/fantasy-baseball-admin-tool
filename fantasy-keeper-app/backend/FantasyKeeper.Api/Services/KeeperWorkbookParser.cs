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

                // The last team has no following anchor to bound it, and LastRowUsed() spans the
                // whole sheet — using it would swallow footers/notes/totals below the last block
                // into that team's editable New Contracts slots (which the writer then clears).
                // Bound it by the tallest of the other detected blocks instead.
                var blockHeights = new List<int>();
                for (var j = 0; j < anchorRows.Count - 1; j++)
                {
                    var jStart = anchorRows[j] + 1;
                    var jEnd = anchorRows[j + 1] - 2;
                    blockHeights.Add(jEnd - jStart + 1);
                }
                const int FallbackMaxBlockHeight = 30; // used only when there's a single team and no other block to size against
                var maxOtherBlockHeight = blockHeights.Count > 0 ? blockHeights.Max() : FallbackMaxBlockHeight;

                var teams = new List<StoredTeamKeepers>();

                for (var i = 0; i < anchorRows.Count; i++)
                {
                    var headerRow = anchorRows[i];
                    var teamNameRow = headerRow - 1;
                    var rawName = worksheet.Cell(teamNameRow, "A").GetString().Trim();

                    var startDataRow = headerRow + 1;
                    var endDataRow = i + 1 < anchorRows.Count
                        ? anchorRows[i + 1] - 2
                        : Math.Min(lastRow, startDataRow + maxOtherBlockHeight - 1);

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
