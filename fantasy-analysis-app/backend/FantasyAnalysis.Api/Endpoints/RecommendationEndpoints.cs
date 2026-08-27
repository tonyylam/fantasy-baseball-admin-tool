using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Endpoints;

public static class RecommendationEndpoints
{
    public static void MapRecommendationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/recommendations/refresh", async (string teamName, RecommendationOrchestrationService orchestrator) =>
        {
            try
            {
                var result = await orchestrator.RefreshAsync(teamName);
                return Results.Ok(result);
            }
            catch (RecommendationPrerequisiteException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (StatsProviderException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
            catch (RecommendationClientException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapGet("/api/recommendations", (RecommendationOrchestrationService orchestrator) =>
        {
            var last = orchestrator.GetLast();
            return last is null
                ? Results.NotFound(new { error = "No recommendations generated yet." })
                : Results.Ok(last);
        });
    }
}
