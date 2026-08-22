import type { AuthResult, Season, KeeperTeamData, KeeperRow } from "../types";

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

export class ApiError extends Error {
  status: number;
  body: unknown;

  constructor(status: number, body: unknown) {
    super(`API request failed with status ${status}`);
    this.status = status;
    this.body = body;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers }
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body);
  }

  return response.json() as Promise<T>;
}

export function authenticate(pin: string): Promise<AuthResult> {
  return request<AuthResult>("/api/auth", {
    method: "POST",
    body: JSON.stringify({ pin })
  });
}

export function getSeasons(pin: string): Promise<Season[]> {
  return request<Season[]>(`/api/seasons?pin=${encodeURIComponent(pin)}`);
}

export function getKeepers(pin: string, seasonId?: string): Promise<KeeperTeamData> {
  const query = seasonId ? `&seasonId=${encodeURIComponent(seasonId)}` : "";
  return request<KeeperTeamData>(`/api/keepers?pin=${encodeURIComponent(pin)}${query}`);
}

export function updateKeepers(pin: string, seasonId: string, newContracts: KeeperRow[]): Promise<KeeperTeamData> {
  return request<KeeperTeamData>(
    `/api/keepers?pin=${encodeURIComponent(pin)}&seasonId=${encodeURIComponent(seasonId)}`,
    { method: "PUT", body: JSON.stringify({ newContracts }) }
  );
}

export function createSeason(pin: string, label: string): Promise<Season> {
  return request<Season>(`/api/admin/seasons?pin=${encodeURIComponent(pin)}`, {
    method: "POST",
    body: JSON.stringify({ label })
  });
}
