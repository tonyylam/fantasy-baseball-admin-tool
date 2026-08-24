using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class KeepersService
{
    private readonly IKeepersDataStore _store;
    private readonly IConfigStore _configStore;

    // Serializes UpdateKeeperData's load -> validate -> mutate -> save sequence. Without it,
    // two owners saving near-simultaneously would each load the same snapshot and the second
    // save would silently clobber the first (lost update).
    private readonly object _lock = new();

    public KeepersService(IKeepersDataStore store, IConfigStore configStore)
    {
        _store = store;
        _configStore = configStore;
    }

    public KeeperTeamData GetKeeperData(string teamId, bool canEdit)
    {
        var team = FindTeam(teamId);
        var stored = FindStoredTeam(teamId);
        return new KeeperTeamData(team.Name, canEdit, stored.ExistingContracts, stored.NewContracts);
    }

    public KeeperTeamData UpdateKeeperData(string teamId, KeeperSubmission submission)
    {
        var team = FindTeam(teamId);

        lock (_lock)
        {
            var data = _store.LoadData() ?? throw new NotFoundException("No keeper data has been imported yet.");
            if (!data.Teams.TryGetValue(teamId, out var stored))
            {
                throw new NotFoundException($"No keeper data found for team '{teamId}'.");
            }

            var errors = ValidateSubmission(submission, stored.NewContractsRows.Count, stored.ExistingContracts.Count);
            if (errors.Count > 0)
            {
                throw new KeeperValidationException(errors);
            }

            var deletedIndices = submission.DeletedExistingContractIndices.ToHashSet();
            var updatedExisting = stored.ExistingContracts
                .Select((row, i) => row with { Deleted = deletedIndices.Contains(i) })
                .ToList();

            var updatedStored = stored with { NewContracts = submission.NewContracts, ExistingContracts = updatedExisting };
            var updatedTeams = new Dictionary<string, StoredTeamKeepers>(data.Teams) { [teamId] = updatedStored };
            var updatedData = data with { Teams = updatedTeams, LastUpdatedUtc = DateTimeOffset.UtcNow };
            _store.SaveData(updatedData);

            return new KeeperTeamData(team.Name, true, updatedStored.ExistingContracts, updatedStored.NewContracts);
        }
    }

    private static List<string> ValidateSubmission(KeeperSubmission submission, int expectedRows, int existingContractsCount)
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

        foreach (var index in submission.DeletedExistingContractIndices)
        {
            if (index < 0 || index >= existingContractsCount)
            {
                errors.Add($"Existing contract index {index} is out of range.");
            }
        }

        return errors;
    }

    private Team FindTeam(string teamId)
    {
        var team = _configStore.GetTeams().FirstOrDefault(t => t.TeamId == teamId);
        if (team is null) throw new NotFoundException($"Team '{teamId}' not found.");
        return team;
    }

    private StoredTeamKeepers FindStoredTeam(string teamId)
    {
        var data = _store.LoadData() ?? throw new NotFoundException("No keeper data has been imported yet.");
        if (!data.Teams.TryGetValue(teamId, out var stored))
        {
            throw new NotFoundException($"No keeper data found for team '{teamId}'.");
        }
        return stored;
    }
}
