# Team Navigation, Cross-Team Viewing & Existing-Contract Deletion — Design

Builds on
[2026-08-24-xlsx-keepers-import-design.md](2026-08-24-xlsx-keepers-import-design.md).
Everything there about the file-backed data store, the xlsx parser/writer,
and the import/review/export flow still holds; this document adds a
navigation layer on top and extends the data model and writer to support
deleting an existing contract.

## Purpose

Today the app only ever shows the logged-in owner's own team, and there is
no way to browse or edit any other team short of re-importing. Two things
are needed: (1) a left-side navigation menu so any authenticated user can
look at every team, with edit rights limited to the admin and the team's
own owner; (2) the ability to remove an existing contract entry — e.g. a
player who was dropped or traded and shouldn't count against the roster
anymore — without touching the source-of-truth cell values, since the
league's real workbook is still the commissioner's record.

## Non-goals

- Not adding a real router/URL-based navigation — the app stays a
  single-page, in-memory-state app per the original design's "no
  state-management or routing library" choice. The selected team/view is
  plain React state, lost on refresh (matches how the app already behaves
  today — there's no deep-linking anywhere in it).
- Not migrating already-imported data. `ExistingContractsRows` (needed for
  the delete feature — see below) is only populated by imports that happen
  *after* this ships. Data imported before this change has no row
  positions recorded for Existing Contracts, so deleting one is simply not
  possible until the admin re-imports. This matches the app's existing
  "admin re-imports each offseason" rhythm and needs no migration path.
- Not adding undo/audit history — a save just reflects the current
  checkbox state of every row (deleted or not), same as New Contracts
  today. Unchecking a previously-deleted row and saving again fully
  restores it (both in the app and in a subsequent export, since export
  always starts from the pristine stashed original bytes, never a
  previous export's output).

## Data model changes

- `ExistingContractRow` gains `Deleted: bool`.
- `StoredTeamKeepers` gains `ExistingContractsRows: IReadOnlyList<int>` —
  the same row-position tracking `NewContractsRows` already provides,
  now for the Existing Contracts side too, index-aligned with
  `ExistingContracts` exactly like `NewContractsRows`/`NewContracts` are.
- `KeeperTeamData` gains `CanEdit: bool` — computed by the endpoint (see
  below) from the caller's role and the team being viewed, not stored.
- `KeeperSubmission` gains `DeletedExistingContractIndices: IReadOnlyList<int>`
  — indices into that team's `ExistingContracts` list. A save always
  sends the *full current* set of deleted indices (not just newly-deleted
  ones), so unchecking a previously-deleted row and saving un-deletes it.

## Parser & writer changes

- `KeeperWorkbookParser.Parse` records each existing contract's row
  number as it builds the list, the same way it already does for New
  Contracts — trivial, since it's already iterating row by row.
- `KeeperWorkbookWriter` (renamed `WriteKeepers` — it no longer only
  writes New Contracts) gets a second pass per team: for every index in
  `ExistingContracts` where `Deleted` is true, apply
  `Style.Font.Strikethrough = true` to that row's H, I, J, L, M cells
  (the same columns Existing Contracts are read from). Values are left
  completely untouched — only the font style changes. Because export
  always starts from the untouched original stashed bytes (never a prior
  export's output), this is naturally idempotent: an un-deleted row's
  cells simply never get the strikethrough applied on that export.

## Authorization & API

- `GET /api/keepers?pin=...&teamId=...` — `teamId` is now a **required**
  query parameter (no more implicit "your own team" default). Any
  authenticated pin (Owner or Admin) may view any team. The endpoint
  computes `canEdit = auth.Role == Admin || teamId == auth.TeamId` and
  includes it in the response.
- `PUT /api/keepers?pin=...&teamId=...` — `teamId` required. The endpoint
  computes the same `canEdit`; if false, returns `403` before ever
  calling into `KeepersService` — `KeepersService` itself stays
  unaware of roles, same separation of concerns it has today.
- `GET /api/teams?pin=...` — new, open to any authenticated pin (not
  admin-only). Returns `{teamId, name}` for every team, same shape the
  admin import-review screen already uses. Replaces `/api/admin/teams`
  entirely (both the nav menu and the admin import-review dropdown use
  this one endpoint now) — one less duplicate route.

## Frontend changes

- A new left sidebar (rendered once logged in, alongside the existing
  Log out button) lists every team by name, admin's own gets no special
  marker, an owner's own team is labeled "My Team". Admin's sidebar gets
  one extra entry above the team list: "Admin Panel", the existing
  import/export screen — unaffected by this change other than switching
  its team-list call from the retired `/api/admin/teams` to `/api/teams`.
- View state lives in `App.tsx`: `{ kind: "team", teamId } | { kind: "admin" }`,
  defaulting to the owner's own team for an Owner, and to the Admin Panel
  for an Admin (their natural "home" — they can click into any team from
  there).
- `KeeperFormScreen` becomes the shared team-viewing/editing screen for
  every team, not just "your own": it takes a `teamId` prop (reloads when
  it changes), and renders every input/Save button/delete-checkbox only
  when `data.canEdit` is true — otherwise the page is the same layout,
  fully read-only, no Save button.
- Existing Contracts gets the same delete-checkbox treatment New
  Contracts already has: a "Delete" column (shown only when `canEdit`),
  checking it doesn't change anything until Save, struck-through styling
  while checked, and it stays checked/struck-through after a successful
  save (unlike New Contracts, which clears to blank — an existing
  contract's data is never cleared, only visually marked, matching the
  export's strikethrough-not-clear behavior).

## Error handling

- `PUT` from a non-admin, non-owning caller → `403` with a message; the
  frontend shouldn't normally construct this request at all (no Save
  button when `!canEdit`), so this is a defense-in-depth check, not a
  primary UX path.
- `GET`/`PUT` for an unknown `teamId` → `404`, same `NotFoundException`
  path already in place.
- `DeletedExistingContractIndices` containing an out-of-range index →
  `400` via the existing `KeeperValidationException` path.

## Testing

- Backend: parser test asserting `ExistingContractsRows` is recorded and
  index-aligned with `ExistingContracts`; writer test asserting a
  deleted existing contract's cells get `Strikethrough = true` with
  values unchanged, and a non-deleted row's cells are untouched;
  `KeepersService` tests for `canEdit`-gated update rejection (Forbidden)
  and for the delete-index round trip (mark deleted → save → un-delete →
  save → both states reflected); endpoint tests for the `403` path and
  for `GET /api/teams` being reachable by an Owner pin, not just Admin.
- Frontend: manual verification via the dev server (matches the
  project's existing no-automated-frontend-tests convention) — nav
  between teams, read-only rendering for another team, admin editing an
  arbitrary team, existing-contract delete/undo before and after save.
