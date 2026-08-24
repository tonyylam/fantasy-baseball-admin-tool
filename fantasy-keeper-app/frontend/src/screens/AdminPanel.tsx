import { useEffect, useState } from "react";
import { getKeepersStatus, exportKeepers } from "../api/client";
import type { KeepersStatus } from "../types";

interface Props {
  pin: string;
}

export function AdminPanel({ pin }: Props) {
  const [status, setStatus] = useState<KeepersStatus | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  function refreshStatus() {
    getKeepersStatus(pin).then(setStatus).catch(() => setStatus(null));
  }

  useEffect(refreshStatus, [pin]);

  async function handleExport() {
    setMessage(null);
    try {
      const blob = await exportKeepers(pin);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = "keepers-export.xlsx";
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      setMessage("Couldn't export. Make sure keeper data has been imported.");
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

      {message && <p role="status">{message}</p>}
    </div>
  );
}
