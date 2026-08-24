export type AuthRole = "Owner" | "Admin";

export interface AuthResult {
  role: AuthRole;
  teamId: string | null;
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
  existingContracts: ExistingContractRow[];
  newContracts: KeeperRow[];
}

export interface TeamSummary {
  teamId: string;
  name: string;
}

export interface ImportBlockPreview {
  blockIndex: number;
  rawNameInSheet: string;
  suggestedTeamId: string | null;
}

export interface ImportPreview {
  fileName: string;
  blocks: ImportBlockPreview[];
}

export interface BlockAssignment {
  blockIndex: number;
  teamId: string | null;
}

export interface KeepersStatus {
  lastUpdatedUtc: string | null;
  sourceFileName: string | null;
}
