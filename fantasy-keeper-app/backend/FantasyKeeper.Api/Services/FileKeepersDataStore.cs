using System.Text.Json;
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class FileKeepersDataStore : IKeepersDataStore
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileKeepersDataStore(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string DataPath => Path.Combine(_dataRoot, "current-keepers.json");
    private string WorkbookPath => Path.Combine(_dataRoot, "current-keepers.xlsx");

    public KeepersData? LoadData()
    {
        if (!File.Exists(DataPath)) return null;
        return JsonSerializer.Deserialize<KeepersData>(File.ReadAllText(DataPath), JsonOptions);
    }

    public void SaveData(KeepersData data)
    {
        Directory.CreateDirectory(_dataRoot);
        File.WriteAllText(DataPath, JsonSerializer.Serialize(data, JsonOptions));
    }

    public void SaveWorkbook(byte[] bytes)
    {
        Directory.CreateDirectory(_dataRoot);
        File.WriteAllBytes(WorkbookPath, bytes);
    }

    public byte[]? LoadWorkbook() => File.Exists(WorkbookPath) ? File.ReadAllBytes(WorkbookPath) : null;
}
