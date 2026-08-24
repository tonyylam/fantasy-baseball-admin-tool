namespace FantasyKeeper.Api.Models;

public record StoredTeamKeepers(
    string RawNameInSheet,
    int HeaderRow,
    IReadOnlyList<int> NewContractsRows,
    IReadOnlyList<KeeperRow> NewContracts,
    IReadOnlyList<int> ExistingContractsRows,
    IReadOnlyList<ExistingContractRow> ExistingContracts);

public record KeepersData(
    string SourceFileName,
    string SheetName,
    DateTimeOffset LastUpdatedUtc,
    IReadOnlyDictionary<string, StoredTeamKeepers> Teams);
