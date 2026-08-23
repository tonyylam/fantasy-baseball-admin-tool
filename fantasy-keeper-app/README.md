# Fantasy Keeper App

Friendly keeper-submission UI for the Worm Burners Dynasty League, backed
by the league's live Google Sheet. See
`docs/superpowers/specs/2026-08-21-fantasy-keeper-app-design.md` in the
repo root for the full design.

## Running locally (no Google account needed)

By default the backend runs against in-memory fake Sheets/Drive clients
seeded with data shaped like the real `2026 Keepers` tab — no Google
Cloud setup required to develop or demo the app.

```bash
dotnet run --project backend/FantasyKeeper.Api
```

The app listens on `http://localhost:5080`. Try:

```bash
curl "http://localhost:5080/api/keepers?pin=1111"
```

Seeded PINs: team PIN `1111` (B Squared), admin PIN `0000`.

The command above starts the backend only, which serves a JSON API but no
UI. See "Frontend" below to also run/build the React app.

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

## Going live against your real Google Sheet

1. In Google Cloud Console, create a project and enable the **Google
   Sheets API** and **Google Drive API**.
2. Create a service account, then create and download a JSON key for it.
   Save it as `fantasy-keeper-app/secrets/service-account.json` (this
   path is gitignored).
3. Open your league's Google Sheet, click Share, and add the service
   account's email address (from the key file) as an **Editor**.
4. Set these as environment variables wherever you deploy (they override
   `appsettings.json` in any environment, including `Production`, which is
   what a normal deployment runs as):

   - `Google__UseDevClients=false`
   - `Google__ServiceAccountKeyPath=/path/to/service-account.json`
   - `Google__CommissionerEmail=you@example.com`
   - `AdminPin=<pick a real admin PIN>`

   (ASP.NET Core's configuration system maps `Google__X` environment
   variables onto the same `Google:X` keys used in `appsettings.json`, via
   the `:` → `__` convention.)

   For local testing of the live-Google path specifically, you can instead
   create `backend/FantasyKeeper.Api/appsettings.Development.json`
   (gitignored) with the same keys nested under `Google`:

   ```json
   {
     "Google": {
       "UseDevClients": false,
       "ServiceAccountKeyPath": "../../secrets/service-account.json",
       "CommissionerEmail": "you@example.com"
     },
     "AdminPin": "<pick a real admin PIN>"
   }
   ```

   but note this file is **only** read when running with
   `ASPNETCORE_ENVIRONMENT=Development` — a `dotnet run` from the backend
   project directory runs as Development by default, but a real deployment
   normally runs as `Production`, where this file is never loaded. A
   production deployment must use environment variables (or
   `appsettings.Production.json`) instead, or it will silently fall back to
   `UseDevClients: true` and the default `AdminPin: "0000"` from the tracked
   `appsettings.json`.

5. Update `config/seasons.json` with the real Sheet's file ID (from its
   URL) and `config/team-mappings/season-1.json` with each team's actual
   cell ranges in the sheet.
6. Update `config/teams.json` with real team PINs.
