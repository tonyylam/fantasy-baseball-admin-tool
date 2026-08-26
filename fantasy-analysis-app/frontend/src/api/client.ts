import type { ConfirmImportRequest, ImportPreview, League, RecommendationSet, ScoringCategoryOption, ScoringSettings } from "../types";

export const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export class ApiError extends Error {
  status: number;
  body: unknown;

  constructor(status: number, body: unknown) {
    super(`API request failed with status ${status}`);
    this.status = status;
    this.body = body;
  }
}

export async function importLeague(file: File): Promise<ImportPreview> {
  const formData = new FormData();
  formData.append("file", file);

  const response = await fetch(`${BASE_URL}/api/league/import`, {
    method: "POST",
    body: formData
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new ApiError(response.status, body);
  }

  return response.json() as Promise<ImportPreview>;
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
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

export function confirmImport(confirmRequest: ConfirmImportRequest): Promise<League> {
  return request<League>("/api/league/import/confirm", {
    method: "POST",
    body: JSON.stringify(confirmRequest)
  });
}

export async function getScoringSettings(): Promise<ScoringSettings | null> {
  try {
    return await request<ScoringSettings>("/api/settings/scoring");
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}

export function saveScoringSettings(settings: ScoringSettings): Promise<ScoringSettings> {
  return request<ScoringSettings>("/api/settings/scoring", {
    method: "PUT",
    body: JSON.stringify(settings)
  });
}

export function refreshRecommendations(teamName: string): Promise<RecommendationSet> {
  return request<RecommendationSet>(`/api/recommendations/refresh?teamName=${encodeURIComponent(teamName)}`, {
    method: "POST"
  });
}

export async function getRecommendations(): Promise<RecommendationSet | null> {
  try {
    return await request<RecommendationSet>("/api/recommendations");
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}

export async function getLeague(): Promise<League | null> {
  try {
    return await request<League>("/api/league");
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}

export function getAvailableScoringCategories(): Promise<ScoringCategoryOption[]> {
  return request<ScoringCategoryOption[]>("/api/settings/scoring/categories");
}
