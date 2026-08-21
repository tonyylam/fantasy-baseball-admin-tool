# Fantasy Keeper App — Design

## Purpose

Worm Burners Dynasty League runs its keeper/contract process through a
large, formula-heavy Google Sheet (`2026 Worm Burners Dynasty League
Details`). Each offseason, owners must find the `2026 Keepers` tab,
locate their team's block among ~10-12 teams laid out side by side, and
manually edit the correct cells (`Player`, `Contract 1-or-2`, `This
Year's Salary`, `Keeper Years Assigned`) without disturbing formulas,
other teams' data, or formatting elsewhere in the workbook. This is
error-prone and unfriendly, especially for owners unfamiliar with the
sheet's layout.

This project is a small web app that gives each owner a focused,
friendly form for submitting their own keepers, while writing only
their specific cells back into the live Google Sheet — leaving
everything else in the workbook untouched. It also lets the
commissioner start a new season (cloning the sheet) and lets anyone
look back at prior seasons read-only.

**V1 scope is the keeper-submission workflow only.** Other workflows
(roster management, general sheet editing) are explicitly out of scope
and would be separate future projects built on the same pattern.

## Non-goals

- Not a general-purpose CSV/spreadsheet editor. It is tailored to this
  league's Keepers sheet structure.
- Not building auth/accounts. Access is via lightweight PINs, not
  Google sign-in, for owners.
- Not attempting to encode the league's seasonal rollover logic
  (turning last season's new contracts into this season's existing
  contracts, clearing fields, etc.) — that stays a manual step the
  commissioner does in Google Sheets directly. The app's "start new
  season" feature only duplicates the file; it does not manipulate
  cell contents beyond that.
- No database. All app-side state is small JSON config files on the
  server's disk.

## Architecture

```
┌───────────────────────┐      HTTP/JSON      ┌────────────────────────────┐
│  React + TypeScript SPA │ ───────────────────▶ │  ASP.NET Core minimal API   │
│  (Vite, plain React     │                      │  (single process, serves    │
│   state — no Redux/      │◀─────────────────── │   built SPA + API)          │
│   router libs needed)    │                      └──────────────┬─────────────┘
└───────────────────────┘                                        │ Google Sheets API v4
                                                                   │ + Drive API v3
                                                                   │ (service account)
                                                                   ▼
                                                     ┌────────────────────────────┐
                                                     │  Your live Google Sheet(s)  │
                                                     │  one per season             │
                                                     │  (only mapped cell ranges   │
                                                     │   ever touched)             │
                                                     └────────────────────────────┘
```

- **Frontend**: React + TypeScript via Vite. No state-management or
  routing library — the app is a handful of screens (PIN entry, keeper
  form, season switcher, admin "start new season" panel) driven by
  plain `useState`/`fetch`. Kept deliberately small per the
  "lightweight" goal.
- **Backend**: ASP.NET Core minimal API (not full MVC — no controller
  ceremony needed for this endpoint count). In production it serves
  the built React static files and the API from one process, so
  there's one thing to deploy and no CORS to configure.
- **Google integration**: a **service account** authenticates the
  backend to Google — no per-owner OAuth. One-time setup: create a GCP
  project, enable the Sheets API and Drive API, create a service
  account, download its JSON key (kept as a server secret, never
  reaches the browser), and share the league's Google Sheet with the
  service account's email as Editor.
- **No local file storage of the workbook.** The Google Sheet is the
  single source of truth; the backend reads/writes specific cell
  ranges directly via the Sheets API.

## Data & config model

All config is plain JSON on the server's disk — no database — kept
directly inspectable/editable by hand if something needs a manual fix,
matching how the league itself is run (by hand, once a year).

- **`config/seasons.json`** — array of season records:
  ```json
  { "id": "2026", "label": "2026 Season", "googleSheetId": "...", "status": "active", "createdAt": "2026-08-21T00:00:00Z" }
  ```
  Exactly one season has `status: "active"` at a time; all others are
  `"archived"`.

- **`config/teams.json`** — season-independent team records:
  ```json
  { "teamId": "b-squared", "name": "B Squared", "pin": "4821" }
  ```
  PINs are constant across seasons for a given team, per league
  preference (fewer moving parts each year).

- **`config/team-mappings/{seasonId}.json`** — per-season map of each
  team's editable range:
  ```json
  { "b-squared": { "sheetTab": "2026 Keepers", "newContractsRange": "C8:F13" } }
  ```
  When a new season is created, this file is cloned from the current
  season's mapping as a starting point, since the sheet copy has an
  identical layout at creation time. The commissioner can hand-edit it
  afterward if next year's sheet gets rearranged before use.

- **Admin config** (server secret / env var) — the admin PIN, and the
  commissioner's Google account email, used to auto-share newly
  created season sheets so they show up in the commissioner's normal
  Google Drive (service-account-owned files are otherwise invisible
  there).

## API & flows

All endpoints are JSON over HTTP.

- **`POST /api/auth`** `{ pin }` → `{ role: "owner", teamId, seasonId }`
  or `{ role: "admin" }`. Frontend holds this in memory/session storage
  for the duration of the session; no persistent login.

- **`GET /api/seasons`** → list of `{ id, label, status }` for the
  season switcher. Available to any authenticated PIN (owner or
  admin).

- **`GET /api/keepers?seasonId=...`** (defaults to active season) →
  for an owner: their team's read-only "Existing Contracts" reference
  rows plus current values in their editable "New Contracts" range.
  Requests for an archived season return the same shape with
  `readOnly: true`.

- **`PUT /api/keepers?seasonId=...`** (owner, active season only) →
  validates the payload against that team's mapped range and field
  rules (contract type is `1` or `2`; salary and keeper-years are
  numeric), then writes via `spreadsheets.values.update` scoped to
  exactly that range. Returns 409 if `seasonId` is not the active
  season.

- **`POST /api/admin/seasons`** `{ label }` (admin PIN only):
  1. Drive API `files.copy` of the active season's Sheet, titled from
     `label`.
  2. Shares the new file as Editor with the commissioner's Google
     account.
  3. Clones the current season's team-mapping config for the new
     season ID.
  4. Marks the previous season `archived` and the new one `active` in
     `seasons.json`.

  Returns the new season record.

### Error handling

- Unknown/invalid PIN → `401`.
- Edits outside a team's mapped range, targeting a non-active season,
  or failing field validation → `400`/`409` with a specific message.
- Transient Google API failures (rate limit, network blip) → one retry
  with backoff, then surfaced to the owner as a friendly "couldn't
  save, try again" message without discarding what they typed (kept in
  form state client-side).

### Concurrency

Because each team's edits target a distinct, non-overlapping cell
range, simultaneous submissions from different owners never conflict —
no locking needed. Two rapid submissions from the *same* team racing
each other is the only overlap case; last-write-wins is acceptable at
this scale.

## Testing

- **Backend unit tests** around range-validation and payload
  validation — this is where a bug could actually corrupt the wrong
  cells — using a mocked Sheets client.
- **A small number of integration tests** against a real, disposable
  test Google Sheet to exercise the actual Sheets/Drive API calls
  (read range, write range, copy file, share file).
- **Frontend** verified manually via the Vite dev server for v1; no
  e2e framework, consistent with keeping this lightweight.

## Deployment

Not decided yet. The architecture (single ASP.NET Core process serving
both API and static SPA files) keeps this decision cheap to defer —
whatever's chosen later (small cloud hosting, etc.) just needs to run
one process and hold the service-account key and admin PIN as secrets.
