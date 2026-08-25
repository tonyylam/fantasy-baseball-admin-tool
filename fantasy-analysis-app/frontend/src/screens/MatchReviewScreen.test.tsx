import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { MatchReviewScreen } from "./MatchReviewScreen";
import type { ImportPreview, League } from "../types";

describe("MatchReviewScreen", () => {
  const preview: ImportPreview = {
    teams: [
      {
        teamName: "Rhino Wranglers",
        players: [
          {
            csvName: "Shohei Ohtani",
            bestGuess: { playerId: "660271", fullName: "Shohei Ohtani", position: "DH", isPitcher: false, score: 1 },
            candidates: [{ playerId: "660271", fullName: "Shohei Ohtani", position: "DH", isPitcher: false, score: 1 }]
          },
          {
            csvName: "Unknown Guy",
            bestGuess: null,
            candidates: []
          }
        ]
      }
    ]
  };

  it("confirms with the default best-guess selection and drops unresolved players", async () => {
    const league: League = { importedAtUtc: "2026-01-01T00:00:00Z", teams: [] };
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve(league) });
    vi.stubGlobal("fetch", fetchMock);
    const onConfirmed = vi.fn();

    render(<MatchReviewScreen preview={preview} onConfirmed={onConfirmed} />);
    fireEvent.click(screen.getByRole("button", { name: /confirm import/i }));

    await waitFor(() => expect(onConfirmed).toHaveBeenCalledWith(league));
    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(body.teams[0].players).toHaveLength(2);
    expect(body.teams[0].players[0].playerId).toBe("660271");
    expect(body.teams[0].players[1].playerId).toBeNull();

    vi.unstubAllGlobals();
  });

  it("warns that unresolved players will be excluded from analysis", () => {
    render(<MatchReviewScreen preview={preview} onConfirmed={vi.fn()} />);

    expect(screen.getByRole("alert")).toHaveTextContent(/1 player will be excluded from analysis/i);
  });

  it("does not warn when every player is resolved", () => {
    const resolvedPreview: ImportPreview = {
      teams: [
        {
          teamName: "Rhino Wranglers",
          players: [preview.teams[0].players[0]]
        }
      ]
    };

    render(<MatchReviewScreen preview={resolvedPreview} onConfirmed={vi.fn()} />);

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
