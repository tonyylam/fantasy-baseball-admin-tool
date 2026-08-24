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
            return new AuthResult(AuthRole.Admin, null);
        }

        var team = _configStore.GetTeams().FirstOrDefault(t => t.Pin == pin);
        return team is null ? null : new AuthResult(AuthRole.Owner, team.TeamId);
    }
}
