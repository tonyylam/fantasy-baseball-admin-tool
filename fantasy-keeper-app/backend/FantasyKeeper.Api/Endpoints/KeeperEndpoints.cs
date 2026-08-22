using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class KeeperEndpoints
{
    public static void MapKeeperEndpoints(this WebApplication app)
    {
        app.MapGet("/api/keepers", async (string pin, string? seasonId, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Owner || auth.TeamId is null) return Results.Unauthorized();

            var targetSeasonId = seasonId ?? auth.SeasonId!;

            try
            {
                return Results.Ok(await keepersService.GetKeeperDataAsync(targetSeasonId, auth.TeamId));
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPut("/api/keepers", async (string pin, string seasonId, KeeperSubmission submission, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Owner || auth.TeamId is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(await keepersService.UpdateKeeperDataAsync(seasonId, auth.TeamId, submission));
            }
            catch (SeasonNotActiveException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (KeeperValidationException ex)
            {
                return Results.BadRequest(new { errors = ex.Errors });
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });
    }
}
