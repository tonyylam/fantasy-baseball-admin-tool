import { useState } from "react";
import { confirmImport } from "../api/client";
import type { ConfirmedPlayer, ConfirmImportRequest, ImportPreview, League, PlayerMatchCandidate } from "../types";

interface MatchReviewScreenProps {
  preview: ImportPreview;
  onConfirmed: (league: League) => void;
}

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
      {preview.teams.map((team, teamIndex) => (
        <section key={team.teamName}>
          <h2>{team.teamName}</h2>
          {team.players.map((player, playerIndex) => (
            <div key={player.csvName}>
              <span>{player.csvName}</span>
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
            </div>
          ))}
        </section>
      ))}
      <button onClick={handleConfirm} disabled={confirming}>
        {confirming ? "Confirming..." : "Confirm Import"}
      </button>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
