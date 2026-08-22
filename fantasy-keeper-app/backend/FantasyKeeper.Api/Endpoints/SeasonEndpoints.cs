using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class SeasonEndpoints
{
    public record CreateSeasonRequest(string Label);

    public static void MapSeasonEndpoints(this WebApplication app)
    {
        app.MapGet("/api/seasons", (string pin, AuthService authService, SeasonService seasonService) =>
        {
            var auth = authService.ResolvePin(pin);
            return auth is null ? Results.Unauthorized() : Results.Ok(seasonService.ListSeasons());
        });

        app.MapPost("/api/admin/seasons", async (string pin, CreateSeasonRequest request, AuthService authService, SeasonService seasonService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            var season = await seasonService.CreateNewSeasonAsync(request.Label);
            return Results.Ok(season);
        });
    }
}
