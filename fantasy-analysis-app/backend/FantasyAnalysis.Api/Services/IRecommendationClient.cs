namespace FantasyAnalysis.Api.Services;

public interface IRecommendationClient
{
    Task<string> GetRecommendationsJsonAsync(string systemPrompt, string userPrompt);
}
