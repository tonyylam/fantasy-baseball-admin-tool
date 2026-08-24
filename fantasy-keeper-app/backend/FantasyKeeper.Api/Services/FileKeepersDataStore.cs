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

    // Data written before a field existed is missing that key on disk, and System.Text.Json
    // deserializes a missing collection property as null regardless of its non-nullable NRT
    // annotation. Normalizing here means every downstream consumer (writer, services,
    // endpoints) can treat these lists as always-present; without it, legacy JSON crashed
    // the whole-league xlsx export with a NullReferenceException.
    public KeepersData? LoadData()
    {
        if (!File.Exists(DataPath)) return null;

        var data = JsonSerializer.Deserialize<KeepersData>(File.ReadAllText(DataPath), JsonOptions);
        if (data is null) return null;

        var teams = data.Teams ?? new Dictionary<string, StoredTeamKeepers>();
        var normalizedTeams = teams.ToDictionary(
            kv => kv.Key,
            kv => kv.Value with
            {
                NewContractsRows = kv.Value.NewContractsRows ?? Array.Empty<int>(),
                NewContracts = kv.Value.NewContracts ?? Array.Empty<KeeperRow>(),
                ExistingContractsRows = kv.Value.ExistingContractsRows ?? Array.Empty<int>(),
                ExistingContracts = kv.Value.ExistingContracts ?? Array.Empty<ExistingContractRow>()
            });

        return data with { Teams = normalizedTeams };
    }

    // Each file is written to a sibling temp file and then moved into place. File.Move with
    // overwrite is atomic within a volume, so a crash mid-write can never leave a truncated or
    // half-serialized final file (a corrupt current-keepers.json would make LoadData throw on
    // every subsequent request, bricking the app). Note this makes each individual file write
    // atomic; it does NOT make ConfirmImport's SaveData-then-SaveWorkbook pair atomic as a unit.
    public void SaveData(KeepersData data)
    {
        Directory.CreateDirectory(_dataRoot);
        WriteAtomic(DataPath, path => File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions)));
    }

    public void SaveWorkbook(byte[] bytes)
    {
        Directory.CreateDirectory(_dataRoot);
        WriteAtomic(WorkbookPath, path => File.WriteAllBytes(path, bytes));
    }

    private static void WriteAtomic(string finalPath, Action<string> write)
    {
        var tempPath = finalPath + ".tmp";
        try
        {
            write(tempPath);
            File.Move(tempPath, finalPath, overwrite: true);
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

    public byte[]? LoadWorkbook() => File.Exists(WorkbookPath) ? File.ReadAllBytes(WorkbookPath) : null;
}
