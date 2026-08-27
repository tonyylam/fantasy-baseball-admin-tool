import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ScoringSettingsScreen } from "./ScoringSettingsScreen";
import type { ScoringCategoryOption, ScoringSettings } from "../types";

describe("ScoringSettingsScreen", () => {
  const availableCategories: ScoringCategoryOption[] = [
    { statKey: "homeRuns", displayName: "Home Runs", group: "hitting" },
    { statKey: "stolenBases", displayName: "Stolen Bases", group: "hitting" },
    { statKey: "era", displayName: "ERA", group: "pitching" }
  ];

  it("loads available categories, checks a hitting category, and saves", async () => {
    const saved: ScoringSettings = {
      hittingCategoryKeys: ["homeRuns"],
      pitchingCategoryKeys: [],
      rosterSlots: {}
    };
    const fetchMock = vi.fn((url: string, options?: RequestInit) => {
      if (url.includes("/api/settings/scoring/categories")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(availableCategories) });
      }
      if (url.includes("/api/settings/scoring")) {
        if (options?.method === "PUT") {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(saved) });
        }
        return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
      }
      return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
    });
    vi.stubGlobal("fetch", fetchMock);
    const onSaved = vi.fn();

    render(<ScoringSettingsScreen onSaved={onSaved} />);
    await waitFor(() => expect(screen.getByLabelText(/home runs/i)).toBeInTheDocument());

    fireEvent.click(screen.getByLabelText(/home runs/i));
    fireEvent.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved));

    const saveCall = fetchMock.mock.calls.find(
      (call) => typeof call[0] === "string" && call[0].includes("/api/settings/scoring") && !call[0].includes("categories") && (call[1] as RequestInit)?.method === "PUT"
    )!;
    const savedBody = JSON.parse((saveCall[1] as RequestInit).body as string);
    expect(savedBody.hittingCategoryKeys).toEqual(["homeRuns"]);
    expect(savedBody.pitchingCategoryKeys).toEqual([]);

    vi.unstubAllGlobals();
  });

  it("pre-checks categories already present in previously saved settings", async () => {
    const existing: ScoringSettings = {
      hittingCategoryKeys: ["stolenBases"],
      pitchingCategoryKeys: ["era"],
      rosterSlots: {}
    };
    const fetchMock = vi.fn((url: string) => {
      if (url.includes("/api/settings/scoring/categories")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(availableCategories) });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve(existing) });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<ScoringSettingsScreen onSaved={vi.fn()} />);

    await waitFor(() => expect(screen.getByLabelText(/stolen bases/i)).toBeChecked());
    expect(screen.getByLabelText(/^era$/i)).toBeChecked();
    expect(screen.getByLabelText(/home runs/i)).not.toBeChecked();

    vi.unstubAllGlobals();
  });

  it("shows an alert instead of an empty form when the categories fetch fails", async () => {
    const fetchMock = vi.fn((url: string) => {
      if (url.includes("/api/settings/scoring/categories")) {
        return Promise.resolve({ ok: false, status: 500, json: () => Promise.resolve({}) });
      }
      return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<ScoringSettingsScreen onSaved={vi.fn()} />);

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.getByRole("alert")).toHaveTextContent(/failed to load/i);

    vi.unstubAllGlobals();
  });
});
