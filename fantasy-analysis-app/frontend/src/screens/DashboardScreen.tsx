import { useEffect, useState } from "react";
import { ApiError, getRecommendations, refreshRecommendations } from "../api/client";
import type { League, Recommendation, RecommendationSet } from "../types";

function describeAnalysisError(err: unknown): string {
  if (err instanceof ApiError) {
    const body = err.body;
    const detail =
      body && typeof body === "object" && "error" in body && typeof (body as { error: unknown }).error === "string"
        ? (body as { error: string }).error
        : JSON.stringify(body);
    return `Analysis failed (${err.status}): ${detail}`;
  }
  if (err instanceof Error) {
    return `Analysis failed: ${err.message}`;
  }
  return "Analysis failed. Please try again.";
}

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
    getRecommendations()
      .then(setRecommendations)
      .catch(() => setError("Failed to load existing recommendations."));
  }, []);

  async function handleAnalyze() {
    setAnalyzing(true);
    setError(null);
    setSelected(null);
    setRecommendations(null);
    try {
      const result = await refreshRecommendations(yourTeamName);
      setRecommendations(result);
    } catch (err) {
      setError(describeAnalysisError(err));
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
      <button onClick={handleAnalyze} disabled={analyzing}>
        {analyzing ? "Analyzing... (this can take a couple minutes on a cold cache)" : "Analyze"}
      </button>
      {error && <p role="alert">{error}</p>}

      <div style={{ display: "flex", gap: "2rem", alignItems: "flex-start" }}>
        <section style={{ flex: "1 1 30%" }}>
          <h2>Your Roster</h2>
          <ul>
            {yourTeam?.players.map((p) => (
              <li key={p.playerId}>{p.playerFullName}</li>
            ))}
          </ul>
        </section>

        <section style={{ flex: "2 1 60%" }}>
          {analyzing && <p>Generating suggestions... this can take a couple minutes.</p>}

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
        </section>
      </div>
    </div>
  );
}
