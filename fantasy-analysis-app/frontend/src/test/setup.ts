import "@testing-library/jest-dom/vitest";

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
