using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class SeasonService
{
    private readonly IConfigStore _configStore;
    private readonly IDriveClient _drive;
    private readonly string _commissionerEmail;

    public SeasonService(IConfigStore configStore, IDriveClient drive, string commissionerEmail)
    {
        _configStore = configStore;
        _drive = drive;
        _commissionerEmail = commissionerEmail;
    }

    public IReadOnlyList<Season> ListSeasons() => _configStore.GetSeasons();

    public async Task<Season> CreateNewSeasonAsync(string label, CancellationToken ct = default)
    {
        var seasons = _configStore.GetSeasons();
        var active = seasons.FirstOrDefault(s => s.IsActive);
        if (active is null)
        {
            throw new InvalidOperationException("No active season found to copy from.");
        }

        var newSheetId = await _drive.CopyFileAsync(active.GoogleSheetId, label, ct);
        await _drive.ShareFileAsync(newSheetId, _commissionerEmail, ct);

        var newSeasonId = Guid.NewGuid().ToString("N");
        var mappings = _configStore.GetTeamMappings(active.Id);
        _configStore.SaveTeamMappings(newSeasonId, mappings);

        var updatedSeasons = seasons
            .Select(s => s.Id == active.Id ? s with { Status = "archived" } : s)
            .Append(new Season(newSeasonId, label, newSheetId, "active", DateTimeOffset.UtcNow))
            .ToList();

        _configStore.SaveSeasons(updatedSeasons);

        return updatedSeasons.Single(s => s.Id == newSeasonId);
    }
}
