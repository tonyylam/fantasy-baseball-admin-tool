import { useEffect, useState, type FormEvent } from "react";
import { getSeasons, createSeason } from "../api/client";
import type { Season } from "../types";

interface Props {
  pin: string;
}

export function AdminPanel({ pin }: Props) {
  const [seasons, setSeasons] = useState<Season[]>([]);
  const [label, setLabel] = useState("");
  const [status, setStatus] = useState<"idle" | "creating">("idle");
  const [message, setMessage] = useState<string | null>(null);

  function refresh() {
    getSeasons(pin).then(setSeasons).catch(() => setSeasons([]));
  }

  useEffect(refresh, [pin]);

  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    if (!label.trim()) return;
    setStatus("creating");
    setMessage(null);
    try {
      await createSeason(pin, label.trim());
      setLabel("");
      setMessage("New season created.");
      refresh();
    } catch {
      setMessage("Couldn't create the new season. Try again.");
    } finally {
      setStatus("idle");
    }
  }

  return (
    <div className="admin-panel">
      <h1>Season Administration</h1>
      <ul>
        {seasons.map((season) => (
          <li key={season.id}>{season.label} — {season.status}</li>
        ))}
      </ul>
      <form onSubmit={handleCreate}>
        <label htmlFor="label">New season label</label>
        <input id="label" value={label} onChange={(event) => setLabel(event.target.value)} />
        <button type="submit" disabled={status === "creating" || !label.trim()}>
          {status === "creating" ? "Creating..." : "Start New Season"}
        </button>
      </form>
      {message && <p role="status">{message}</p>}
    </div>
  );
}
