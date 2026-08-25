# Fantasy Analysis App — Design

## Summary

A single-user web app that imports a CSV of every team's roster in a
fantasy baseball (MLB) league, lets the user enter their league's
scoring settings, and uses Claude to generate ranked, reasoned waiver
wire pickup and trade suggestions on an interactive dashboard.

Built as a new sibling project to `fantasy-keeper-app` in the same
GitHub workspace, following its conventions: Vite + React + TypeScript
frontend, ASP.NET Core 8 minimal-API backend, single-process
deployment (backend serves the built frontend from `wwwroot`), JSON
file persistence, no database.

## Goals

- Import a CSV containing every team's roster (not just the user's).
- Let the user enter and persist their league's scoring settings
  (points/categories, roster slots) via a form.
- Compute the waiver-wire pool (unrostered players) from real MLB
  player data, not guesswork.
- Use Claude (Opus 5) to reason over real stats plus scoring settings
  to produce ranked waiver pickup and trade suggestions with written
  reasoning, using web search to fill in qualitative context (recent
  news, injuries, streaks) that structured stats don't capture.
- Present results as a persistent, revisitable interactive dashboard,
  not a one-shot report.

## Non-Goals (v1)

- Multi-user support, accounts, or authentication — this is a
  single-user tool.
- Sports other than MLB — the design keeps the stats layer swappable
  so NFL/NBA can be added later as a separate follow-on project, but
  v1 ships MLB only.
- Live/real-time draft or in-season lineup optimization — this is
  waiver/trade analysis, not a lineup-setting tool.
- Executing trades or waiver claims through any platform's API — the
  app only produces suggestions; the user acts on them manually.

## Architecture

- **Frontend**: Vite + React + TypeScript, in `frontend/`.
- **Backend**: ASP.NET Core 8 minimal API, in
  `backend/FantasyAnalysis.Api/` (+ `FantasyAnalysis.Api.Tests`).
- **Deployment**: same single-process model as `fantasy-keeper-app` —
  in dev, two servers with a Vite proxy to the backend; in production,
  `npm run build` populates the backend's `wwwroot` and the backend
  serves both API and UI from one process.
- **Persistence**: gitignored `data/` folder holding JSON files —
  imported league (all teams' rosters), scoring settings, stats cache,
  and last-generated recommendations. No database; single-user and low
  write volume don't justify one.
- **External services**:
  - MLB Stats API (`statsapi.mlb.com`) for MLB player data and stat
    lines. This is MLB's own public API — no signup, no API key, no
    cost — confirmed working during design (both the active-player
    list and per-player stat lines returned real data live). It's
    unofficial/undocumented for third-party use, which is a real risk
    (see Error Handling): it could change shape or access policy
    without notice. (Originally scoped around MySportsFeeds, but its
    personal tier turned out to require a $5/month subscription, not
    the free tier assumed during brainstorming — swapped before
    implementation for a genuinely free option.)
  - Anthropic API, model `claude-opus-5`, via the official C# SDK,
    with the `web_search_20260209` server tool enabled on the
    recommendation call.

## Components

### Backend services (interface + implementation, matching the
sibling app's `IKeepersDataStore`/`FileKeepersDataStore` pattern)

- `RosterCsvParser` — parses the uploaded CSV into teams and their
  player rosters.
- `ILeagueDataStore` / `FileLeagueDataStore` — persists and retrieves
  the imported league as JSON.
- `IScoringSettingsStore` / `FileScoringSettingsStore` — persists the
  user's scoring settings.
- `IStatsProvider` / `MlbStatsProvider` — wraps the MLB Stats API
  (`statsapi.mlb.com`). Exposes both:
  - `GetAllActivePlayersAsync()` — the full current MLB player
    universe, used to compute the waiver pool.
  - `GetPlayerStatsAsync(playerIds)` — bulk stat lines.
  Designed so a future `NflStatsProvider` / `NbaStatsProvider` can
  implement the same interface for a later sport.
- `IStatsCache` / `FileStatsCache` — caches fetched stats with a
  refresh interval (TTL) so repeated analysis doesn't re-hit
  the MLB Stats API on every request.
- `IPlayerMatchingService` — fuzzy-matches CSV player names against
  the MLB Stats API's player universe and produces a best-guess match
  per player for the review step (see Data Flow).
- `IRecommendationEngine` / `ClaudeRecommendationEngine` — assembles
  prompt context (user's roster with stats, a numerically pre-filtered
  shortlist of waiver candidates per weak position, other teams'
  rosters for trade matching, scoring settings) and calls Claude Opus
  5 with the web-search tool and structured output
  (`output_config.format`) to produce ranked, reasoned suggestions.

### Endpoints (minimal API, matching the `Endpoints/` pattern)

- `POST /api/league/import` — upload and parse the roster CSV.
- `GET /api/league/import/matches` — return auto-matched
  players pending review.
- `POST /api/league/import/confirm` — confirm/correct matches and
  persist the league.
- `GET /api/league` — current teams/rosters.
- `GET /api/settings/scoring`, `PUT /api/settings/scoring` — scoring
  settings CRUD.
- `POST /api/recommendations/refresh` — trigger a stats refresh and
  AI analysis, return recommendations.
- `GET /api/recommendations` — last generated recommendations
  (cached until refreshed).

### Frontend screens

- **Import** — CSV upload.
- **Player match review** — each parsed player next to a best-guess
  MLB Stats API match; confirm, correct via dropdown/search, or mark
  unresolved, then confirm import. Mirrors the keeper app's
  team-matching review UI.
- **Scoring settings** — a form for points/categories and roster
  slots, saved for reuse.
- **Dashboard** — the user's roster, ranked waiver suggestions, ranked
  trade suggestions, and a click-through detail view with full
  reasoning and any web-search citations per suggestion. Includes an
  "Analyze" action to regenerate recommendations on demand.

## Data Flow

1. **Import**: user uploads a CSV containing every team's roster →
   `RosterCsvParser` extracts team names and player names per team.
2. **Player matching**: `IPlayerMatchingService` fuzzy-matches each
   parsed player name against `MlbStatsProvider.GetAllActivePlayersAsync()`
   and returns a best-guess match per player. The user reviews and
   confirms/corrects matches in the UI before anything is persisted —
   this exists because a silent bad match (e.g. suffix/diacritic
   mismatches) would misclassify a rostered player as a free agent or
   attribute stats to the wrong player, corrupting both the waiver
   pool and the analysis.
3. **Persist league**: confirmed teams/rosters (with resolved player
   IDs) are saved via `FileLeagueDataStore`.
4. **Waiver pool**: computed as all active MLB players minus every
   player matched across all rostered teams.
5. **Scoring settings**: entered once via the settings form, persisted
   via `FileScoringSettingsStore`, reused on every analysis.
6. **Analysis** (triggered by the "Analyze" action /
   `POST /api/recommendations/refresh`):
   a. Refresh cached stats in bulk (respecting the cache TTL) for the
      user's roster and the full waiver pool.
   b. Numerically pre-filter the waiver pool down to a shortlist of
      top candidates per position the user is weak at, using the
      user's scoring settings to compute comparable fantasy value.
      This keeps the subsequent AI call small — Claude reasons over a
      shortlist, not the full player universe.
   c. Assemble context: user's roster with stats, the shortlist with
      stats, other teams' rosters (for trade matching), and scoring
      settings.
   d. Call Claude Opus 5 with the web-search tool enabled and a
      structured output schema; Claude reasons over positional need,
      team context, and recent news/injury/streak context it pulls
      via search, and returns ranked waiver and trade suggestions with
      written reasoning.
   e. Persist the result via a recommendations JSON file; return it to
      the frontend.
7. **Dashboard render**: frontend renders the roster and ranked
   suggestions from the persisted/returned recommendations; a fresh
   "Analyze" regenerates them.

## Error Handling

- **MLB Stats API unavailable, rate-limited, or its response shape
  changes unexpectedly** (a real risk given it's unofficial/
  undocumented for third-party use): serve cached stats with a visible
  staleness indicator. If no cache exists yet for a needed player,
  surface a clear error rather than silently falling back to an
  all-web-search analysis. Parsing code should fail loudly (a typed
  exception) on unexpected/missing fields rather than silently
  producing wrong stats.
- **Unmatched players** from the review step: excluded from analysis
  with a visible warning until the user resolves them.
- **Claude API errors**: handled via a most-specific-first exception
  chain (rate limit vs. auth vs. server error vs. connection error, per
  the Anthropic C# SDK's typed exceptions), surfaced in the UI with a
  retry action.
- **Web search tool errors**: these arrive as a result block with an
  error code rather than a raised exception; handled per-result — a
  failed search reduces the qualitative color on one suggestion, it
  doesn't fail the whole analysis request.

## Testing

- **CSV parser**: unit tests against sample CSVs, matching the sibling
  app's `KeeperWorkbookParser` test pattern.
- **Player matching service**: unit tests covering exact matches,
  fuzzy/suffix/diacritic variants, and no-match cases.
- **Stats provider**: unit tests against mocked MLB Stats API
  responses (no live calls in CI).
- **Recommendation engine**: unit tests for context/prompt assembly
  and response parsing; the Anthropic client is mocked so CI doesn't
  hit the real API or incur cost.
- **Frontend**: component tests for the import review screen and the
  dashboard, driven by mock data.

## Open Questions / Follow-ups (not blocking v1)

- The MLB Stats API is unofficial/undocumented for third-party use.
  Endpoint shapes were confirmed live during design
  (`/api/v1/sports/1/players?season={year}` for the player list,
  `/api/v1/people/{id}/stats?stats=season&group=hitting&season={year}`
  for per-player stat lines, `group=pitching` for pitchers), but there
  is no published bulk/leaderboard endpoint confirmed to work — the
  implementation fetches stats per-player, concurrently with a
  throttle, rather than assuming an unverified bulk call.
- Exact scoring-settings schema (which categories/points fields the
  form exposes) should be nailed down during implementation based on
  the user's actual league rules.
- Adding NFL/NBA as a second sport is an explicit future project, not
  part of this spec — the `IStatsProvider` abstraction exists to make
  that follow-on smaller, not to build it now.
