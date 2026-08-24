using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class KeeperEndpoints
{
    public static void MapKeeperEndpoints(this WebApplication app)
    {
        app.MapGet("/api/keepers", (string pin, string teamId, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null) return Results.Unauthorized();

            var canEdit = auth.Role == AuthRole.Admin || teamId == auth.TeamId;

            try
            {
                return Results.Ok(keepersService.GetKeeperData(teamId, canEdit));
            }
            catch (NotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPut("/api/keepers", (string pin, string teamId, KeeperSubmission submission, AuthService authService, KeepersService keepersService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null) return Results.Unauthorized();

            var canEdit = auth.Role == AuthRole.Admin || teamId == auth.TeamId;
            if (!canEdit)
            {
                return Results.Json(new { error = "You don't have permission to edit this team." }, statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                return Results.Ok(keepersService.UpdateKeeperData(teamId, submission));
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

        app.MapGet("/api/teams", (string pin, AuthService authService, IConfigStore configStore) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null) return Results.Unauthorized();

            var teams = configStore.GetTeams().Select(t => new { teamId = t.TeamId, name = t.Name });
            return Results.Ok(teams);
        });
    }
}
