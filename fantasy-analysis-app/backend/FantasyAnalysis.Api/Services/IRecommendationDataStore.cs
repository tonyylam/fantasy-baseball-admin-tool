using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IRecommendationDataStore
{
    RecommendationSet? Load();
    void Save(RecommendationSet recommendations);
}
