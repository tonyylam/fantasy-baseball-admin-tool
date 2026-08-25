namespace FantasyAnalysis.Api.Models;

public class CsvParseException : Exception
{
    public CsvParseException(string message) : base(message) { }
}
