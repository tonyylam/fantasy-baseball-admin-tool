import { useEffect, useState } from "react";
import { getLeague, getScoringSettings } from "./api/client";
import { ImportScreen } from "./screens/ImportScreen";
import { MatchReviewScreen } from "./screens/MatchReviewScreen";
import { ScoringSettingsScreen } from "./screens/ScoringSettingsScreen";
import { DashboardScreen } from "./screens/DashboardScreen";
import type { ImportPreview, League } from "./types";

type Screen = "loading" | "import" | "matchReview" | "teamPicker" | "settings" | "dashboard";

export function App() {
  const [screen, setScreen] = useState<Screen>("loading");
  const [league, setLeague] = useState<League | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [yourTeamName, setYourTeamName] = useState<string | null>(null);
  const [hasSettings, setHasSettings] = useState(false);

  useEffect(() => {
    Promise.all([getLeague(), getScoringSettings()]).then(([loadedLeague, settings]) => {
      setLeague(loadedLeague);
      setHasSettings(settings !== null);
      const storedTeam = localStorage.getItem("yourTeamName");
      setYourTeamName(storedTeam);

      if (!loadedLeague) {
        setScreen("import");
      } else if (!storedTeam || !loadedLeague.teams.some((t) => t.teamName === storedTeam)) {
        setScreen("teamPicker");
      } else if (settings === null) {
        setScreen("settings");
      } else {
        setScreen("dashboard");
      }
    });
  }, []);

  function handlePreviewReady(nextPreview: ImportPreview) {
    setPreview(nextPreview);
    setScreen("matchReview");
  }

  function handleConfirmed(nextLeague: League) {
    setLeague(nextLeague);
    setPreview(null);
    setScreen("teamPicker");
  }

  function handleTeamChosen(teamName: string) {
    localStorage.setItem("yourTeamName", teamName);
    setYourTeamName(teamName);
    setScreen(hasSettings ? "dashboard" : "settings");
  }

  function handleSettingsSaved() {
    setHasSettings(true);
    setScreen("dashboard");
  }

  if (screen === "loading") return <p>Loading...</p>;

  return (
    <div>
      {league && (
        <header>
          <button onClick={() => setScreen(yourTeamName ? "dashboard" : "teamPicker")}>Dashboard</button>
          <button onClick={() => setScreen("settings")}>Scoring Settings</button>
          <button onClick={() => setScreen("import")}>Re-import League</button>
        </header>
      )}

      {screen === "import" && <ImportScreen onPreviewReady={handlePreviewReady} />}
      {screen === "matchReview" && preview && (
        <MatchReviewScreen preview={preview} onConfirmed={handleConfirmed} />
      )}
      {screen === "teamPicker" && league && (
        <div>
          <h1>Which team is yours?</h1>
          {league.teams.map((t) => (
            <button key={t.teamName} onClick={() => handleTeamChosen(t.teamName)}>
              {t.teamName}
            </button>
          ))}
        </div>
      )}
      {screen === "settings" && <ScoringSettingsScreen onSaved={handleSettingsSaved} />}
      {screen === "dashboard" && league && yourTeamName && (
        <DashboardScreen league={league} yourTeamName={yourTeamName} />
      )}
    </div>
  );
}
