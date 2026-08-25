import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DashboardScreen } from "./DashboardScreen";
import type { League, RecommendationSet } from "../types";

describe("DashboardScreen", () => {
  const league: League = {
    importedAtUtc: "2026-01-01T00:00:00Z",
    teams: [
      {
        teamName: "Rhino Wranglers",
        players: [{ csvName: "Shohei Ohtani", playerId: "660271", playerFullName: "Shohei Ohtani", position: "DH", isPitcher: false }]
      }
    ]
  };

  it("loads existing recommendations, then re-analyzes and shows suggestion detail", async () => {
    const initial: RecommendationSet = { generatedAtUtc: "2026-01-01T00:00:00Z", waiverSuggestions: [], tradeSuggestions: [] };
    const refreshed: RecommendationSet = {
      generatedAtUtc: "2026-01-02T00:00:00Z",
      waiverSuggestions: [
        { type: "Waiver", summary: "Pick up X", reasoning: "Hot streak per recent box scores", involvedPlayerIds: ["1"], citations: ["https://example.com"], rank: 1 }
      ],
      tradeSuggestions: []
    };
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(initial) }) // initial GET
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(refreshed) }); // POST refresh
    vi.stubGlobal("fetch", fetchMock);

    render(<DashboardScreen league={league} yourTeamName="Rhino Wranglers" />);

    await waitFor(() => expect(screen.getByText("Shohei Ohtani")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: /analyze/i }));
    await waitFor(() => expect(screen.getByText("Pick up X")).toBeInTheDocument());

    fireEvent.click(screen.getByText("Pick up X"));
    expect(screen.getByText("Hot streak per recent box scores")).toBeInTheDocument();

    vi.unstubAllGlobals();
  });
});
