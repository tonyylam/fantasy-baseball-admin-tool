using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class LeagueImportService
{
    private readonly RosterCsvParser _parser;
    private readonly IPlayerMatchingService _matcher;
    private readonly IStatsProvider _statsProvider;
    private readonly ILeagueDataStore _leagueStore;

    public LeagueImportService(
        RosterCsvParser parser,
        IPlayerMatchingService matcher,
        IStatsProvider statsProvider,
        ILeagueDataStore leagueStore)
    {
        _parser = parser;
        _matcher = matcher;
        _statsProvider = statsProvider;
        _leagueStore = leagueStore;
    }

    public async Task<ImportPreview> PreviewImportAsync(string csvContent)
    {
        var parsed = _parser.Parse(csvContent);
        var pool = await _statsProvider.GetAllActivePlayersAsync(SeasonClock.Current);

        var teamPreviews = parsed.Teams
            .Select(t => new TeamMatchPreview(t.TeamName, _matcher.MatchPlayers(t.PlayerNames, pool)))
            .ToList();

        return new ImportPreview(teamPreviews);
    }

    public League ConfirmImport(ConfirmImportRequest request)
    {
        var teams = request.Teams
            .Select(t => new TeamRoster(
                t.TeamName,
                t.Players
                    .Where(p => p.PlayerId is not null)
                    .Select(p => new RosteredPlayer(p.CsvName, p.PlayerId!, p.PlayerFullName!, p.Position!, p.IsPitcher))
                    .ToList()))
            .ToList();

        var league = new League(DateTimeOffset.UtcNow, teams);
        _leagueStore.SaveLeague(league);
        return league;
    }
}
