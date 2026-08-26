import { useState } from "react";
import { confirmImport } from "../api/client";
import type { ConfirmedPlayer, ConfirmImportRequest, ImportPreview, League, PlayerMatchCandidate } from "../types";

interface MatchReviewScreenProps {
  preview: ImportPreview;
  onConfirmed: (league: League) => void;
}

const HIGH_CONFIDENCE_THRESHOLD = 0.8;
const HIGH_CONFIDENCE_COLOR = "#d4edda";
const LOW_CONFIDENCE_COLOR = "#fff3cd";

export function MatchReviewScreen({ preview, onConfirmed }: MatchReviewScreenProps) {
  const [selections, setSelections] = useState<(string | null)[][]>(() =>
    preview.teams.map((team) => team.players.map((p) => p.bestGuess?.playerId ?? null))
  );
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function selectCandidate(teamIndex: number, playerIndex: number, playerId: string) {
    setSelections((prev) => {
      const next = prev.map((row) => [...row]);
      next[teamIndex][playerIndex] = playerId || null;
      return next;
    });
  }

  function findCandidate(candidates: PlayerMatchCandidate[], playerId: string | null): PlayerMatchCandidate | null {
    return candidates.find((c) => c.playerId === playerId) ?? null;
  }

  function confidenceLevel(candidate: PlayerMatchCandidate | null): "high" | "low" | "unresolved" {
    if (!candidate) return "unresolved";
    return candidate.score >= HIGH_CONFIDENCE_THRESHOLD ? "high" : "low";
  }

  function confidenceRowStyle(level: "high" | "low" | "unresolved"): React.CSSProperties | undefined {
    if (level === "high") return { backgroundColor: HIGH_CONFIDENCE_COLOR };
    if (level === "low") return { backgroundColor: LOW_CONFIDENCE_COLOR };
    return undefined;
  }

  const unresolvedCount = selections.flat().filter((s) => s === null).length;

  async function handleConfirm() {
    setConfirming(true);
    setError(null);
    try {
      const request: ConfirmImportRequest = {
        teams: preview.teams.map((team, teamIndex) => ({
          teamName: team.teamName,
          players: team.players.map((player, playerIndex): ConfirmedPlayer => {
            const selectedId = selections[teamIndex][playerIndex];
            const candidate = findCandidate(player.candidates, selectedId);
            return {
              csvName: player.csvName,
              playerId: candidate?.playerId ?? null,
              playerFullName: candidate?.fullName ?? null,
              position: candidate?.position ?? null,
              isPitcher: candidate?.isPitcher ?? false
            };
          })
        }))
      };
      const league = await confirmImport(request);
      onConfirmed(league);
    } catch {
      setError("Failed to confirm import. Please try again.");
    } finally {
      setConfirming(false);
    }
  }

  return (
    <div>
      <h1>Review Matched Players</h1>
      <p>
        Rows highlighted <span style={{ backgroundColor: HIGH_CONFIDENCE_COLOR }}>green</span> are high-confidence
        matches ({Math.round(HIGH_CONFIDENCE_THRESHOLD * 100)}% or higher). Rows highlighted{" "}
        <span style={{ backgroundColor: LOW_CONFIDENCE_COLOR }}>yellow</span> are worth a closer look.
      </p>
      {preview.teams.map((team, teamIndex) => (
        <section key={team.teamName}>
          <h2>{team.teamName}</h2>
          <table>
            <thead>
              <tr>
                <th>Player</th>
                <th>Match</th>
              </tr>
            </thead>
            <tbody>
              {team.players.map((player, playerIndex) => {
                const selectedId = selections[teamIndex][playerIndex];
                const selectedCandidate = findCandidate(player.candidates, selectedId);
                const level = confidenceLevel(selectedCandidate);
                return (
                  <tr key={player.csvName} data-confidence={level} style={confidenceRowStyle(level)}>
                    <td>{player.csvName}</td>
                    <td>
                      <select
                        value={selections[teamIndex][playerIndex] ?? ""}
                        onChange={(e) => selectCandidate(teamIndex, playerIndex, e.target.value)}
                      >
                        <option value="">-- Unresolved / Skip --</option>
                        {player.candidates.map((c) => (
                          <option key={c.playerId} value={c.playerId}>
                            {c.fullName} ({Math.round(c.score * 100)}%)
                          </option>
                        ))}
                      </select>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </section>
      ))}
      {unresolvedCount > 0 && (
        <p role="alert">
          {unresolvedCount} player{unresolvedCount === 1 ? "" : "s"} will be excluded from analysis (left
          unresolved).
        </p>
      )}
      <button onClick={handleConfirm} disabled={confirming}>
        {confirming ? "Confirming..." : "Confirm Import"}
      </button>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
