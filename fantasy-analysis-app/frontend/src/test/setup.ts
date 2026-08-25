import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// vitest.config.ts does not set `test.globals: true`, so no test file has a
// bare global `afterEach` — every file imports it from "vitest" explicitly
// where it's needed. @testing-library/react's own auto-cleanup only
// registers itself when it finds a bare global `afterEach` function, so
// without this it silently never runs, and DOM trees from earlier tests in
// the same file stay mounted and leak into later tests' queries. Register
// cleanup explicitly here instead.
afterEach(() => {
  cleanup();
});

// Node 22+ defines a global `localStorage` accessor (behind the
// --experimental-webstorage flag) that is undefined without a backing file.
// Vitest's jsdom environment only promotes window properties that are *not*
// already present on globalThis onto globalThis (see its key-whitelist
// filter), so once Node's own broken accessor exists, jsdom's real
// localStorage never gets wired up and `localStorage.clear()` in tests
// throws. Replace it with a small in-memory Storage implementation whenever
// the existing global isn't a working Storage.
if (typeof globalThis.localStorage === "undefined" || typeof globalThis.localStorage?.clear !== "function") {
  class MemoryStorage implements Storage {
    private store = new Map<string, string>();

    get length(): number {
      return this.store.size;
    }

    clear(): void {
      this.store.clear();
    }

    getItem(key: string): string | null {
      return this.store.has(key) ? this.store.get(key)! : null;
    }

    key(index: number): string | null {
      return Array.from(this.store.keys())[index] ?? null;
    }

    removeItem(key: string): void {
      this.store.delete(key);
    }

    setItem(key: string, value: string): void {
      this.store.set(key, String(value));
    }
  }

  Object.defineProperty(globalThis, "localStorage", {
    value: new MemoryStorage(),
    configurable: true,
    writable: true
  });
}
