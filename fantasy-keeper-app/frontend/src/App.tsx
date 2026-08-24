import { useEffect, useState } from "react";
import { useAuth } from "./state/useAuth";
import { PinEntryScreen } from "./screens/PinEntryScreen";
import { KeeperFormScreen } from "./screens/KeeperFormScreen";
import { AdminPanel } from "./screens/AdminPanel";
import { Sidebar } from "./components/Sidebar";
import { getTeams } from "./api/client";
import type { TeamSummary } from "./types";

type View = { kind: "team"; teamId: string } | { kind: "admin" };

export default function App() {
  const { pin, auth, login, logout, error, isLoading } = useAuth();
  const [teams, setTeams] = useState<TeamSummary[]>([]);
  const [view, setView] = useState<View | null>(null);

  useEffect(() => {
    if (!pin) return;
    getTeams(pin).then(setTeams).catch(() => setTeams([]));
  }, [pin]);

  if (!pin || !auth) {
    return <PinEntryScreen onSubmit={login} error={error} isLoading={isLoading} />;
  }

  const isAdmin = auth.role === "Admin";
  const effectiveView: View = view ?? (isAdmin ? { kind: "admin" } : { kind: "team", teamId: auth.teamId! });

  return (
    <div style={{ display: "flex", minHeight: "100vh" }}>
      <Sidebar
        teams={teams}
        myTeamId={auth.teamId}
        isAdmin={isAdmin}
        activeTeamId={effectiveView.kind === "team" ? effectiveView.teamId : null}
        onSelectTeam={(teamId) => setView({ kind: "team", teamId })}
        onSelectAdminPanel={() => setView({ kind: "admin" })}
      />
      <div style={{ flex: 1, padding: "1rem" }}>
        <button onClick={logout}>Log out</button>
        {effectiveView.kind === "admin" ? (
          <AdminPanel pin={pin} />
        ) : (
          <KeeperFormScreen pin={pin} teamId={effectiveView.teamId} />
        )}
      </div>
    </div>
  );
}
