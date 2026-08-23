using System.Globalization;
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class KeepersService
{
    private readonly ISheetsClient _sheets;
    private readonly IConfigStore _configStore;

    public KeepersService(ISheetsClient sheets, IConfigStore configStore)
    {
        _sheets = sheets;
        _configStore = configStore;
    }

    public async Task<KeeperTeamData> GetKeeperDataAsync(string seasonId, string teamId, CancellationToken ct = default)
    {
        var season = FindSeason(seasonId);
        var team = FindTeam(teamId);
        var mapping = FindMapping(seasonId, teamId);

        var existingRaw = await _sheets.GetRangeAsync(season.GoogleSheetId, mapping.SheetTab, mapping.ExistingContractsRange, ct);
        var newRaw = await _sheets.GetRangeAsync(season.GoogleSheetId, mapping.SheetTab, mapping.NewContractsRange, ct);

        var existing = existingRaw.Select(ParseExistingRow).ToList();
        var newContracts = newRaw.Select(ParseNewRow).ToList();

        return new KeeperTeamData(team.Name, !season.IsActive, existing, newContracts);
    }

    public async Task<KeeperTeamData> UpdateKeeperDataAsync(string seasonId, string teamId, KeeperSubmission submission, CancellationToken ct = default)
    {
        var season = FindSeason(seasonId);
        if (!season.IsActive)
        {
            throw new SeasonNotActiveException(seasonId);
        }

        var team = FindTeam(teamId);
        var mapping = FindMapping(seasonId, teamId);

        var (expectedRows, expectedCols) = A1Range.GetDimensions(mapping.NewContractsRange);
        if (expectedCols != 4)
        {
            throw new InvalidOperationException(
                $"Mapping for '{teamId}' expects 4 columns (Player, Contract Type, Salary, Keeper Years) but range '{mapping.NewContractsRange}' has {expectedCols}.");
        }

        var errors = ValidateSubmission(submission, expectedRows);
        if (errors.Count > 0)
        {
            throw new KeeperValidationException(errors);
        }

        var values = submission.NewContracts
            .Select(row => (IReadOnlyList<string>)new List<string>
            {
                row.Player ?? "",
                row.ContractType?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.Salary?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.KeeperYears?.ToString(CultureInfo.InvariantCulture) ?? ""
            })
            .ToList();

        await _sheets.UpdateRangeAsync(season.GoogleSheetId, mapping.SheetTab, mapping.NewContractsRange, values, ct);

        return await GetKeeperDataAsync(seasonId, teamId, ct);
    }

    private static List<string> ValidateSubmission(KeeperSubmission submission, int expectedRows)
    {
        var errors = new List<string>();

        if (submission.NewContracts.Count != expectedRows)
        {
            errors.Add($"Expected {expectedRows} rows but received {submission.NewContracts.Count}.");
            return errors;
        }

        for (var i = 0; i < submission.NewContracts.Count; i++)
        {
            var row = submission.NewContracts[i];
            var isBlank = string.IsNullOrWhiteSpace(row.Player)
                && row.ContractType is null
                && row.Salary is null
                && row.KeeperYears is null;
            if (isBlank)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Player))
            {
                errors.Add($"Row {i + 1}: player name is required when other fields are set.");
            }
            else if (row.Player.TrimStart()[0] is '=' or '+' or '-' or '@')
            {
                errors.Add($"Row {i + 1}: player name cannot start with '=', '+', '-', or '@'.");
            }

            if (row.ContractType is not (1 or 2))
            {
                errors.Add($"Row {i + 1}: contract type must be 1 or 2.");
            }

            if (row.Salary is null || row.Salary < 0)
            {
                errors.Add($"Row {i + 1}: salary must be a non-negative number.");
            }

            if (row.KeeperYears is null || row.KeeperYears < 0)
            {
                errors.Add($"Row {i + 1}: keeper years must be a non-negative number.");
            }
        }

        return errors;
    }

    private static KeeperRow ParseNewRow(IReadOnlyList<string> cells)
    {
        string Cell(int i) => i < cells.Count ? cells[i] : "";
        return new KeeperRow(
            Cell(0),
            int.TryParse(Cell(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ct) ? ct : null,
            decimal.TryParse(Cell(2), NumberStyles.Number, CultureInfo.InvariantCulture, out var salary) ? salary : null,
            int.TryParse(Cell(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var years) ? years : null);
    }

    private static ExistingContractRow ParseExistingRow(IReadOnlyList<string> cells)
    {
        string Cell(int i) => i < cells.Count ? cells[i] : "";
        return new ExistingContractRow(
            Cell(0),
            Cell(1),
            decimal.TryParse(Cell(2), NumberStyles.Number, CultureInfo.InvariantCulture, out var lastYear) ? lastYear : null,
            decimal.TryParse(Cell(3), NumberStyles.Number, CultureInfo.InvariantCulture, out var leagueValue) ? leagueValue : null,
            decimal.TryParse(Cell(4), NumberStyles.Number, CultureInfo.InvariantCulture, out var thisYear) ? thisYear : null);
    }

    private Season FindSeason(string seasonId)
    {
        var season = _configStore.GetSeasons().FirstOrDefault(s => s.Id == seasonId);
        if (season is null) throw new NotFoundException($"Season '{seasonId}' not found.");
        return season;
    }

    private Team FindTeam(string teamId)
    {
        var team = _configStore.GetTeams().FirstOrDefault(t => t.TeamId == teamId);
        if (team is null) throw new NotFoundException($"Team '{teamId}' not found.");
        return team;
    }

    private TeamMapping FindMapping(string seasonId, string teamId)
    {
        var mappings = _configStore.GetTeamMappings(seasonId);
        if (!mappings.TryGetValue(teamId, out var mapping))
        {
            throw new NotFoundException($"No mapping for team '{teamId}' in season '{seasonId}'.");
        }
        return mapping;
    }
}
