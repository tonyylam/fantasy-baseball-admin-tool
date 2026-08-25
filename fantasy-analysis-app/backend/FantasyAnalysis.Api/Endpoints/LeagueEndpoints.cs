using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Endpoints;

public static class LeagueEndpoints
{
    public static void MapLeagueEndpoints(this WebApplication app)
    {
        app.MapPost("/api/league/import", async (IFormFile file, LeagueImportService importService) =>
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var csvContent = await reader.ReadToEndAsync();

            try
            {
                var preview = await importService.PreviewImportAsync(csvContent);
                return Results.Ok(preview);
            }
            catch (CsvParseException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (StatsProviderException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        }).DisableAntiforgery();

        app.MapPost("/api/league/import/confirm", (ConfirmImportRequest request, LeagueImportService importService) =>
        {
            var league = importService.ConfirmImport(request);
            return Results.Ok(league);
        });

        app.MapGet("/api/league", (ILeagueDataStore leagueStore) =>
        {
            var league = leagueStore.LoadLeague();
            return league is null
                ? Results.NotFound(new { error = "No league has been imported yet." })
                : Results.Ok(league);
        });
    }
}
