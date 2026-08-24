# Fantasy Keeper App — xlsx Import/Export Design

Supersedes the "Google integration" architecture in
[2026-08-21-fantasy-keeper-app-design.md](2026-08-21-fantasy-keeper-app-design.md).
Everything there about PIN auth, the keeper-submission form's basic
shape, and "no database, plain JSON config" still holds; this document
replaces the live Google Sheets/Drive connection with an
admin-uploaded xlsx file as the source of truth.

## Purpose

The original design assumed a live service-account connection to a
Google Sheet. After building against the real workbook, the owner
decided against a live Drive connection — instead, the commissioner
(admin) will upload the current season's xlsx export from their own
league workbook each offseason. That upload becomes the app's working
data. Team owners edit their keepers through the same web form as
before; the admin can export the current state back to xlsx at any
point to save a copy or hand it back to the league's real workbook.

The real workbook (`Test 2026 Worm Burners Dynasty League Details.xlsx`,
`2026 Keepers` tab) was inspected directly to ground this design: all
15 teams live on **one tab**, stacked vertically in irregularly-sized
blocks, not one tab per team as the original config model assumed.
Each block follows a consistent internal pattern anchored by a literal
header row (`Player`, `Contract 1 or 2?`, `This Year's Salary`,
`Keeper Years Assigned`, `Existing Contracts`, `Player`, `Contract# -
yr/length`, `Last year's salary`, `League value`, `This year's
Salary`) — reliable enough to auto-locate every team's block without
hand-configured cell ranges. Team name text above that header is
messier (typos, renames across seasons) and is **not** trustworthy for
silent automatic matching.

## Non-goals

- Not re-adding a live Google Sheets/Drive connection.
- Not modeling multiple seasons or season history in the app. There is
  exactly one "current" working dataset at a time; prior seasons exist
  only as xlsx files the admin has already exported and kept
  elsewhere. (A future "read last season's EoY roster from a tab in
  the uploaded file" feature is explicitly deferred — out of scope
  here.)
- Not attempting fully-automatic team-name matching with no human
  check — a review/confirm step is mandatory on every import.
- Not changing the uploaded file's layout, styling, formulas, or any
  tab/cell the app doesn't own. Export only ever rewrites the New
  Contracts cells (`C`–`F`) inside each team's already-detected block.
- Not a general-purpose spreadsheet editor — parsing is tailored to
  this workbook's Keepers-tab structure (a stable header row per
  team block).

## Architecture

```
┌───────────────────────┐      HTTP/JSON      ┌────────────────────────────┐
│  React + TypeScript SPA │ ───────────────────▶ │  ASP.NET Core minimal API   │
│  (Vite, plain React     │                      │  (single process, serves    │
│   state)                 │◀─────────────────── │   built SPA + API)          │
└───────────────────────┘                      └──────────────┬─────────────┘
                                                                 │ ClosedXML
                                                                 ▼
                                                   ┌──────────────────────────┐
                                                   │  data/current-keepers.xlsx │
                                                   │  data/current-keepers.json │
                                                   │  (server disk, no DB)      │
                                                   └──────────────────────────┘
```

- **Frontend**: same React/Vite app. `KeeperFormScreen` loses the
  season `<select>` (single dataset now). `AdminPanel` gains
  Import (upload → review/confirm) and Export.
- **Backend**: ASP.NET Core minimal API, unchanged shape. `ISheetsClient`,
  `IDriveClient`, `Services/Google/*`, `Services/Dev/DevSheetsClient.cs`,
  `Services/Dev/DevDriveClient.cs`, `GoogleCredentialLoader`,
  `RetryPolicy`, and `SeasonService` are all removed. xlsx parsing/
  writing uses **ClosedXML** (MIT-licensed; opens/edits/saves existing
  workbooks while leaving untouched cells, formulas, and formatting
  alone).
- **Storage**: still no database. `data/current-keepers.xlsx` holds
  the last-imported workbook bytes verbatim (used as the base for
  export writes). `data/current-keepers.json` holds the parsed,
  editable state plus metadata. Both live under the same
  `_configRoot`-style disk path `JsonConfigStore` already uses.

## Data model

`config/teams.json` (teamId, name, pin) is unchanged — still the
source of truth for login. `config/seasons.json` and
`config/team-mappings/*.json` are deleted; `TeamMapping`'s
`SheetTab`/`ExistingContractsRange`/`NewContractsRange` fields and
`Season`'s `GoogleSheetId`/`status` fields are deleted.

New: **`data/current-keepers.json`**

```json
{
  "sourceFileName": "Test 2026 Worm Burners Dynasty League Details.xlsx",
  "sheetName": "2026 Keepers",
  "lastUpdatedUtc": "2026-08-24T18:12:00Z",
  "teams": {
    "b-squared": {
      "rawNameInSheet": "B Squared",
      "headerRow": 7,
      "newContractsRows": [8, 9, 10, 11, 12, 13, 14, 15],
      "newContracts": [
        { "player": "T. Story", "contractType": 1, "salary": 14, "keeperYears": 2 }
      ],
      "existingContracts": [
        { "player": "Jasson Dominguez", "contractInfo": "#1 - 2/3", "lastYearSalary": 3, "leagueValue": 1.34, "thisYearSalary": 1.34 }
      ]
    }
  }
}
```

- `newContractsRows` records the exact worksheet row for each editable
  slot, in order — this is what makes export a targeted cell write
  instead of a layout decision.
- `existingContracts` is a static snapshot captured at import time
  (read-only in the UI today; never written back).
- `lastUpdatedUtc` is bumped on every successful import confirm *and*
  every team save, and is what the admin/team UI displays as "Last
  updated."

`data/current-keepers.xlsx` is the exact bytes of the last-imported
file — export re-opens this file (not a regenerated one) and writes
only into the recorded `newContractsRows` cells, so every untouched
cell, formula, tab, and style survives round-trip.

## Import flow

1. **`POST /api/admin/keepers/import`** (admin, multipart file upload)
   → backend opens the workbook with ClosedXML, finds the tab
   containing the header-anchor pattern (a row where `C == "Player"`,
   `D` starts with `"Contract"`, `G` contains `"Existing"`), and walks
   the sheet top to bottom. For each anchor row found: the team name
   is the column-`A` value of the row immediately above it; the
   block's data rows run from the anchor row `+1` to the row before
   the *next* anchor's team-name row (or, for the last team, to the
   sheet's last used row). Within that row range, `C`–`F` become
   `newContracts`/`newContractsRows`, and `H, I, J, L, M` (columns `K`
   and `N` are unused helper/note columns) become `existingContracts`.
   No data is committed yet — the parse result is returned to the
   frontend along with, for each detected block, a best-effort
   normalized-name match against `teams.json` (case/punctuation-
   insensitive; `null` if no confident match).
2. **Review screen** (frontend, admin only): lists every detected
   block's raw sheet name next to a team dropdown (pre-selected with
   the best-effort match, or blank). The admin must resolve every
   block — pick a team or explicitly mark it "skip" — before the
   confirm button enables. Two blocks cannot be assigned to the same
   team.
3. **`POST /api/admin/keepers/import/confirm`** `{ teamAssignments }`
   → backend re-parses (or reuses a short-lived server-side cache of
   the pending parse — implementation detail for the plan) applying
   the confirmed assignments, replaces `current-keepers.json` and
   `current-keepers.xlsx` atomically, and stamps `lastUpdatedUtc`.

If no header-anchor pattern is found anywhere in the file, or the file
isn't a valid xlsx, the import request fails before ever reaching the
review screen, with a message like "Couldn't find a keepers table in
this file."

**Overwrite warning**: if a current dataset already exists, the
Import UI shows "Importing will overwrite all current keeper data,"
recommends exporting a backup first, and surfaces the Export action
right there alongside Continue/Cancel.

## Editing flow

Unchanged endpoints/shape (`GET`/`PUT` keepers by team), now backed by
`current-keepers.json` instead of live Sheets calls. Two UI additions
to `KeeperFormScreen`:

- The season `<select>` is removed entirely.
- Each New Contracts row gets a "Delete this contract" checkbox.
  Checking it does not change the row's values — they stay visible
  (styled to signal pending deletion, e.g. struck-through) and the
  checkbox can be unchecked to restore normal editing. On Save, any
  row still checked has its fields cleared to blank *before*
  submitting. This needs no backend change: a fully-blank row is
  already valid and skipped by `KeepersService`'s existing
  blank-row check, so a "deleted" row simply becomes an empty slot in
  its original position — consistent with never changing the
  worksheet's row structure.

Saving a team's keepers also bumps `lastUpdatedUtc`.

## Export flow

**`GET /api/admin/keepers/export`** (admin) → backend opens
`current-keepers.xlsx` fresh with ClosedXML, and for every team, for
every `(row, newContractRow)` pair in `newContractsRows`/
`newContracts`, writes the four values into `C{row}`–`F{row}` as
numeric cells (not strings, so any formulas referencing them — e.g.
the sheet's own `Total Contracts`/`Yrs` sums — keep working).
Everything else in the file is untouched. Streams the result back as
an xlsx download; does not mutate the stored `current-keepers.xlsx`
(re-import is still required to make an export "the new source of
truth").

## API summary

- `POST /api/admin/keepers/import` — upload + parse, returns pending
  review data (admin).
- `POST /api/admin/keepers/import/confirm` — commit reviewed import
  (admin).
- `GET /api/admin/keepers/export` — download current state as xlsx
  (admin).
- `GET /api/admin/keepers/status` — `{ lastUpdatedUtc, sourceFileName }`
  for display.
- `GET /api/keepers` / `PUT /api/keepers` — per-team read/save,
  unchanged shape, no longer take a `seasonId`.
- `POST /api/auth` — unchanged.

## Error handling

- Unrecognized/corrupt upload → `400` before the review screen.
- Review confirm with an unresolved or duplicate team assignment →
  `400`, review screen stays open.
- Same field validation as today (`contractType` is `1` or `2`,
  non-negative salary/keeperYears, no formula-injection leading
  characters) on team saves.
- Export when no dataset has ever been imported → `409` with a message
  telling the admin to import first.

## Testing

- Backend unit tests for the header-anchor parser and block-boundary
  detection, using the real workbook (sanitized/trimmed to a couple of
  teams) as a fixture — this is where a parsing bug could silently
  misattribute a team's data.
- Backend unit tests for the export writer confirming untouched cells/
  tabs/formulas survive a round trip (import then export the fixture,
  diff against original except the intended cells).
- Backend unit tests for name-matching normalization and the
  confirm-with-unresolved/duplicate-assignment error paths.
- Frontend: manual verification via the Vite dev server, consistent
  with the project's existing "no e2e framework" choice — cover the
  import warning/backup prompt, review screen, delete-checkbox
  round trip, and export download.

## Removed from the codebase

`ISheetsClient`, `IDriveClient`, `Services/Google/*`,
`Services/Dev/DevSheetsClient.cs`, `Services/Dev/DevDriveClient.cs`,
`SeasonService`, `Models/TeamMapping.cs`'s range fields, `Season`'s
Google/status fields, `config/seasons.json`,
`config/team-mappings/*.json`, and their corresponding tests
(`SeasonServiceTests`, `GoogleCredentialLoaderTests`,
`DevClientsTests`, `RetryPolicyTests`, `Fakes/FakeSheetsClient.cs`,
`Fakes/FakeDriveClient.cs`). `Google.Apis.Auth`/`Drive.v3`/`Sheets.v4`
NuGet packages are replaced with `ClosedXML`.
