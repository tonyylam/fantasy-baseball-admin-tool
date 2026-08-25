using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RosterCsvParserTests
{
    [Fact]
    public void Parse_GroupsPlayersByTeamInFileOrder()
    {
        var csv = "Team,Player\nRhino Wranglers,Shohei Ohtani\nRhino Wranglers,Mookie Betts\nSea Dogs,Ronald Acuna Jr.\n";

        var result = new RosterCsvParser().Parse(csv);

        Assert.Equal(2, result.Teams.Count);
        Assert.Equal("Rhino Wranglers", result.Teams[0].TeamName);
        Assert.Equal(new[] { "Shohei Ohtani", "Mookie Betts" }, result.Teams[0].PlayerNames);
        Assert.Equal("Sea Dogs", result.Teams[1].TeamName);
        Assert.Equal(new[] { "Ronald Acuna Jr." }, result.Teams[1].PlayerNames);
    }

    [Fact]
    public void Parse_MissingHeader_ThrowsCsvParseException()
    {
        var csv = "Rhino Wranglers,Shohei Ohtani\n";

        Assert.Throws<CsvParseException>(() => new RosterCsvParser().Parse(csv));
    }

    [Fact]
    public void Parse_RowWithWrongColumnCount_ThrowsCsvParseException()
    {
        var csv = "Team,Player\nRhino Wranglers,Shohei Ohtani,ExtraColumn\n";

        Assert.Throws<CsvParseException>(() => new RosterCsvParser().Parse(csv));
    }
}
