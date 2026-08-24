# Fantasy Keeper App

Friendly keeper-submission UI for the Worm Burners Dynasty League. The
admin uploads the league's season xlsx export as the source of truth;
team owners submit their keepers through a focused web form; the admin
can export the current state back to xlsx at any time. See
`docs/superpowers/specs/2026-08-24-xlsx-keepers-import-design.md` for
the full design (supersedes the earlier live-Google-Sheets design in
`docs/superpowers/specs/2026-08-21-fantasy-keeper-app-design.md`).

## Running locally

```bash
dotnet run --project backend/FantasyKeeper.Api
```

The app listens on `http://localhost:5080`. Try:

```bash
curl "http://localhost:5080/api/keepers?pin=1111"
```

(Returns 404 until an xlsx has been imported through the Admin panel —
see "Importing keeper data" below.)

Seeded PINs come from `config/teams.json`; the admin PIN comes from
`AdminPin` in `backend/FantasyKeeper.Api/appsettings.json` (defaults to
`0000`).

The command above starts the backend only, which serves a JSON API but
no UI. See "Frontend" below to also run/build the React app.

## Frontend

The React/TypeScript UI lives in `frontend/`. It talks to the backend over
HTTP using the `VITE_API_BASE_URL` it's built with.

**Local dev with hot reload** — run the backend and frontend as two
separate servers:

```bash
dotnet run --project backend/FantasyKeeper.Api
```

```bash
npm install --prefix frontend
npm run dev --prefix frontend
```

The Vite dev server proxies API calls to `http://localhost:5080` via the
`VITE_API_BASE_URL` set in `frontend/.env.development`. Open the URL Vite
prints (typically `http://localhost:5173`).

**Single-process deployment** — build the frontend into the backend's
static file directory, then run the backend alone:

```bash
npm run build --prefix frontend
```

This populates `backend/FantasyKeeper.Api/wwwroot` (gitignored, rebuilt
each time) with the production bundle. Once it exists:

```bash
dotnet run --project backend/FantasyKeeper.Api
```

serves both the API and the UI from `http://localhost:5080` — no separate
frontend server needed. This is the mode used in production, so the build
must be re-run whenever frontend source changes.

## Importing keeper data

1. Log in to the Admin panel with the admin PIN.
2. Under "Import season xlsx," choose the league's current xlsx export
   (the tab holding each team's Existing/New Contracts tables is
   located automatically — no configuration needed).
3. If keeper data has been imported before, you'll be warned that
   importing overwrites it; use "Export current data" there first if
   you want a backup.
4. Review the detected teams. Each row shows the name found in the
   sheet next to a dropdown of known teams (pre-filled with a best
   guess) — confirm or correct every row, or mark it "Skip," then
   click "Confirm Import."
5. Team owners can now log in with their PINs and submit keepers.
   Admin can click "Export current data" at any time to download the
   current state as xlsx.

## Config

- `config/teams.json` — `{ teamId, name, pin }` per team. Hand-edit to
  add/remove teams or change PINs.
- `AdminPin` in `appsettings.json` (or the `AdminPin` environment
  variable in production) — the commissioner's PIN.
- `data/` (gitignored) — holds the last-imported xlsx and its parsed
  state; not meant to be hand-edited.
