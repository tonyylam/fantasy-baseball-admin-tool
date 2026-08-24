using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Endpoints;

public static class AdminKeepersEndpoints
{
    public static void MapAdminKeepersEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/teams", (string pin, AuthService authService, IConfigStore configStore) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            var teams = configStore.GetTeams().Select(t => new { teamId = t.TeamId, name = t.Name });
            return Results.Ok(teams);
        });

        app.MapPost("/api/admin/keepers/import", (string pin, IFormFile file, AuthService authService, KeepersImportService importService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            using var stream = file.OpenReadStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            try
            {
                return Results.Ok(importService.StartImport(ms.ToArray(), file.FileName));
            }
            catch (InvalidWorkbookException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();

        app.MapPost("/api/admin/keepers/import/confirm", (string pin, ConfirmImportRequest request, AuthService authService, KeepersImportService importService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            try
            {
                return Results.Ok(importService.ConfirmImport(request.Assignments));
            }
            catch (InvalidWorkbookException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (KeeperValidationException ex)
            {
                return Results.BadRequest(new { errors = ex.Errors });
            }
        });

        app.MapGet("/api/admin/keepers/export", (string pin, AuthService authService, KeepersImportService importService) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            try
            {
                var bytes = importService.Export();
                return Results.File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "keepers-export.xlsx");
            }
            catch (NotFoundException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapGet("/api/admin/keepers/status", (string pin, AuthService authService, IKeepersDataStore store) =>
        {
            var auth = authService.ResolvePin(pin);
            if (auth is null || auth.Role != AuthRole.Admin) return Results.Unauthorized();

            var data = store.LoadData();
            return Results.Ok(new { lastUpdatedUtc = data?.LastUpdatedUtc, sourceFileName = data?.SourceFileName });
        });
    }
}
