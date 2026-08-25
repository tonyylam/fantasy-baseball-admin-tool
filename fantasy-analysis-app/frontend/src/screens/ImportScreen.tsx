import { useState } from "react";
import { importLeague, ApiError } from "../api/client";
import type { ImportPreview } from "../types";

interface ImportScreenProps {
  onPreviewReady: (preview: ImportPreview) => void;
}

export function ImportScreen({ onPreviewReady }: ImportScreenProps) {
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);

  async function handleImport() {
    if (!file) return;
    setImporting(true);
    setError(null);
    try {
      const preview = await importLeague(file);
      onPreviewReady(preview);
    } catch (err) {
      setError(err instanceof ApiError ? String(err.body) : "Import failed. Please try again.");
    } finally {
      setImporting(false);
    }
  }

  return (
    <div>
      <h1>Import League Roster</h1>
      <label htmlFor="roster-csv">Roster CSV (Team,Player)</label>
      <input
        id="roster-csv"
        type="file"
        accept=".csv"
        onChange={(e) => setFile(e.target.files?.[0] ?? null)}
      />
      <button onClick={handleImport} disabled={!file || importing}>
        {importing ? "Importing..." : "Import"}
      </button>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
