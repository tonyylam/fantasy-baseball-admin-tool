import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ScoringSettingsScreen } from "./ScoringSettingsScreen";
import type { ScoringSettings } from "../types";

describe("ScoringSettingsScreen", () => {
  it("loads nothing on a fresh league, adds a category, and saves", async () => {
    const saved: ScoringSettings = {
      hittingCategories: [{ statKey: "homeRuns", pointsPerUnit: 4 }],
      pitchingCategories: [],
      rosterSlots: {}
    };
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, status: 404, json: () => Promise.resolve({}) }) // initial load
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(saved) }); // save
    vi.stubGlobal("fetch", fetchMock);
    const onSaved = vi.fn();

    render(<ScoringSettingsScreen onSaved={onSaved} />);
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByRole("button", { name: /add hitting category/i }));
    fireEvent.change(screen.getByLabelText(/hitting stat key 0/i), { target: { value: "homeRuns" } });
    fireEvent.change(screen.getByLabelText(/hitting points 0/i), { target: { value: "4" } });
    fireEvent.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved));

    // Verify the actual request body sent to the API
    const savedBody = JSON.parse(fetchMock.mock.calls[1][1].body as string);
    expect(savedBody.hittingCategories).toEqual([{ statKey: "homeRuns", pointsPerUnit: 4 }]);
    expect(savedBody.pitchingCategories).toEqual([]);
    expect(savedBody.rosterSlots).toEqual({});

    vi.unstubAllGlobals();
  });
});
