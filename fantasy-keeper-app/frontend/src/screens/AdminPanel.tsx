import { useEffect, useState, type ChangeEvent } from "react";
import { getTeams, importKeepers, confirmImport, getKeepersStatus, exportKeepers, ApiError } from "../api/client";
import type { TeamSummary, ImportPreview, BlockAssignment, KeepersStatus } from "../types";

interface Props {
  pin: string;
}

const SKIP = "__skip__";

export function AdminPanel({ pin }: Props) {
  const [teams, setTeams] = useState<TeamSummary[]>([]);
  const [status, setStatus] = useState<KeepersStatus | null>(null);
  const [pendingFile, setPendingFile] = useState<File | null>(null);
  const [showOverwriteWarning, setShowOverwriteWarning] = useState(false);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [assignments, setAssignments] = useState<Record<number, string>>({});
  const [phase, setPhase] = useState<"idle" | "importing" | "confirming">("idle");
  const [message, setMessage] = useState<string | null>(null);

  function refresh() {
    getTeams(pin).then(setTeams).catch(() => setTeams([]));
    getKeepersStatus(pin).then(setStatus).catch(() => setStatus(null));
  }

  useEffect(refresh, [pin]);

  async function handleExport() {
    setMessage(null);
    try {
      const blob = await exportKeepers(pin);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = "keepers-export.xlsx";
      // The anchor must be in the document and the blob URL must outlive the click tick,
      // or some browsers abort the download.
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 0);
    } catch (err) {
      // The export endpoint returns 409 only for "nothing imported yet", so re-importing is
      // the right remedy for that case alone. Any other failure (e.g. a 500 from bad stored
      // data) must not steer the admin into overwriting good data with a re-import.
      if (err instanceof ApiError && err.status === 409) {
        setMessage("Couldn't export. Make sure keeper data has been imported.");
      } else if (err instanceof ApiError) {
        const body = err.body as { error?: string } | null;
        setMessage(body?.error ?? "Couldn't export. Something went wrong on the server.");
      } else {
        setMessage("Couldn't export. Try again.");
      }
    }
  }

  function handleFileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    setPendingFile(file);
    setMessage(null);
    if (status?.lastUpdatedUtc) {
      setShowOverwriteWarning(true);
    } else {
      void startImport(file);
    }
  }

  async function startImport(file: File) {
    setShowOverwriteWarning(false);
    setPhase("importing");
    setMessage(null);
    try {
      const result = await importKeepers(pin, file);
      setPreview(result);
      const initial: Record<number, string> = {};
      for (const block of result.blocks) {
        initial[block.blockIndex] = block.suggestedTeamId ?? "";
      }
      setAssignments(initial);
    } catch {
      setMessage("Couldn't read that file. Make sure it's the league's xlsx export.");
    } finally {
      setPhase("idle");
      setPendingFile(null);
    }
  }

  function cancelReview() {
    setPreview(null);
    setAssignments({});
  }

  const chosenTeamIds = Object.values(assignments).filter((v) => v !== "" && v !== SKIP);
  const hasDuplicates = new Set(chosenTeamIds).size !== chosenTeamIds.length;
  const hasUnresolved = preview?.blocks.some((b) => assignments[b.blockIndex] === "") ?? true;
  const canConfirm = !!preview && !hasUnresolved && !hasDuplicates;

  async function handleConfirm() {
    if (!preview) return;
    setPhase("confirming");
    setMessage(null);
    try {
      const payload: BlockAssignment[] = preview.blocks.map((b) => ({
        blockIndex: b.blockIndex,
        teamId: assignments[b.blockIndex] === SKIP ? null : assignments[b.blockIndex]
      }));
      await confirmImport(pin, payload);
      setPreview(null);
      setAssignments({});
      setMessage("Import confirmed.");
      refresh();
    } catch {
      setMessage("Couldn't confirm the import. Try again.");
    } finally {
      setPhase("idle");
    }
  }

  return (
    <div className="admin-panel">
      <h1>Keepers Administration</h1>

      <p>
        {status?.lastUpdatedUtc
          ? `Last updated: ${new Date(status.lastUpdatedUtc).toLocaleString()} (from ${status.sourceFileName})`
          : "No keeper data has been imported yet."}
      </p>

      <button onClick={handleExport} disabled={!status?.lastUpdatedUtc}>Export current data</button>

      {!preview && (
        <div>
          <label htmlFor="import-file">Import season xlsx</label>
          <input id="import-file" type="file" accept=".xlsx" onChange={handleFileSelected} disabled={phase === "importing"} />
        </div>
      )}

      {showOverwriteWarning && pendingFile && (
        <div role="alertdialog">
          <p>Importing will overwrite all current keeper data. Consider exporting a backup first.</p>
          <button onClick={handleExport}>Export current data</button>
          <button onClick={() => void startImport(pendingFile)}>Continue import</button>
          <button onClick={() => { setShowOverwriteWarning(false); setPendingFile(null); }}>Cancel</button>
        </div>
      )}

      {preview && (
        <div>
          <h2>Confirm teams for "{preview.fileName}" (tab: {preview.sheetName})</h2>
          <table>
            <thead>
              <tr><th>Detected in sheet</th><th>Team</th></tr>
            </thead>
            <tbody>
              {preview.blocks.map((block) => (
                <tr key={block.blockIndex}>
                  <td>{block.rawNameInSheet}</td>
                  <td>
                    <select
                      value={assignments[block.blockIndex] ?? ""}
                      onChange={(e) =>
                        setAssignments((prev) => ({ ...prev, [block.blockIndex]: e.target.value }))
                      }
                    >
                      <option value="">-- Choose --</option>
                      <option value={SKIP}>-- Skip this block --</option>
                      {teams.map((team) => (
                        <option key={team.teamId} value={team.teamId}>{team.name}</option>
                      ))}
                    </select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {hasDuplicates && <p role="status">The same team is assigned to more than one block.</p>}
          <button onClick={handleConfirm} disabled={!canConfirm || phase === "confirming"}>
            {phase === "confirming" ? "Confirming..." : "Confirm Import"}
          </button>
          <button onClick={cancelReview}>Cancel</button>
        </div>
      )}

      {message && <p role="status">{message}</p>}
    </div>
  );
}
