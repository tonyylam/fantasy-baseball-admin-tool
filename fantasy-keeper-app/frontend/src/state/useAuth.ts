import { useCallback, useState } from "react";
import { authenticate, ApiError } from "../api/client";
import type { AuthResult } from "../types";

interface AuthState {
  pin: string;
  auth: AuthResult;
}

const STORAGE_KEY = "fantasy-keeper-auth";

function loadStoredAuth(): AuthState | null {
  const raw = sessionStorage.getItem(STORAGE_KEY);
  return raw ? (JSON.parse(raw) as AuthState) : null;
}

export function useAuth() {
  const [state, setState] = useState<AuthState | null>(loadStoredAuth);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const login = useCallback(async (pin: string) => {
    setIsLoading(true);
    setError(null);
    try {
      const auth = await authenticate(pin);
      const next = { pin, auth };
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next));
      setState(next);
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 401
          ? "That PIN wasn't recognized. Check with your commissioner."
          : "Something went wrong logging in. Try again."
      );
    } finally {
      setIsLoading(false);
    }
  }, []);

  const logout = useCallback(() => {
    sessionStorage.removeItem(STORAGE_KEY);
    setState(null);
  }, []);

  return { pin: state?.pin ?? null, auth: state?.auth ?? null, login, logout, error, isLoading };
}
