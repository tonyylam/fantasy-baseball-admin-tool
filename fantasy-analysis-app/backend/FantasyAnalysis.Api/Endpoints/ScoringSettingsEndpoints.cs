using System.Linq;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Endpoints;

public static class ScoringSettingsEndpoints
{
    public static void MapScoringSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings/scoring", (IScoringSettingsStore store) =>
        {
            var settings = store.Load();
            return settings is null
                ? Results.NotFound(new { error = "No scoring settings saved yet." })
                : Results.Ok(settings);
        });

        app.MapPut("/api/settings/scoring", (ScoringSettings settings, IScoringSettingsStore store) =>
        {
            store.Save(settings);
            return Results.Ok(settings);
        });

        app.MapGet("/api/settings/scoring/categories", () =>
        {
            var categories = RotoCategoryReference.Categories.Values
                .Select(c => new { statKey = c.StatKey, displayName = c.DisplayName, group = c.Group })
                .ToList();
            return Results.Ok(categories);
        });
    }
}
