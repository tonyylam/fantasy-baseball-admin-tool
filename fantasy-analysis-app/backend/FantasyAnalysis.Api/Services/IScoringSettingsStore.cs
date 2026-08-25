using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IScoringSettingsStore
{
    ScoringSettings? Load();
    void Save(ScoringSettings settings);
}
