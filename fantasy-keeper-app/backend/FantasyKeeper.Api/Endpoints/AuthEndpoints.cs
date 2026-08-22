using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class AuthEndpoints
{
    public record AuthRequest(string Pin);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth", (AuthRequest request, AuthService authService) =>
        {
            var result = authService.ResolvePin(request.Pin);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });
    }
}
