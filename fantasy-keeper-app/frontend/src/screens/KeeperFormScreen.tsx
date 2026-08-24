import { useCallback, useEffect, useState } from "react";
import { getKeepers, updateKeepers, ApiError } from "../api/client";
import type { KeeperRow, KeeperTeamData } from "../types";

interface Props {
  pin: string;
  teamId: string;
}

export function KeeperFormScreen({ pin, teamId }: Props) {
  const [data, setData] = useState<KeeperTeamData | null>(null);
  const [rows, setRows] = useState<KeeperRow[]>([]);
  const [deletedIndices, setDeletedIndices] = useState<Set<number>>(new Set());
  const [deletedExistingIndices, setDeletedExistingIndices] = useState<Set<number>>(new Set());
  const [status, setStatus] = useState<"idle" | "loading" | "saving" | "error">("loading");
  const [message, setMessage] = useState<string | null>(null);

  const loadKeepers = useCallback(async () => {
    setStatus("loading");
    setMessage(null);
    try {
      const result = await getKeepers(pin, teamId);
      setData(result);
      setRows(result.newContracts);
      setDeletedIndices(new Set());
      setDeletedExistingIndices(
        new Set(result.existingContracts.flatMap((row, i) => (row.deleted ? [i] : [])))
      );
      setStatus("idle");
    } catch {
      setStatus("error");
      setMessage("Couldn't load this team's keepers. Try again.");
    }
  }, [pin, teamId]);

  useEffect(() => {
    loadKeepers();
  }, [loadKeepers]);

  function updateRow(index: number, field: keyof KeeperRow, value: string) {
    setRows((prev) =>
      prev.map((row, i) => {
        if (i !== index) return row;
        if (field === "player") return { ...row, player: value };
        return { ...row, [field]: value === "" ? null : Number(value) };
      })
    );
  }

  function toggleDelete(index: number) {
    setDeletedIndices((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  }

  function toggleDeleteExisting(index: number) {
    setDeletedExistingIndices((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  }

  async function handleSave() {
    setStatus("saving");
    setMessage(null);
    try {
      const submission = rows.map((row, i) =>
        deletedIndices.has(i) ? { player: "", contractType: null, salary: null, keeperYears: null } : row
      );
      const result = await updateKeepers(pin, teamId, submission, Array.from(deletedExistingIndices));
      setData(result);
      setRows(result.newContracts);
      setDeletedIndices(new Set());
      setDeletedExistingIndices(
        new Set(result.existingContracts.flatMap((row, i) => (row.deleted ? [i] : [])))
      );
      setStatus("idle");
      setMessage("Saved.");
    } catch (err) {
      setStatus("idle");
      if (err instanceof ApiError && err.status === 400) {
        const body = err.body as { errors?: string[] };
        setMessage((body.errors ?? ["Some fields are invalid."]).join(" "));
      } else {
        setMessage("Couldn't save. Try again.");
      }
    }
  }

  if (status === "error") {
    return (
      <div>
        <p role="status">{message}</p>
        <button onClick={() => loadKeepers()}>Retry</button>
      </div>
    );
  }

  if (status === "loading" || !data) {
    return <p>Loading...</p>;
  }

  return (
    <div className="keeper-form">
      <h1>{data.teamName} — Keepers</h1>

      <h2>Existing Contracts</h2>
      <table>
        <thead>
          <tr>
            <th>Player</th><th>Contract</th><th>Last Year</th><th>League Value</th><th>This Year</th>
            {data.canEdit && <th>Delete</th>}
          </tr>
        </thead>
        <tbody>
          {data.existingContracts.map((row, i) => {
            const isDeleted = deletedExistingIndices.has(i);
            const rowStyle = isDeleted ? { textDecoration: "line-through", opacity: 0.5 } : undefined;
            return (
              <tr key={i} style={rowStyle}>
                <td>{row.player}</td>
                <td>{row.contractInfo}</td>
                <td>{row.lastYearSalary ?? ""}</td>
                <td>{row.leagueValue ?? ""}</td>
                <td>{row.thisYearSalary ?? ""}</td>
                {data.canEdit && (
                  <td>
                    <input
                      type="checkbox"
                      checked={isDeleted}
                      onChange={() => toggleDeleteExisting(i)}
                      aria-label={`Delete existing contract for row ${i + 1}`}
                    />
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>

      <h2>New Contracts</h2>
      <table>
        <thead>
          <tr>
            <th>Player</th><th>Contract 1 or 2</th><th>Salary</th><th>Keeper Years</th>
            {data.canEdit && <th>Delete</th>}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => {
            const isDeleted = deletedIndices.has(i);
            const rowStyle = isDeleted ? { textDecoration: "line-through", opacity: 0.5 } : undefined;
            return (
              <tr key={i} style={rowStyle}>
                <td>
                  <input
                    style={{ width: "100%", boxSizing: "border-box" }}
                    value={row.player}
                    disabled={!data.canEdit}
                    onChange={(e) => updateRow(i, "player", e.target.value)}
                  />
                </td>
                <td>
                  <select
                    style={{ width: "100%", boxSizing: "border-box" }}
                    value={row.contractType ?? ""}
                    disabled={!data.canEdit}
                    onChange={(e) => updateRow(i, "contractType", e.target.value)}
                  >
                    <option value="">--</option>
                    <option value="1">1</option>
                    <option value="2">2</option>
                  </select>
                </td>
                <td>
                  <input
                    style={{ width: "100%", boxSizing: "border-box" }}
                    value={row.salary ?? ""}
                    disabled={!data.canEdit}
                    onChange={(e) => updateRow(i, "salary", e.target.value)}
                  />
                </td>
                <td>
                  <input
                    style={{ width: "100%", boxSizing: "border-box" }}
                    value={row.keeperYears ?? ""}
                    disabled={!data.canEdit}
                    onChange={(e) => updateRow(i, "keeperYears", e.target.value)}
                  />
                </td>
                {data.canEdit && (
                  <td>
                    <input
                      type="checkbox"
                      checked={isDeleted}
                      onChange={() => toggleDelete(i)}
                      aria-label={`Delete contract for row ${i + 1}`}
                    />
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>

      {data.canEdit && (
        <button onClick={handleSave} disabled={status === "saving"}>
          {status === "saving" ? "Saving..." : "Save Keepers"}
        </button>
      )}
      {message && <p role="status">{message}</p>}
    </div>
  );
}
