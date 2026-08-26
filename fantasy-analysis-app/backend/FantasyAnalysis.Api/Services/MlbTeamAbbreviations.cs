namespace FantasyAnalysis.Api.Services;

/// <summary>
/// MLB team abbreviation -&gt; team ID, verified against statsapi.mlb.com/api/v1/teams?sportId=1.
/// Includes each team's official abbreviation plus common alternate abbreviations used by
/// other fantasy platforms (e.g. ARI/WAS/CHW instead of MLB's own AZ/WSH/CWS).
/// </summary>
public static class MlbTeamAbbreviations
{
    public static readonly IReadOnlyDictionary<string, int> TeamIdsByAbbreviation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["LAA"] = 108,
        ["AZ"] = 109,
        ["ARI"] = 109,
        ["BAL"] = 110,
        ["BOS"] = 111,
        ["CHC"] = 112,
        ["CIN"] = 113,
        ["CLE"] = 114,
        ["COL"] = 115,
        ["DET"] = 116,
        ["HOU"] = 117,
        ["KC"] = 118,
        ["LAD"] = 119,
        ["WSH"] = 120,
        ["WAS"] = 120,
        ["NYM"] = 121,
        ["ATH"] = 133,
        ["PIT"] = 134,
        ["SD"] = 135,
        ["SEA"] = 136,
        ["SF"] = 137,
        ["STL"] = 138,
        ["TB"] = 139,
        ["TEX"] = 140,
        ["TOR"] = 141,
        ["MIN"] = 142,
        ["PHI"] = 143,
        ["ATL"] = 144,
        ["CWS"] = 145,
        ["CHW"] = 145,
        ["MIA"] = 146,
        ["NYY"] = 147,
        ["MIL"] = 158,
    };
}
