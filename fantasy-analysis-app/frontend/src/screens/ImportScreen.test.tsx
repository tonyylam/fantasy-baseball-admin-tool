import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ImportScreen } from "./ImportScreen";
import type { ImportPreview } from "../types";

describe("ImportScreen", () => {
  it("uploads the selected CSV and reports the preview", async () => {
    const preview: ImportPreview = { teams: [] };
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(preview)
    });
    vi.stubGlobal("fetch", fetchMock);
    const onPreviewReady = vi.fn();

    render(<ImportScreen onPreviewReady={onPreviewReady} />);

    const file = new File(["Team,Player\nA,B\n"], "roster.csv", { type: "text/csv" });
    const input = screen.getByLabelText(/roster csv/i) as HTMLInputElement;
    fireEvent.change(input, { target: { files: [file] } });
    fireEvent.click(screen.getByRole("button", { name: /import/i }));

    await waitFor(() => expect(onPreviewReady).toHaveBeenCalledWith(preview));
    expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining("/api/league/import"), expect.objectContaining({ method: "POST" }));

    vi.unstubAllGlobals();
  });
});
