import { useCallback, useEffect, useState } from "react";
import { getKeepers, getSeasons, updateKeepers, ApiError } from "../api/client";
import type { KeeperRow, KeeperTeamData, Season } from "../types";

interface Props {
  pin: string;
  defaultSeasonId: string;
}

export function KeeperFormScreen({ pin, defaultSeasonId }: Props) {
  const [seasons, setSeasons] = useState<Season[]>([]);
  const [seasonId, setSeasonId] = useState(defaultSeasonId);
  const [data, setData] = useState<KeeperTeamData | null>(null);
  const [rows, setRows] = useState<KeeperRow[]>([]);
  const [status, setStatus] = useState<"idle" | "loading" | "saving" | "error">("loading");
  const [message, setMessage] = useState<string | null>(null);

  const loadKeepers = useCallback(async (targetSeasonId: string) => {
    setStatus("loading");
    setMessage(null);
    try {
      const result = await getKeepers(pin, targetSeasonId);
      setData(result);
      setRows(result.newContracts);
      setStatus("idle");
    } catch {
      setStatus("error");
      setMessage("Couldn't load your keepers. Try again.");
    }
  }, [pin]);

  useEffect(() => {
    getSeasons(pin).then(setSeasons).catch(() => setSeasons([]));
  }, [pin]);

  useEffect(() => {
    loadKeepers(seasonId);
  }, [seasonId, loadKeepers]);

  function updateRow(index: number, field: keyof KeeperRow, value: string) {
    setRows((prev) =>
      prev.map((row, i) => {
        if (i !== index) return row;
        if (field === "player") return { ...row, player: value };
        return { ...row, [field]: value === "" ? null : Number(value) };
      })
    );
  }

  async function handleSave() {
    setStatus("saving");
    setMessage(null);
    try {
      const result = await updateKeepers(pin, seasonId, rows);
      setData(result);
      setRows(result.newContracts);
      setStatus("idle");
      setMessage("Saved.");
    } catch (err) {
      setStatus("idle");
      if (err instanceof ApiError && err.status === 400) {
        const body = err.body as { errors?: string[] };
        setMessage((body.errors ?? ["Some fields are invalid."]).join(" "));
      } else if (err instanceof ApiError && err.status === 409) {
        setMessage("This season is no longer open for edits.");
      } else {
        setMessage("Couldn't save. Try again.");
      }
    }
  }

  if (status === "error") {
    return (
      <div>
        <p role="status">{message}</p>
        <button onClick={() => loadKeepers(seasonId)}>Retry</button>
      </div>
    );
  }

  if (status === "loading" || !data) {
    return <p>Loading...</p>;
  }

  const readOnly = data.readOnly;

  return (
    <div className="keeper-form">
      <h1>{data.teamName} — Keepers</h1>

      <label htmlFor="season">Season</label>
      <select id="season" value={seasonId} onChange={(event) => setSeasonId(event.target.value)}>
        {seasons.map((season) => (
          <option key={season.id} value={season.id}>
            {season.label} {season.status === "archived" ? "(archived)" : ""}
          </option>
        ))}
      </select>

      <h2>Existing Contracts</h2>
      <table>
        <thead>
          <tr><th>Player</th><th>Contract</th><th>Last Year</th><th>League Value</th><th>This Year</th></tr>
        </thead>
        <tbody>
          {data.existingContracts.map((row, i) => (
            <tr key={i}>
              <td>{row.player}</td>
              <td>{row.contractInfo}</td>
              <td>{row.lastYearSalary ?? ""}</td>
              <td>{row.leagueValue ?? ""}</td>
              <td>{row.thisYearSalary ?? ""}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2>New Contracts</h2>
      <table>
        <thead>
          <tr><th>Player</th><th>Contract 1 or 2</th><th>Salary</th><th>Keeper Years</th></tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              <td><input value={row.player} disabled={readOnly} onChange={(e) => updateRow(i, "player", e.target.value)} /></td>
              <td><input value={row.contractType ?? ""} disabled={readOnly} onChange={(e) => updateRow(i, "contractType", e.target.value)} /></td>
              <td><input value={row.salary ?? ""} disabled={readOnly} onChange={(e) => updateRow(i, "salary", e.target.value)} /></td>
              <td><input value={row.keeperYears ?? ""} disabled={readOnly} onChange={(e) => updateRow(i, "keeperYears", e.target.value)} /></td>
            </tr>
          ))}
        </tbody>
      </table>

      {!readOnly && (
        <button onClick={handleSave} disabled={status === "saving"}>
          {status === "saving" ? "Saving..." : "Save Keepers"}
        </button>
      )}
      {message && <p role="status">{message}</p>}
    </div>
  );
}
