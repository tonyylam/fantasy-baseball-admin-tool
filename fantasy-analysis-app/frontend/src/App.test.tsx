import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import type { League, ScoringSettings } from "./types";

describe("App", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("shows the import screen when no league has been imported yet", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: false, status: 404, json: () => Promise.resolve({}) });
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    await waitFor(() => expect(screen.getByText(/import league roster/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });

  it("goes straight to the dashboard when league, team, and settings already exist", async () => {
    const league: League = {
      importedAtUtc: "2026-01-01T00:00:00Z",
      teams: [{ teamName: "Rhino Wranglers", players: [] }]
    };
    const settings: ScoringSettings = { hittingCategories: [], pitchingCategories: [], rosterSlots: {} };
    localStorage.setItem("yourTeamName", "Rhino Wranglers");
    const fetchMock = vi.fn((url: string) => {
      if (url.includes("/api/league") && !url.includes("import")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(league) });
      }
      if (url.includes("/api/settings/scoring")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(settings) });
      }
      if (url.includes("/api/recommendations")) {
        return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
      }
      return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    await waitFor(() => expect(screen.getByText(/rhino wranglers dashboard/i)).toBeInTheDocument());
    vi.unstubAllGlobals();
  });
});
