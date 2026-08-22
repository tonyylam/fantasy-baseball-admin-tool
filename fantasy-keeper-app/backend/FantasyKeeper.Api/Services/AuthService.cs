using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class AuthService
{
    private readonly IConfigStore _configStore;
    private readonly string _adminPin;

    public AuthService(IConfigStore configStore, string adminPin)
    {
        _configStore = configStore;
        _adminPin = adminPin;
    }

    public AuthResult? ResolvePin(string pin)
    {
        if (!string.IsNullOrEmpty(pin) && pin == _adminPin)
        {
            return new AuthResult(AuthRole.Admin, null, null);
        }

        var team = _configStore.GetTeams().FirstOrDefault(t => t.Pin == pin);
        if (team is null)
        {
            return null;
        }

        var activeSeason = _configStore.GetSeasons().FirstOrDefault(s => s.IsActive);
        if (activeSeason is null)
        {
            return null;
        }

        return new AuthResult(AuthRole.Owner, team.TeamId, activeSeason.Id);
    }
}
