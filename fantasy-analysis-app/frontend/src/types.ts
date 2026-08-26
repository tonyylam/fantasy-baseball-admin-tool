export interface RosteredPlayer {
  csvName: string;
  playerId: string;
  playerFullName: string;
  position: string;
  isPitcher: boolean;
}

export interface TeamRoster {
  teamName: string;
  players: RosteredPlayer[];
}

export interface League {
  importedAtUtc: string;
  teams: TeamRoster[];
}

export interface PlayerMatchCandidate {
  playerId: string;
  fullName: string;
  position: string;
  isPitcher: boolean;
  score: number;
}

export interface PlayerMatch {
  csvName: string;
  bestGuess: PlayerMatchCandidate | null;
  candidates: PlayerMatchCandidate[];
}

export interface TeamMatchPreview {
  teamName: string;
  players: PlayerMatch[];
}

export interface ImportPreview {
  teams: TeamMatchPreview[];
}

export interface ConfirmedPlayer {
  csvName: string;
  playerId: string | null;
  playerFullName: string | null;
  position: string | null;
  isPitcher: boolean;
}

export interface ConfirmedTeam {
  teamName: string;
  players: ConfirmedPlayer[];
}

export interface ConfirmImportRequest {
  teams: ConfirmedTeam[];
}

export interface ScoringSettings {
  hittingCategoryKeys: string[];
  pitchingCategoryKeys: string[];
  rosterSlots: Record<string, number>;
}

export interface ScoringCategoryOption {
  statKey: string;
  displayName: string;
  group: "hitting" | "pitching";
}

export type RecommendationType = "Waiver" | "Trade";

export interface Recommendation {
  type: RecommendationType;
  summary: string;
  reasoning: string;
  involvedPlayerIds: string[];
  citations: string[];
  rank: number;
}

export interface RecommendationSet {
  generatedAtUtc: string;
  waiverSuggestions: Recommendation[];
  tradeSuggestions: Recommendation[];
}
