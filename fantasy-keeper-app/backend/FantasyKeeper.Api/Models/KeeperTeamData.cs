namespace FantasyKeeper.Api.Models;

public record KeeperTeamData(string TeamName, IReadOnlyList<ExistingContractRow> ExistingContracts, IReadOnlyList<KeeperRow> NewContracts);
