using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeRecommendationDataStore : IRecommendationDataStore
{
    public RecommendationSet? Saved { get; private set; }

    public RecommendationSet? Load() => Saved;

    public void Save(RecommendationSet recommendations) => Saved = recommendations;
}
