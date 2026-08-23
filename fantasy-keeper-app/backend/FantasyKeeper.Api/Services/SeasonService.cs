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
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Season label is required.");
        }

        var seasons = _configStore.GetSeasons();
        var active = seasons.FirstOrDefault(s => s.IsActive);
        if (active is null)
        {
            throw new InvalidOperationException("No active season found to copy from.");
        }

        var newSheetId = await _drive.CopyFileAsync(active.GoogleSheetId, label, ct);

        // Skip sharing when no commissioner email is configured (e.g. a
        // deployment that hasn't set Google:CommissionerEmail yet) rather
        // than calling the real Drive API with an empty/invalid email,
        // which would fail after already creating the copy and leave an
        // orphaned, unshared file behind. A season with an unshared sheet
        // is still fully usable by the app itself.
        if (!string.IsNullOrWhiteSpace(_commissionerEmail))
        {
            await _drive.ShareFileAsync(newSheetId, _commissionerEmail, ct);
        }

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
