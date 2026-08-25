using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeScoringSettingsStore : IScoringSettingsStore
{
    private ScoringSettings? _settings;

    public FakeScoringSettingsStore(ScoringSettings? initial = null) => _settings = initial;

    public ScoringSettings? Load() => _settings;

    public void Save(ScoringSettings settings) => _settings = settings;
}
