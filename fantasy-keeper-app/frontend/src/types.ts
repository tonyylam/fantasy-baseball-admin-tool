export type AuthRole = "Owner" | "Admin";

export interface AuthResult {
  role: AuthRole;
  teamId: string | null;
  seasonId: string | null;
}

export interface Season {
  id: string;
  label: string;
  googleSheetId: string;
  status: "active" | "archived";
  createdAt: string;
}

export interface KeeperRow {
  player: string;
  contractType: number | null;
  salary: number | null;
  keeperYears: number | null;
}

export interface ExistingContractRow {
  player: string;
  contractInfo: string;
  lastYearSalary: number | null;
  leagueValue: number | null;
  thisYearSalary: number | null;
}

export interface KeeperTeamData {
  teamName: string;
  readOnly: boolean;
  existingContracts: ExistingContractRow[];
  newContracts: KeeperRow[];
}
