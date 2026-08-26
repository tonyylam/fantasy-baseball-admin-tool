# Rotisserie Scoring — Design

## Summary

Replaces the app's points-per-stat scoring model with a Rotisserie-style
model: teams are ranked 1st-to-last in each scoring category and awarded
roto points by rank, summed across categories for an overall standing.
This is a correction, not an addition — the original points-per-stat
design (see
`docs/superpowers/specs/2026-08-25-fantasy-analysis-app-design.md`) never
matched how the user's actual league scores, and produced meaningless
numbers for rate-based categories (OBP, SLG, ERA, WHIP, K/9) regardless of
format, since summing `rate × points-per-unit` ignores the sample size the
rate came from.

## Goals

- Score the league the way it's actually played: Rotisserie, ranking teams
  per category rather than accumulating points per stat.
- Correctly handle rate-based categories (OBP, SLG, ERA, WHIP, K/9) by
  recombining each team's underlying counting stats (hits, walks, innings
  pitched, etc.) into a team-level rate, never by averaging individual
  players' own rates — averaging rates ignores playing time and lets a
  small sample distort a team's number.
- Remove the direction/rate-vs-counting judgment call from the user
  entirely — this was the actual pain point reported ("the points values
  for these categories doesn't make sense"). Whether a category is a rate
  stat, and whether higher or lower is better, becomes a known fact about
  the stat, not something configured by hand.
- Target waiver/trade recommendations at the categories the user's team is
  actually weak in, using real per-category production rather than a
  single blended "value" number.

## Non-Goals (this change)

- Head-to-head scoring (points or categories) — out of scope. The data
  model leaves room for a future `LeagueFormat` value, but no H2H logic is
  built, and no format toggle is shown in the UI (there would be nothing
  to toggle to).
- Quality Starts as a scoring category — confirmed unavailable from
  `statsapi.mlb.com`'s season stat groups (no `qualityStarts` field, no
  documented alternate endpoint). Would require pulling and aggregating
  per-game start logs, a materially different and larger integration.
  Dropped from the category list; can be revisited as its own project if
  ever worth the added scope.
- A dashboard panel displaying the standings table directly. Standings
  feed into the AI's reasoning context this round; they aren't rendered as
  their own UI. Can be added later.
- Any category beyond the 11 confirmed with the user (R, HR, RBI, SB, OBP,
  SLG, W, SV, ERA, WHIP, K/9). Adding a category later is a small addition
  to `RotoCategoryReference`, not a redesign.

## Architecture

No changes to the overall stack (ASP.NET Core backend, React/TypeScript
frontend, JSON file persistence, `statsapi.mlb.com` + Claude). This is a
scoring-model change confined to: the `ScoringSettings` model and store,
a new roto standings/shortlisting layer that replaces `FantasyValueRanker`,
the AI prompt payload built by `ClaudeRecommendationEngine`, and the
scoring settings screen.

## Components

### Backend

- `Models/ScoringSettings.cs` (redesigned) — `ScoringSettings` now holds
  `IReadOnlyList<string> HittingCategoryKeys`,
  `IReadOnlyList<string> PitchingCategoryKeys`, and the existing
  `IReadOnlyDictionary<string, int> RosterSlots`. No more
  `ScoringCategory(StatKey, PointsPerUnit)` — "points per unit" isn't a
  roto concept.
- `Services/RotoCategoryReference.cs` (new, static — same pattern as
  `MlbTeamAbbreviations`) — the source of truth for every supported
  category's metadata, verified against live `statsapi.mlb.com` responses
  during design:

  | Category | StatKey | Group | Direction | Rate stat? | Underlying components (rate stats only) |
  |---|---|---|---|---|---|
  | Runs | `runs` | Hitting | Higher | No | — |
  | Home Runs | `homeRuns` | Hitting | Higher | No | — |
  | RBI | `rbi` | Hitting | Higher | No | — |
  | Stolen Bases | `stolenBases` | Hitting | Higher | No | — |
  | On-Base % | `obp` | Hitting | Higher | Yes | `(hits + baseOnBalls + hitByPitch) / (atBats + baseOnBalls + hitByPitch + sacFlies)` |
  | Slugging % | `slg` | Hitting | Higher | Yes | `totalBases / atBats` |
  | Wins | `wins` | Pitching | Higher | No | — |
  | Saves | `saves` | Pitching | Higher | No | — |
  | ERA | `era` | Pitching | **Lower** | Yes | `(earnedRuns * 9) / trueInnings` |
  | WHIP | `whip` | Pitching | **Lower** | Yes | `(baseOnBalls + hits) / trueInnings` |
  | K/9 | `strikeoutsPer9Inn` | Pitching | Higher | Yes | `(strikeOuts * 9) / trueInnings` |

  `trueInnings` is not the raw `inningsPitched` field taken as a decimal —
  see "Innings Pitched Conversion" under Error Handling.

- `Services/RotoStandingsCalculator.cs` (new) —
  `ComputeStandings(League league, ScoringSettings settings, IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId) : RotoStandings`.
  For each active category: computes every team's aggregate value (sum for
  counting stats; recombined-from-components for rate stats, using every
  rostered player's relevant stat line), ranks teams 1..N respecting the
  category's direction, and awards roto points per category
  (`N - rank + 1`), splitting points evenly across a tie. Produces
  `record TeamCategoryStanding(string TeamName, string CategoryKey, decimal Value, decimal Rank, decimal RotoPoints)`
  and `record RotoStandings(IReadOnlyList<TeamCategoryStanding> Standings)`.
  `Rank`/`RotoPoints` are `decimal` (not `int`) specifically to represent
  split-tie values (e.g. two teams tied for 5th/6th both get rank 5.5).
- `Services/WeakCategoryWaiverShortlist.cs` (new, replaces
  `FantasyValueRanker` — which is deleted along with its tests, since the
  points-per-stat concept it embodied no longer applies anywhere in this
  app) — given the computed standings, the user's team name, the waiver
  pool, and `statsByPlayerId`: identifies the user's team's 2-3 weakest
  categories by current rank, then for each shortlists waiver candidates
  by raw production in that category — highest raw counting total, or (for
  rate categories) best recombined rate among candidates meeting a minimum
  sample-size floor (50 PA for hitting rate categories, 20 IP for pitching
  rate categories) so a tiny sample can't rank above a real season's
  production. Not filtered by roster position — Claude already receives
  full roster context and can flag a positional conflict in its reasoning.
- `Services/ClaudeRecommendationEngine.cs` (modified) — the prompt payload
  changes from a single per-player `fantasyValue` number to: the full
  `RotoStandings` table, the user's team's identified weak categories, and
  the weak-category-targeted shortlist with real stat values (not a
  blended score). System prompt updated to explain the roto scoring model
  so Claude's reasoning references actual category standings rather than
  a generic "value."
- `Services/RecommendationOrchestrationService.cs` (modified) — calls
  `RotoStandingsCalculator` and `WeakCategoryWaiverShortlist` in place of
  `FantasyValueRanker`.
- `Endpoints/ScoringSettingsEndpoints.cs` (modified) — add
  `GET /api/settings/scoring/categories`, returning
  `RotoCategoryReference`'s known categories as
  `[{ statKey, displayName, group }]`, so the frontend builds its checkbox
  list from the backend's source of truth instead of duplicating it (and
  automatically picks up any category added to the reference table later).

### Frontend

- `types.ts` — `ScoringSettings` updated to
  `{ hittingCategoryKeys: string[]; pitchingCategoryKeys: string[]; rosterSlots: Record<string, number> }`;
  new `ScoringCategoryOption { statKey: string; displayName: string; group: "hitting" | "pitching" }`.
- `api/client.ts` — new `getAvailableScoringCategories(): Promise<ScoringCategoryOption[]>`
  calling the new endpoint; `getScoringSettings`/`saveScoringSettings`
  updated for the new `ScoringSettings` shape.
- `screens/ScoringSettingsScreen.tsx` (redesigned) — loads the available
  categories from the backend and renders them as checkboxes grouped by
  hitting/pitching (checked state driven by whether the key is present in
  `hittingCategoryKeys`/`pitchingCategoryKeys`), replacing the old
  dynamic add/remove/points-value rows. Roster slots UI (added in the
  original design) is unchanged.

## Data Flow

1. User opens Scoring Settings: frontend loads available categories
   (`GET /api/settings/scoring/categories`) and any previously saved
   settings, pre-checking the saved keys.
2. User checks/unchecks categories and roster slots, saves —
   `PUT /api/settings/scoring` persists `HittingCategoryKeys`/
   `PitchingCategoryKeys`/`RosterSlots` exactly as selected. No direction
   or rate-vs-counting input from the user anywhere.
3. On Analyze (`POST /api/recommendations/refresh`, orchestration
   unchanged up through stats fetch/cache): `RotoStandingsCalculator`
   computes standings for every team from the current rosters and cached
   stats. `WeakCategoryWaiverShortlist` identifies the user's weakest
   categories and shortlists waiver candidates against them.
   `ClaudeRecommendationEngine` builds the prompt from standings +
   shortlist + rosters + scoring settings (as before) and calls Claude.

## Error Handling

- **Innings Pitched Conversion.** `statsapi.mlb.com` reports innings
  pitched as e.g. `"50.1"`, where the digit after the decimal point is
  *thirds of an inning* (`.0` = 0, `.1` = ⅓, `.2` = ⅔) — not a decimal
  fraction. Naively parsing this as `decimal` and using it directly would
  silently produce a slightly wrong denominator for every ERA/WHIP/K9
  recomputation. `RotoStandingsCalculator` converts via a dedicated
  helper (`whole + (fractionalDigit == 1 ? 1m/3 : fractionalDigit == 2 ? 2m/3 : 0)`)
  wherever innings are summed as a rate-stat denominator — this
  conversion happens nowhere else in the codebase today and must not be
  skipped.
- **Missing rate-stat components.** If a rostered player's stat line is
  missing one of a rate category's underlying components (e.g. no
  `sacFlies` key), that player contributes 0 for the missing component
  rather than being excluded from the team total — consistent with
  `StatLine.Stats` already being a sparse dictionary elsewhere in the app
  (Task 4's `TryConvertToDecimal` already skips non-numeric/absent
  fields).
- **A team with zero rostered players in a stat group** (e.g. no
  pitchers, though unlikely in practice) contributes 0 to every counting
  category and is excluded from rate-category ranking for that category
  (an empty-sample rate is undefined, not zero) rather than ranking it
  artificially last or throwing.
- **Ties** are handled via split roto points per the standard roto rule
  (see Components), not by arbitrary tiebreaking or throwing.

## Testing

- `RotoCategoryReference`: a smoke test confirming every category key
  referenced by the design's 11 supported categories resolves to a
  metadata entry (catches a future typo when adding a category).
- `RotoStandingsCalculator`: unit tests covering counting-stat
  aggregation, rate-stat recombination (including a dedicated test for
  the innings-pitched `.1`/`.2` conversion, since it's easy to regress),
  ranking direction (confirm ERA/WHIP rank ascending, others descending),
  and split-tie point handling.
- `WeakCategoryWaiverShortlist`: unit tests covering weak-category
  identification from standings, counting-stat shortlisting, rate-stat
  shortlisting with the minimum-sample-size floor excluding a small-sample
  outlier, and behavior when fewer than the configured minimum candidates
  exist.
- `ClaudeRecommendationEngine`: existing tests updated for the new prompt
  payload shape (standings + shortlist instead of per-player
  `fantasyValue`); assert the standings table and weak categories are
  present in the serialized prompt.
- `ScoringSettings`/store/endpoint tests updated for the new shape,
  including the new `GET /api/settings/scoring/categories` endpoint.
- Frontend `ScoringSettingsScreen` tests updated for checkbox-based
  rendering and the new settings shape.
- `FantasyValueRanker` and its tests are deleted, not deprecated in place
  — nothing in the app uses points-per-stat scoring after this change.

## Open Questions / Follow-ups (not blocking this change)

- Quality Starts, if ever wanted, needs its own project: pulling and
  aggregating per-game pitching logs from `statsapi.mlb.com`, a
  meaningfully different integration than the season-aggregate stats this
  app already fetches.
- A dashboard view of the live standings table (not just feeding it to
  Claude) was explicitly deferred — worth considering if the user wants
  to see category standings directly rather than only through AI-written
  reasoning.
- Head-to-head scoring remains unimplemented; the category-key-list shape
  of `ScoringSettings` doesn't preclude adding it, but the ranking/points
  logic for H2H (points or categories) would need its own design pass.
