# Fantasy Analysis App

AI-assisted roster analysis for fantasy baseball leagues. Import your
league's rosters from a CSV export, set your league's scoring rules,
and get Claude-generated waiver-pickup and trade recommendations backed
by live MLB stats and web search for injury/news context.

## Running locally

The backend requires an Anthropic API key — it resolves it eagerly at
startup and will fail fast if it's missing, rather than on the first
request that needs it. Set it once via .NET user-secrets:

```bash
dotnet user-secrets set AnthropicApiKey <your-key> --project backend/FantasyAnalysis.Api
```

(In production, set the `AnthropicApiKey` environment variable
instead.)

```bash
dotnet run --project backend/FantasyAnalysis.Api
```

The app listens on `http://localhost:5080`. Try:

```bash
curl http://localhost:5080/health
```

The command above starts the backend only, which serves a JSON API but
no UI. See "Frontend" below to also run/build the React app.

## Frontend

The React/TypeScript UI lives in `frontend/`. It talks to the backend
over HTTP using the `VITE_API_BASE_URL` it's built with.

**Local dev with hot reload** — run the backend and frontend as two
separate servers:

```bash
dotnet run --project backend/FantasyAnalysis.Api
```

```bash
npm install --prefix frontend
npm run dev --prefix frontend
```

The Vite dev server sends API calls to `http://localhost:5080` via the
`VITE_API_BASE_URL` set in `frontend/.env.development`. Open the URL
Vite prints (typically `http://localhost:5173`).

**Single-process deployment** — build the frontend into the backend's
static file directory, then run the backend alone:

```bash
npm run build --prefix frontend
```

This populates `backend/FantasyAnalysis.Api/wwwroot` (gitignored,
rebuilt each time) with the production bundle. Once it exists:

```bash
dotnet run --project backend/FantasyAnalysis.Api
```

serves both the API and the UI from `http://localhost:5080` — no
separate frontend server needed. This is the mode used in production,
so the build must be re-run whenever frontend source changes.

## Importing league rosters

Rosters are imported from a CSV with a header row and one data row per
rostered player:

```csv
Team,Player
Rhino Wranglers,Shohei Ohtani
Rhino Wranglers,Mookie Betts
Diamond Dogs,Aaron Judge
```

After uploading, the review screen matches each CSV name against MLB
players and lets you confirm or correct each match; any player left
unresolved is excluded from analysis (and flagged with a warning)
until you resolve it or re-import.

## Data sources

MLB player and stat data comes from the free, unofficial
[`statsapi.mlb.com`](https://statsapi.mlb.com) API — no API key or
configuration needed.

## Config

- `AnthropicApiKey` — required. Set via `dotnet user-secrets` for
  local dev (see above) or the `AnthropicApiKey` environment variable
  in production.
- `data/` (gitignored) — holds the imported league, scoring settings,
  stats cache, and last-generated recommendations; not meant to be
  hand-edited.
