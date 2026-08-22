namespace FantasyKeeper.Api.Models;

public record KeeperTeamData(string TeamName, bool ReadOnly, IReadOnlyList<ExistingContractRow> ExistingContracts, IReadOnlyList<KeeperRow> NewContracts);
