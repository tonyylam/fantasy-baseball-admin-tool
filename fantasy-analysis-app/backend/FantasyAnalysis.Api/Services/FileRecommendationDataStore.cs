using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FileRecommendationDataStore : IRecommendationDataStore
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FileRecommendationDataStore(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string Path_ => Path.Combine(_dataRoot, "recommendations.json");

    public RecommendationSet? Load()
    {
        if (!File.Exists(Path_)) return null;
        return JsonSerializer.Deserialize<RecommendationSet>(File.ReadAllText(Path_), JsonOptions);
    }

    public void Save(RecommendationSet recommendations)
    {
        Directory.CreateDirectory(_dataRoot);
        var tempPath = Path_ + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(recommendations, JsonOptions));
            File.Move(tempPath, Path_, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }
}
