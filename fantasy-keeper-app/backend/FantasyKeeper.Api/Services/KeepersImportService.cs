using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class KeepersImportService
{
    private readonly IKeepersDataStore _store;
    private readonly IConfigStore _configStore;
    private readonly object _lock = new();
    private PendingImport? _pending;

    private record PendingImport(string SourceFileName, byte[] WorkbookBytes, ParsedWorkbook Parsed);

    public KeepersImportService(IKeepersDataStore store, IConfigStore configStore)
    {
        _store = store;
        _configStore = configStore;
    }

    public ImportPreview StartImport(byte[] fileBytes, string fileName)
    {
        ParsedWorkbook parsed;
        using (var ms = new MemoryStream(fileBytes))
        {
            parsed = KeeperWorkbookParser.Parse(ms);
        }

        var teams = _configStore.GetTeams();
        var blocks = new List<ImportBlockPreview>();
        for (var i = 0; i < parsed.Teams.Count; i++)
        {
            blocks.Add(new ImportBlockPreview(i, parsed.Teams[i].RawNameInSheet, SuggestTeamId(parsed.Teams[i].RawNameInSheet, teams)));
        }

        lock (_lock)
        {
            _pending = new PendingImport(fileName, fileBytes, parsed);
        }

        return new ImportPreview(fileName, parsed.SheetName, blocks);
    }

    public KeepersData ConfirmImport(IReadOnlyList<BlockAssignment> assignments)
    {
        PendingImport pending;
        lock (_lock)
        {
            pending = _pending ?? throw new InvalidWorkbookException("No pending import to confirm. Upload a file first.");
        }

        if (assignments.Count != pending.Parsed.Teams.Count)
        {
            throw new KeeperValidationException(new List<string> { "Every detected team must be resolved before confirming." });
        }

        var errors = new List<string>();
        var seenBlockIndexes = new HashSet<int>();
        var seenTeamIds = new HashSet<string>();
        var teams = new Dictionary<string, StoredTeamKeepers>();
        var validTeamIds = _configStore.GetTeams().Select(t => t.TeamId).ToHashSet();

        foreach (var assignment in assignments)
        {
            if (assignment.BlockIndex < 0 || assignment.BlockIndex >= pending.Parsed.Teams.Count)
            {
                errors.Add($"Block index {assignment.BlockIndex} is not a detected team.");
                continue;
            }
            if (!seenBlockIndexes.Add(assignment.BlockIndex))
            {
                errors.Add($"Block index {assignment.BlockIndex} was assigned more than once.");
                continue;
            }
            if (assignment.TeamId is null)
            {
                continue;
            }
            if (!validTeamIds.Contains(assignment.TeamId))
            {
                errors.Add($"'{assignment.TeamId}' is not a known team.");
                continue;
            }
            if (!seenTeamIds.Add(assignment.TeamId))
            {
                errors.Add($"Team '{assignment.TeamId}' was assigned to more than one block.");
                continue;
            }
            teams[assignment.TeamId] = pending.Parsed.Teams[assignment.BlockIndex];
        }

        if (errors.Count > 0)
        {
            throw new KeeperValidationException(errors);
        }

        var data = new KeepersData(pending.SourceFileName, pending.Parsed.SheetName, DateTimeOffset.UtcNow, teams);
        _store.SaveData(data);
        _store.SaveWorkbook(pending.WorkbookBytes);

        lock (_lock)
        {
            _pending = null;
        }

        return data;
    }

    public byte[] Export()
    {
        var data = _store.LoadData() ?? throw new NotFoundException("No keeper data has been imported yet.");
        var workbookBytes = _store.LoadWorkbook() ?? throw new NotFoundException("No keeper data has been imported yet.");
        return KeeperWorkbookWriter.WriteKeepers(workbookBytes, data.SheetName, data.Teams);
    }

    private static string? SuggestTeamId(string rawName, IReadOnlyList<Team> teams)
    {
        var normalizedRaw = Normalize(rawName);
        return teams.FirstOrDefault(t => Normalize(t.Name) == normalizedRaw)?.TeamId;
    }

    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
