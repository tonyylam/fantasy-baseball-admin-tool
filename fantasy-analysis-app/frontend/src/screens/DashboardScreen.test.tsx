import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DashboardScreen } from "./DashboardScreen";
import type { League, RecommendationSet } from "../types";

describe("DashboardScreen", () => {
  afterEach(cleanup);

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

  it("clears the previously selected suggestion when re-analyzing", async () => {
    const initial: RecommendationSet = {
      generatedAtUtc: "2026-01-01T00:00:00Z",
      waiverSuggestions: [
        { type: "Waiver", summary: "Old pick", reasoning: "Old reasoning", involvedPlayerIds: ["1"], citations: [], rank: 1 }
      ],
      tradeSuggestions: []
    };
    const refreshed: RecommendationSet = {
      generatedAtUtc: "2026-01-02T00:00:00Z",
      waiverSuggestions: [
        { type: "Waiver", summary: "New pick", reasoning: "New reasoning", involvedPlayerIds: ["2"], citations: [], rank: 1 }
      ],
      tradeSuggestions: []
    };
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(initial) }) // initial GET
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(refreshed) }); // POST refresh
    vi.stubGlobal("fetch", fetchMock);

    render(<DashboardScreen league={league} yourTeamName="Rhino Wranglers" />);

    await waitFor(() => expect(screen.getByText("Old pick")).toBeInTheDocument());
    fireEvent.click(screen.getByText("Old pick"));
    expect(screen.getByText("Old reasoning")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /analyze/i }));
    expect(screen.queryByText("Old reasoning")).not.toBeInTheDocument();

    await waitFor(() => expect(screen.getByText("New pick")).toBeInTheDocument());
    expect(screen.queryByText("Old reasoning")).not.toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  it("shows the API's actual error message instead of a generic one when analysis fails", async () => {
    const initial: RecommendationSet = { generatedAtUtc: "2026-01-01T00:00:00Z", waiverSuggestions: [], tradeSuggestions: [] };
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(initial) }) // initial GET
      .mockResolvedValueOnce({
        ok: false,
        status: 502,
        json: () => Promise.resolve({ error: "Claude API request failed: upstream timeout" })
      }); // POST refresh
    vi.stubGlobal("fetch", fetchMock);

    render(<DashboardScreen league={league} yourTeamName="Rhino Wranglers" />);

    await waitFor(() => expect(screen.getByText("Shohei Ohtani")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: /analyze/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.getByRole("alert")).toHaveTextContent("Claude API request failed: upstream timeout");
    expect(screen.getByRole("alert")).toHaveTextContent(/502/);

    vi.unstubAllGlobals();
  });

  it("clears the suggestion lists immediately and shows a processing indicator while re-analyzing", async () => {
    const initial: RecommendationSet = {
      generatedAtUtc: "2026-01-01T00:00:00Z",
      waiverSuggestions: [
        { type: "Waiver", summary: "Old pick", reasoning: "Old reasoning", involvedPlayerIds: ["1"], citations: [], rank: 1 }
      ],
      tradeSuggestions: []
    };
    const refreshed: RecommendationSet = { generatedAtUtc: "2026-01-02T00:00:00Z", waiverSuggestions: [], tradeSuggestions: [] };
    let resolveRefresh: (value: { ok: boolean; json: () => Promise<RecommendationSet> }) => void = () => {};
    const refreshPromise = new Promise<{ ok: boolean; json: () => Promise<RecommendationSet> }>((resolve) => {
      resolveRefresh = resolve;
    });
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(initial) }) // initial GET
      .mockReturnValueOnce(refreshPromise); // POST refresh - stays pending until resolved below
    vi.stubGlobal("fetch", fetchMock);

    render(<DashboardScreen league={league} yourTeamName="Rhino Wranglers" />);

    await waitFor(() => expect(screen.getByText("Old pick")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: /analyze/i }));

    expect(screen.queryByText("Old pick")).not.toBeInTheDocument();
    expect(screen.getByText(/generating suggestions/i)).toBeInTheDocument();

    resolveRefresh({ ok: true, json: () => Promise.resolve(refreshed) });
    await waitFor(() => expect(screen.queryByText(/generating suggestions/i)).not.toBeInTheDocument());

    vi.unstubAllGlobals();
  });

  it("lays out the roster and suggestions as two side-by-side columns, with suggestion detail nested in the suggestions column", async () => {
    const initial: RecommendationSet = {
      generatedAtUtc: "2026-01-01T00:00:00Z",
      waiverSuggestions: [
        { type: "Waiver", summary: "Pick up X", reasoning: "Hot streak", involvedPlayerIds: ["1"], citations: [], rank: 1 }
      ],
      tradeSuggestions: []
    };
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve(initial) });
    vi.stubGlobal("fetch", fetchMock);

    render(<DashboardScreen league={league} yourTeamName="Rhino Wranglers" />);

    await waitFor(() => expect(screen.getByText("Pick up X")).toBeInTheDocument());
    fireEvent.click(screen.getByText("Pick up X"));

    const rosterHeading = screen.getByRole("heading", { name: /your roster/i });
    const suggestionsHeading = screen.getByRole("heading", { name: /waiver suggestions/i });
    const detailHeading = screen.getByRole("heading", { name: "Pick up X" });

    // "Waiver Suggestions" renders inside its own nested <section> (from suggestionList()),
    // which itself sits inside the right-hand column <section> - so the column is its parent.
    const suggestionsColumn = suggestionsHeading.closest("section")?.parentElement;

    // Roster and the suggestions column render as siblings under a shared row container,
    // not stacked one after the other under the top-level page div.
    expect(rosterHeading.closest("section")?.parentElement).toBe(suggestionsColumn?.parentElement);
    // The selected suggestion's detail panel is nested inside the suggestions column, not a
    // top-level sibling of the roster/suggestions row.
    expect(suggestionsColumn).toContainElement(detailHeading);

    vi.unstubAllGlobals();
  });
});
