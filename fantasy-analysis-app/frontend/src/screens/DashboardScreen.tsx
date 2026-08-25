import { useEffect, useState } from "react";
import { getRecommendations, refreshRecommendations } from "../api/client";
import type { League, Recommendation, RecommendationSet } from "../types";

interface DashboardScreenProps {
  league: League;
  yourTeamName: string;
}

export function DashboardScreen({ league, yourTeamName }: DashboardScreenProps) {
  const [recommendations, setRecommendations] = useState<RecommendationSet | null>(null);
  const [selected, setSelected] = useState<Recommendation | null>(null);
  const [analyzing, setAnalyzing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const yourTeam = league.teams.find((t) => t.teamName === yourTeamName);

  useEffect(() => {
    getRecommendations().then(setRecommendations);
  }, []);

  async function handleAnalyze() {
    setAnalyzing(true);
    setError(null);
    try {
      const result = await refreshRecommendations(yourTeamName);
      setRecommendations(result);
    } catch {
      setError("Analysis failed. Please try again.");
    } finally {
      setAnalyzing(false);
    }
  }

  function suggestionList(title: string, suggestions: Recommendation[]) {
    return (
      <section>
        <h2>{title}</h2>
        <ul>
          {suggestions.map((s) => (
            <li key={`${s.type}-${s.rank}`}>
              <button onClick={() => setSelected(s)}>{s.summary}</button>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  return (
    <div>
      <h1>{yourTeamName} Dashboard</h1>
      <section>
        <h2>Your Roster</h2>
        <ul>
          {yourTeam?.players.map((p) => (
            <li key={p.playerId}>{p.playerFullName}</li>
          ))}
        </ul>
      </section>

      <button onClick={handleAnalyze} disabled={analyzing}>
        {analyzing ? "Analyzing... (this can take a couple minutes on a cold cache)" : "Analyze"}
      </button>
      {error && <p role="alert">{error}</p>}

      {recommendations && (
        <>
          {suggestionList("Waiver Suggestions", recommendations.waiverSuggestions)}
          {suggestionList("Trade Suggestions", recommendations.tradeSuggestions)}
        </>
      )}

      {selected && (
        <aside>
          <h3>{selected.summary}</h3>
          <p>{selected.reasoning}</p>
          <ul>
            {selected.citations.map((c) => (
              <li key={c}>
                <a href={c}>{c}</a>
              </li>
            ))}
          </ul>
        </aside>
      )}
    </div>
  );
}
