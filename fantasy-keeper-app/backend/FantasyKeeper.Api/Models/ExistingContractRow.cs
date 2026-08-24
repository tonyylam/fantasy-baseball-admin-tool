namespace FantasyKeeper.Api.Models;

public record ExistingContractRow(string Player, string ContractInfo, decimal? LastYearSalary, decimal? LeagueValue, decimal? ThisYearSalary, bool Deleted = false);
