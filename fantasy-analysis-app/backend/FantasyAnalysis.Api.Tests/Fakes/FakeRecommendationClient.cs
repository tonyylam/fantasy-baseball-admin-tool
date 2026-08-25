using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeRecommendationClient : IRecommendationClient
{
    private readonly string _responseJson;
    public string? LastSystemPrompt { get; private set; }
    public string? LastUserPrompt { get; private set; }

    public FakeRecommendationClient(string responseJson)
    {
        _responseJson = responseJson;
    }

    public Task<string> GetRecommendationsJsonAsync(string systemPrompt, string userPrompt)
    {
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        return Task.FromResult(_responseJson);
    }
}
