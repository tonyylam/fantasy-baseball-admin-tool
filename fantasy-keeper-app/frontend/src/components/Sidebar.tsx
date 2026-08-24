import type { TeamSummary } from "../types";

interface Props {
  teams: TeamSummary[];
  myTeamId: string | null;
  isAdmin: boolean;
  activeTeamId: string | null;
  onSelectTeam: (teamId: string) => void;
  onSelectAdminPanel: () => void;
}

export function Sidebar({ teams, myTeamId, isAdmin, activeTeamId, onSelectTeam, onSelectAdminPanel }: Props) {
  return (
    <nav style={{ width: 200, flexShrink: 0, borderRight: "1px solid #ccc", padding: "0.5rem" }}>
      {isAdmin && (
        <button
          onClick={onSelectAdminPanel}
          style={{
            display: "block",
            width: "100%",
            textAlign: "left",
            fontWeight: activeTeamId === null ? "bold" : "normal"
          }}
        >
          Admin Panel
        </button>
      )}
      {teams.map((team) => (
        <button
          key={team.teamId}
          onClick={() => onSelectTeam(team.teamId)}
          style={{
            display: "block",
            width: "100%",
            textAlign: "left",
            fontWeight: activeTeamId === team.teamId ? "bold" : "normal"
          }}
        >
          {team.name}
          {team.teamId === myTeamId ? " (My Team)" : ""}
        </button>
      ))}
    </nav>
  );
}
