using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class KeeperEndpoints
{
    public static void MapKeeperEndpoints(this WebApplication app)
    {
        app.MapGet("/api/keepers", (string pin, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Owner || auth.TeamId is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(keepersService.GetKeeperData(auth.TeamId));
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPut("/api/keepers", (string pin, KeeperSubmission submission, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Owner || auth.TeamId is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(keepersService.UpdateKeeperData(auth.TeamId, submission));
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
