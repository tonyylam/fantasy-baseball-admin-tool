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
