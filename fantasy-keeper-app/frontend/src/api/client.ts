import type {
  AuthResult,
  KeeperTeamData,
  KeeperRow,
  TeamSummary,
  ImportPreview,
  BlockAssignment,
  KeepersStatus
} from "../types";

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

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

export function getKeepers(pin: string, teamId: string): Promise<KeeperTeamData> {
  return request<KeeperTeamData>(`/api/keepers?pin=${encodeURIComponent(pin)}&teamId=${encodeURIComponent(teamId)}`);
}

export function updateKeepers(
  pin: string,
  teamId: string,
  newContracts: KeeperRow[],
  deletedExistingContractIndices: number[]
): Promise<KeeperTeamData> {
  return request<KeeperTeamData>(`/api/keepers?pin=${encodeURIComponent(pin)}&teamId=${encodeURIComponent(teamId)}`, {
    method: "PUT",
    body: JSON.stringify({ newContracts, deletedExistingContractIndices })
  });
}

export function getTeams(pin: string): Promise<TeamSummary[]> {
  return request<TeamSummary[]>(`/api/teams?pin=${encodeURIComponent(pin)}`);
}

export async function importKeepers(pin: string, file: File): Promise<ImportPreview> {
  const formData = new FormData();
  formData.append("file", file);

  const response = await fetch(`${BASE_URL}/api/admin/keepers/import?pin=${encodeURIComponent(pin)}`, {
    method: "POST",
    body: formData
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body);
  }

  return response.json() as Promise<ImportPreview>;
}

export function confirmImport(pin: string, assignments: BlockAssignment[]): Promise<void> {
  return request<void>(`/api/admin/keepers/import/confirm?pin=${encodeURIComponent(pin)}`, {
    method: "POST",
    body: JSON.stringify({ assignments })
  });
}

export async function exportKeepers(pin: string): Promise<Blob> {
  const response = await fetch(`${BASE_URL}/api/admin/keepers/export?pin=${encodeURIComponent(pin)}`);

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body);
  }

  return response.blob();
}

export function getKeepersStatus(pin: string): Promise<KeepersStatus> {
  return request<KeepersStatus>(`/api/admin/keepers/status?pin=${encodeURIComponent(pin)}`);
}
