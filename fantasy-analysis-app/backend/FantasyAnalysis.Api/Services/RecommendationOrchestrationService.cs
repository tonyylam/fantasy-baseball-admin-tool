using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class RecommendationOrchestrationService
{
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);

    private readonly ILeagueDataStore _leagueStore;
    private readonly IScoringSettingsStore _settingsStore;
    private readonly IStatsProvider _statsProvider;
    private readonly IStatsCache _statsCache;
    private readonly WaiverPoolCalculator _waiverPoolCalculator;
    private readonly RotoStandingsCalculator _standingsCalculator;
    private readonly WeakCategoryWaiverShortlist _shortlistBuilder;
    private readonly ClaudeRecommendationEngine _engine;
    private readonly IRecommendationDataStore _recommendationStore;

    public RecommendationOrchestrationService(
        ILeagueDataStore leagueStore,
        IScoringSettingsStore settingsStore,
        IStatsProvider statsProvider,
        IStatsCache statsCache,
        WaiverPoolCalculator waiverPoolCalculator,
        RotoStandingsCalculator standingsCalculator,
        WeakCategoryWaiverShortlist shortlistBuilder,
        ClaudeRecommendationEngine engine,
        IRecommendationDataStore recommendationStore)
    {
        _leagueStore = leagueStore;
        _settingsStore = settingsStore;
        _statsProvider = statsProvider;
        _statsCache = statsCache;
        _waiverPoolCalculator = waiverPoolCalculator;
        _standingsCalculator = standingsCalculator;
        _shortlistBuilder = shortlistBuilder;
        _engine = engine;
        _recommendationStore = recommendationStore;
    }

    public async Task<RecommendationSet> RefreshAsync(string yourTeamName)
    {
        var league = _leagueStore.LoadLeague()
            ?? throw new RecommendationPrerequisiteException("A league must be imported before generating recommendations.");
        var settings = _settingsStore.Load()
            ?? throw new RecommendationPrerequisiteException("Scoring settings must be saved before generating recommendations.");

        var season = SeasonClock.Current;
        var allPlayers = await _statsProvider.GetAllActivePlayersAsync(season);
        var waiverPool = _waiverPoolCalculator.ComputeWaiverPool(allPlayers, league);

        var statLines = _statsCache.GetIfFresh(season, CacheMaxAge);
        if (statLines is null)
        {
            var rosteredIds = league.Teams.SelectMany(t => t.Players).Select(p => p.PlayerId);
            var idsNeeded = rosteredIds.Concat(waiverPool.Select(p => p.Id)).Distinct().ToList();
            statLines = await _statsProvider.GetPlayerStatsAsync(idsNeeded, season);
            _statsCache.Store(season, statLines);
        }

        var statsByPlayerId = statLines
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StatLine>)g.ToList());

        var standings = _standingsCalculator.ComputeStandings(league, settings.HittingCategoryKeys, settings.PitchingCategoryKeys, statsByPlayerId);
        var shortlist = _shortlistBuilder.ShortlistForWeakCategories(standings, yourTeamName, waiverPool, statsByPlayerId);

        var recommendations = await _engine.GenerateRecommendationsAsync(league, yourTeamName, standings, shortlist, statsByPlayerId);
        _recommendationStore.Save(recommendations);
        return recommendations;
    }

    public RecommendationSet? GetLast() => _recommendationStore.Load();
}
