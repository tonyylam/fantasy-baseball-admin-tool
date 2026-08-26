# Rotisserie Scoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the app's points-per-stat scoring model with Rotisserie-style category ranking, correctly handling rate-based categories (OBP, SLG, ERA, WHIP, K/9) by recombining each team's underlying counting stats instead of averaging individual players' rates.

**Architecture:** A new static reference table (`RotoCategoryReference`) removes direction/rate-vs-counting as something the user configures — it's a known fact about each stat. A shared math helper (`RotoStatMath`) computes a category's value (counting or rate) from any set of stat lines, reused by both team-standings computation and waiver-candidate scoring, so the recombination logic exists in exactly one place. `RotoStandingsCalculator` and `WeakCategoryWaiverShortlist` replace `FantasyValueRanker` (deleted — its points-per-stat concept no longer applies anywhere in this app), and `ClaudeRecommendationEngine`'s prompt now carries real standings and category values instead of a single blended "fantasy value" number.

**Tech Stack:** Same as the base app — .NET 8 minimal API, xUnit, React 19 + TypeScript + Vite, Vitest.

**Spec:** [docs/superpowers/specs/2026-08-26-rotisserie-scoring-design.md](../specs/2026-08-26-rotisserie-scoring-design.md)

## Global Constraints

- Rotisserie only — no head-to-head logic is built. `ScoringSettings`'s new shape (a list of active category keys, not points-per-stat) doesn't preclude adding H2H later, but nothing in this plan implements it.
- Exactly 11 supported categories: `runs`, `homeRuns`, `rbi`, `stolenBases`, `obp`, `slg` (hitting); `wins`, `saves`, `era`, `whip`, `strikeoutsPer9Inn` (pitching). Quality Starts is explicitly out of scope (unavailable from `statsapi.mlb.com`'s season stat groups).
- Direction and rate-vs-counting are never user-configured — they come from the hardcoded `RotoCategoryReference`, verified against live `statsapi.mlb.com` responses during design (see the spec's category table for exact `StatKey` values).
- Innings pitched from the MLB Stats API (e.g. `"50.1"`) must be converted correctly before being summed as a rate-stat denominator — the digit after the decimal is *thirds of an inning* (`.1` = ⅓, `.2` = ⅔), not a decimal fraction. This conversion must happen in exactly one place (`RotoStatMath.ConvertToTrueInnings`) and every caller that sums innings pitched must go through it.
- Ties in a category split roto points evenly across the tied ranks (standard roto rule) — never arbitrary tiebreaking.
- `FantasyValueRanker` and its test file are deleted as part of this plan, not left in place unused — nothing in the app uses points-per-stat scoring after this change.
- **Dependency ordering note (why the tasks are sequenced the way they are):** `FantasyValueRanker.cs` directly reads the OLD `ScoringSettings` shape (`settings.HittingCategories`, `category.PointsPerUnit`). The instant `ScoringSettings`'s shape changes, `FantasyValueRanker.cs` stops compiling — and because a single compile error anywhere fails the whole test project's build regardless of test filters, `FantasyValueRanker.cs` must be deleted (and its two direct consumers, `ClaudeRecommendationEngine` and `RecommendationOrchestrationService`, migrated) in the SAME task that changes `ScoringSettings`'s shape. `RotoStandingsCalculator` therefore takes plain `IReadOnlyList<string>` category-key lists rather than a `ScoringSettings` object, specifically so it (and `WeakCategoryWaiverShortlist`, which never depended on `ScoringSettings` at all) can be built and tested in earlier, independent tasks — before `ScoringSettings` changes shape — leaving only one task (Task 5) where the model, the engine, the orchestration wiring, and the `FantasyValueRanker` deletion all move together.

## File Structure

**Backend:**
- `Models/RotoCategory.cs` (new) — `StatDirection` enum, `RotoCategoryDefinition` record.
- `Services/RotoCategoryReference.cs` (new) — the static lookup table for all 11 categories.
- `Services/RotoStatMath.cs` (new) — `ConvertToTrueInnings`, `ComputeCategoryValue` (shared by standings + shortlisting).
- `Models/RotoStandings.cs` (new) — `TeamCategoryStanding`, `RotoStandings` records.
- `Services/RotoStandingsCalculator.cs` (new) — team category totals, ranking, tie-splitting.
- `Services/WeakCategoryWaiverShortlist.cs` (new, replaces `FantasyValueRanker`) — weak-category identification + candidate shortlisting.
- `Models/ScoringSettings.cs` (modified) — category-key-list shape instead of points-per-stat.
- `Endpoints/ScoringSettingsEndpoints.cs` (modified) — adds `GET /api/settings/scoring/categories`.
- `Services/ClaudeRecommendationEngine.cs` (modified) — prompt built from standings + shortlist instead of a ranker-computed value.
- `Services/RecommendationOrchestrationService.cs` (modified) — wires the new services in.
- `Program.cs` (modified) — DI registrations swapped.
- `Services/FantasyValueRanker.cs` (deleted).

**Frontend:**
- `types.ts` (modified) — `ScoringSettings`/`ScoringCategory` types replaced with the new shape; new `ScoringCategoryOption`.
- `api/client.ts` (modified) — new `getAvailableScoringCategories`.
- `screens/ScoringSettingsScreen.tsx` (modified) — checkbox-based category selection.

---

### Task 1: Roto category reference data

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/RotoCategory.cs`
- Create: `backend/FantasyAnalysis.Api/Services/RotoCategoryReference.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/RotoCategoryReferenceTests.cs`

**Interfaces:**
- Consumes: nothing from prior tasks.
- Produces: `enum StatDirection { HigherIsBetter, LowerIsBetter }`; `record RotoCategoryDefinition(string StatKey, string DisplayName, string Group, StatDirection Direction, bool IsRateStat, IReadOnlyList<string> NumeratorStatKeys, decimal NumeratorMultiplier, IReadOnlyList<string> DenominatorStatKeys)` — `Group` is a plain `"hitting"`/`"pitching"` string, matching `StatLine.Group`'s existing convention rather than introducing a second representation of the same concept; `static class RotoCategoryReference { static IReadOnlyDictionary<string, RotoCategoryDefinition> Categories }` keyed by `StatKey`, holding exactly the 11 supported categories. For a counting stat, `DenominatorStatKeys` is empty (meaning "just sum the numerator, no division") and `NumeratorStatKeys` is `[StatKey]` itself. Every later task that needs category metadata reads from `RotoCategoryReference.Categories`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RotoCategoryReferenceTests
{
    [Theory]
    [InlineData("runs", "hitting", StatDirection.HigherIsBetter, false)]
    [InlineData("homeRuns", "hitting", StatDirection.HigherIsBetter, false)]
    [InlineData("rbi", "hitting", StatDirection.HigherIsBetter, false)]
    [InlineData("stolenBases", "hitting", StatDirection.HigherIsBetter, false)]
    [InlineData("obp", "hitting", StatDirection.HigherIsBetter, true)]
    [InlineData("slg", "hitting", StatDirection.HigherIsBetter, true)]
    [InlineData("wins", "pitching", StatDirection.HigherIsBetter, false)]
    [InlineData("saves", "pitching", StatDirection.HigherIsBetter, false)]
    [InlineData("era", "pitching", StatDirection.LowerIsBetter, true)]
    [InlineData("whip", "pitching", StatDirection.LowerIsBetter, true)]
    [InlineData("strikeoutsPer9Inn", "pitching", StatDirection.HigherIsBetter, true)]
    public void Categories_ContainsExpectedMetadataForEveryKnownCategory(
        string statKey, string group, StatDirection direction, bool isRateStat)
    {
        var definition = RotoCategoryReference.Categories[statKey];

        Assert.Equal(group, definition.Group);
        Assert.Equal(direction, definition.Direction);
        Assert.Equal(isRateStat, definition.IsRateStat);
        Assert.Equal(statKey, definition.StatKey);
    }

    [Fact]
    public void Categories_ContainsExactlyElevenSupportedCategories()
    {
        Assert.Equal(11, RotoCategoryReference.Categories.Count);
    }

    [Fact]
    public void EraWhipAndK9_UseInningsPitchedAsDenominatorWithCorrectMultiplier()
    {
        var era = RotoCategoryReference.Categories["era"];
        Assert.Equal(new[] { "earnedRuns" }, era.NumeratorStatKeys);
        Assert.Equal(9m, era.NumeratorMultiplier);
        Assert.Equal(new[] { "inningsPitched" }, era.DenominatorStatKeys);

        var whip = RotoCategoryReference.Categories["whip"];
        Assert.Equal(new[] { "baseOnBalls", "hits" }, whip.NumeratorStatKeys);
        Assert.Equal(1m, whip.NumeratorMultiplier);
        Assert.Equal(new[] { "inningsPitched" }, whip.DenominatorStatKeys);

        var k9 = RotoCategoryReference.Categories["strikeoutsPer9Inn"];
        Assert.Equal(new[] { "strikeOuts" }, k9.NumeratorStatKeys);
        Assert.Equal(9m, k9.NumeratorMultiplier);
        Assert.Equal(new[] { "inningsPitched" }, k9.DenominatorStatKeys);
    }

    [Fact]
    public void ObpAndSlg_UseCorrectUnderlyingComponents()
    {
        var obp = RotoCategoryReference.Categories["obp"];
        Assert.Equal(new[] { "hits", "baseOnBalls", "hitByPitch" }, obp.NumeratorStatKeys);
        Assert.Equal(new[] { "atBats", "baseOnBalls", "hitByPitch", "sacFlies" }, obp.DenominatorStatKeys);

        var slg = RotoCategoryReference.Categories["slg"];
        Assert.Equal(new[] { "totalBases" }, slg.NumeratorStatKeys);
        Assert.Equal(new[] { "atBats" }, slg.DenominatorStatKeys);
    }

    [Fact]
    public void CountingStats_HaveEmptyDenominatorAndNumeratorEqualToOwnStatKey()
    {
        foreach (var key in new[] { "runs", "homeRuns", "rbi", "stolenBases", "wins", "saves" })
        {
            var definition = RotoCategoryReference.Categories[key];
            Assert.Equal(new[] { key }, definition.NumeratorStatKeys);
            Assert.Empty(definition.DenominatorStatKeys);
            Assert.Equal(1m, definition.NumeratorMultiplier);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RotoCategoryReferenceTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Implement the models and reference table**

`backend/FantasyAnalysis.Api/Models/RotoCategory.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public enum StatDirection { HigherIsBetter, LowerIsBetter }

public record RotoCategoryDefinition(
    string StatKey,
    string DisplayName,
    string Group,
    StatDirection Direction,
    bool IsRateStat,
    IReadOnlyList<string> NumeratorStatKeys,
    decimal NumeratorMultiplier,
    IReadOnlyList<string> DenominatorStatKeys);
```

`backend/FantasyAnalysis.Api/Services/RotoCategoryReference.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

/// <summary>
/// Every category key, direction, and rate-stat component set here was verified against
/// live statsapi.mlb.com responses during design - see the design spec's category table.
/// </summary>
public static class RotoCategoryReference
{
    public static readonly IReadOnlyDictionary<string, RotoCategoryDefinition> Categories = new Dictionary<string, RotoCategoryDefinition>
    {
        ["runs"] = Counting("runs", "Runs", "hitting"),
        ["homeRuns"] = Counting("homeRuns", "Home Runs", "hitting"),
        ["rbi"] = Counting("rbi", "RBI", "hitting"),
        ["stolenBases"] = Counting("stolenBases", "Stolen Bases", "hitting"),
        ["obp"] = new("obp", "On-Base %", "hitting", StatDirection.HigherIsBetter, true,
            new[] { "hits", "baseOnBalls", "hitByPitch" }, 1m,
            new[] { "atBats", "baseOnBalls", "hitByPitch", "sacFlies" }),
        ["slg"] = new("slg", "Slugging %", "hitting", StatDirection.HigherIsBetter, true,
            new[] { "totalBases" }, 1m, new[] { "atBats" }),
        ["wins"] = Counting("wins", "Wins", "pitching"),
        ["saves"] = Counting("saves", "Saves", "pitching"),
        ["era"] = new("era", "ERA", "pitching", StatDirection.LowerIsBetter, true,
            new[] { "earnedRuns" }, 9m, new[] { "inningsPitched" }),
        ["whip"] = new("whip", "WHIP", "pitching", StatDirection.LowerIsBetter, true,
            new[] { "baseOnBalls", "hits" }, 1m, new[] { "inningsPitched" }),
        ["strikeoutsPer9Inn"] = new("strikeoutsPer9Inn", "K/9", "pitching", StatDirection.HigherIsBetter, true,
            new[] { "strikeOuts" }, 9m, new[] { "inningsPitched" }),
    };

    private static RotoCategoryDefinition Counting(string statKey, string displayName, string group) =>
        new(statKey, displayName, group, StatDirection.HigherIsBetter, false, new[] { statKey }, 1m, Array.Empty<string>());
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RotoCategoryReferenceTests`
Expected: PASS (16 tests — 11 from the `[Theory]` + 5 standalone facts)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/RotoCategory.cs backend/FantasyAnalysis.Api/Services/RotoCategoryReference.cs backend/FantasyAnalysis.Api.Tests/RotoCategoryReferenceTests.cs
git commit -m "Add roto category reference data"
```

---

### Task 2: Roto stat math (shared innings conversion + category value computation)

**Files:**
- Create: `backend/FantasyAnalysis.Api/Services/RotoStatMath.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/RotoStatMathTests.cs`

**Interfaces:**
- Consumes: `RotoCategoryDefinition` (Task 1), `StatLine` (existing).
- Produces: `static class RotoStatMath` with `static decimal ConvertToTrueInnings(decimal rawValue)` and `static decimal? ComputeCategoryValue(IEnumerable<StatLine> lines, RotoCategoryDefinition definition)`. `ComputeCategoryValue` sums each numerator/denominator component across every line whose `Group` matches the definition's `Group`, applying `ConvertToTrueInnings` specifically wherever the summed key is `"inningsPitched"` (nowhere else), multiplies the numerator sum by `NumeratorMultiplier`, and divides by the denominator sum — returning the numerator alone (no division) when `DenominatorStatKeys` is empty, and `null` when a rate stat's denominator sums to exactly zero (undefined, not zero). Summation is associative, so the SAME call works for a single candidate's own stat line(s) (Task 4's shortlisting) and for every rostered player's lines concatenated together (Task 3's team totals) — this is the one place the innings-pitched conversion happens; no other file may re-implement it.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RotoStatMathTests
{
    [Theory]
    [InlineData(50.0, 50.0)]      // whole innings, no conversion needed
    [InlineData(50.1, 50.333333)] // ".1" means 1/3 of an inning, not 0.1
    [InlineData(50.2, 50.666667)] // ".2" means 2/3 of an inning, not 0.2
    public void ConvertToTrueInnings_HandlesMlbThirdsNotation(double raw, double expected)
    {
        var result = RotoStatMath.ConvertToTrueInnings((decimal)raw);

        Assert.Equal((decimal)expected, System.Math.Round(result, 6));
    }

    [Fact]
    public void ComputeCategoryValue_CountingStat_SumsAcrossLinesFromMatchingGroupOnly()
    {
        var definition = RotoCategoryReference.Categories["homeRuns"];
        var lines = new List<StatLine>
        {
            new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 20m }),
            new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 15m }),
            new("1", 2026, "pitching", new Dictionary<string, decimal> { ["homeRuns"] = 999m }) // wrong group, must be ignored
        };

        var value = RotoStatMath.ComputeCategoryValue(lines, definition);

        Assert.Equal(35m, value);
    }

    [Fact]
    public void ComputeCategoryValue_RateStatWithMultiplier_RecombinesUnderlyingComponentsAndConvertsInnings()
    {
        var definition = RotoCategoryReference.Categories["era"];
        var lines = new List<StatLine>
        {
            // 20 ER over 81.0 IP
            new("1", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 20m, ["inningsPitched"] = 81.0m }),
            // 19 ER over "50.1" (= 50 + 1/3) IP
            new("1", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 19m, ["inningsPitched"] = 50.1m })
        };

        var value = RotoStatMath.ComputeCategoryValue(lines, definition);

        // total ER = 39, total true IP = 81 + 50 + 1/3 = 131.333...; ERA = 39*9/131.333... = 2.6725...
        var expected = 39m * 9m / (131m + 1m / 3m);
        Assert.Equal(System.Math.Round(expected, 4), System.Math.Round(value!.Value, 4));
    }

    [Fact]
    public void ComputeCategoryValue_RateStatWithZeroDenominator_ReturnsNull()
    {
        var definition = RotoCategoryReference.Categories["obp"];
        var lines = new List<StatLine>(); // no stat lines at all -> denominator sums to zero

        var value = RotoStatMath.ComputeCategoryValue(lines, definition);

        Assert.Null(value);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RotoStatMathTests`
Expected: FAIL (type doesn't exist yet)

- [ ] **Step 3: Implement**

`backend/FantasyAnalysis.Api/Services/RotoStatMath.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public static class RotoStatMath
{
    // MLB reports innings pitched as e.g. "50.1", where the digit after the decimal is
    // THIRDS of an inning (.1 = 1/3, .2 = 2/3) - not a decimal fraction. Getting this wrong
    // would silently skew every ERA/WHIP/K9 computation that sums innings across players.
    public static decimal ConvertToTrueInnings(decimal rawValue)
    {
        var wholeInnings = decimal.Truncate(rawValue);
        var fractionalDigit = rawValue - wholeInnings;
        if (fractionalDigit == 0.1m) return wholeInnings + 1m / 3m;
        if (fractionalDigit == 0.2m) return wholeInnings + 2m / 3m;
        return wholeInnings;
    }

    // Computes a category's value (counting or rate) from any set of stat lines - a single
    // candidate's own line(s), or every rostered player's lines concatenated together for a
    // team total. Summation is associative, so one formula serves both callers.
    public static decimal? ComputeCategoryValue(IEnumerable<StatLine> lines, RotoCategoryDefinition definition)
    {
        var relevantLines = lines.Where(l => l.Group == definition.Group).ToList();

        var numerator = definition.NumeratorStatKeys.Sum(key => SumStatKey(relevantLines, key)) * definition.NumeratorMultiplier;

        if (definition.DenominatorStatKeys.Count == 0)
        {
            return numerator;
        }

        var denominator = definition.DenominatorStatKeys.Sum(key => SumStatKey(relevantLines, key));
        return denominator == 0 ? null : numerator / denominator;
    }

    private static decimal SumStatKey(IReadOnlyList<StatLine> lines, string statKey)
    {
        decimal total = 0;
        foreach (var line in lines)
        {
            if (!line.Stats.TryGetValue(statKey, out var rawValue)) continue;
            total += statKey == "inningsPitched" ? ConvertToTrueInnings(rawValue) : rawValue;
        }
        return total;
    }
}
```

Add `using System.Linq;` to the top of the file.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RotoStatMathTests`
Expected: PASS (5 tests — 3 `[Theory]` cases + 3 facts)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Services/RotoStatMath.cs backend/FantasyAnalysis.Api.Tests/RotoStatMathTests.cs
git commit -m "Add shared roto stat math (innings conversion + category value computation)"
```

---

### Task 3: Roto standings calculator

**Files:**
- Create: `backend/FantasyAnalysis.Api/Models/RotoStandings.cs`
- Create: `backend/FantasyAnalysis.Api/Services/RotoStandingsCalculator.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/RotoStandingsCalculatorTests.cs`

**Interfaces:**
- Consumes: `RotoStatMath.ComputeCategoryValue` (Task 2), `RotoCategoryReference`/`StatDirection` (Task 1), `League`/`TeamRoster`/`RosteredPlayer` (existing), `StatLine` (existing).
- Produces: `record TeamCategoryStanding(string TeamName, string CategoryKey, decimal Value, decimal Rank, decimal RotoPoints)`, `record RotoStandings(IReadOnlyList<TeamCategoryStanding> Standings)`, and `class RotoStandingsCalculator` with `RotoStandings ComputeStandings(League league, IReadOnlyList<string> hittingCategoryKeys, IReadOnlyList<string> pitchingCategoryKeys, IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)`. **Deliberately takes plain category-key lists, not a `ScoringSettings` object** — see the Global Constraints note on dependency ordering; this keeps `RotoStandingsCalculator` buildable before `ScoringSettings` changes shape in Task 5. `Rank`/`RotoPoints` are `decimal`, not `int`, specifically to represent split ties (two teams tied for 5th/6th both get rank `5.5`). This is the type Task 4's weak-category identification and Task 5's prompt payload both consume; Task 5's orchestration wiring calls this with `settings.HittingCategoryKeys`/`settings.PitchingCategoryKeys` extracted at the call site.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RotoStandingsCalculatorTests
{
    [Fact]
    public void ComputeStandings_CountingStat_SumsAcrossRosterAndRanksDescending()
    {
        var league = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Team A", new List<RosteredPlayer>
            {
                new("P1", "1", "P1", "OF", false),
                new("P2", "2", "P2", "OF", false)
            }),
            new("Team B", new List<RosteredPlayer> { new("P3", "3", "P3", "OF", false) })
        });
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 20m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 15m }) },
            ["3"] = new List<StatLine> { new("3", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m }) }
        };

        var standings = new RotoStandingsCalculator().ComputeStandings(league, new List<string> { "homeRuns" }, new List<string>(), statsByPlayerId);

        var teamA = standings.Standings.Single(s => s.TeamName == "Team A" && s.CategoryKey == "homeRuns");
        var teamB = standings.Standings.Single(s => s.TeamName == "Team B" && s.CategoryKey == "homeRuns");
        Assert.Equal(35m, teamA.Value);
        Assert.Equal(10m, teamB.Value);
        Assert.Equal(1m, teamA.Rank);
        Assert.Equal(2m, teamB.Rank);
        Assert.Equal(2m, teamA.RotoPoints);
        Assert.Equal(1m, teamB.RotoPoints);
    }

    [Fact]
    public void ComputeStandings_RateStat_RecombinesUnderlyingComponentsInsteadOfAveragingPlayerRates()
    {
        // A naive "average the players' own OBP" would be dragged way up by the tiny sample.
        // The correct team OBP recombines raw H/BB/HBP/AB/SF totals across the whole roster.
        var league = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Team A", new List<RosteredPlayer>
            {
                new("SmallSample", "1", "SmallSample", "OF", false),
                new("EverydayPlayer", "2", "EverydayPlayer", "OF", false)
            })
        });
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            // 1-for-1 with a walk: individually a 1.000 OBP
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["hits"] = 1m, ["baseOnBalls"] = 1m, ["hitByPitch"] = 0m, ["atBats"] = 1m, ["sacFlies"] = 0m }) },
            // 100-for-400 with 40 walks: individually a .318 OBP
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["hits"] = 100m, ["baseOnBalls"] = 40m, ["hitByPitch"] = 0m, ["atBats"] = 400m, ["sacFlies"] = 0m }) }
        };

        var standings = new RotoStandingsCalculator().ComputeStandings(league, new List<string> { "obp" }, new List<string>(), statsByPlayerId);

        var teamObp = Assert.Single(standings.Standings).Value;
        // Correct recombination: (1+100 + 1+40) / (1+400 + 1+40) = 142/442
        var expected = 142m / 442m;
        Assert.Equal(Math.Round(expected, 4), Math.Round(teamObp, 4));
        // A naive average of the two players' own OBPs, (1.000 + 0.318) / 2 ~= 0.659, would be very different.
        Assert.NotEqual(Math.Round(0.659m, 2), Math.Round(teamObp, 2));
    }

    [Fact]
    public void ComputeStandings_LowerIsBetterCategory_RanksSmallestValueFirst()
    {
        var league = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Low ERA Team", new List<RosteredPlayer> { new("Ace", "1", "Ace", "SP", true) }),
            new("High ERA Team", new List<RosteredPlayer> { new("Scherzer", "2", "Scherzer", "SP", true) })
        });
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 20m, ["inningsPitched"] = 81.0m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 39m, ["inningsPitched"] = 50.1m }) }
        };

        var standings = new RotoStandingsCalculator().ComputeStandings(league, new List<string>(), new List<string> { "era" }, statsByPlayerId);

        var lowEra = standings.Standings.Single(s => s.TeamName == "Low ERA Team");
        var highEra = standings.Standings.Single(s => s.TeamName == "High ERA Team");
        // Lower ERA is better -> Low ERA Team ranks 1st despite having the numerically smaller value.
        Assert.Equal(1m, lowEra.Rank);
        Assert.Equal(2m, highEra.Rank);
        Assert.Equal(2m, lowEra.RotoPoints);
        Assert.Equal(1m, highEra.RotoPoints);
    }

    [Fact]
    public void ComputeStandings_TiedTeams_SplitRankAndRotoPointsEvenly()
    {
        var league = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Team A", new List<RosteredPlayer> { new("P1", "1", "P1", "OF", false) }),
            new("Team B", new List<RosteredPlayer> { new("P2", "2", "P2", "OF", false) }),
            new("Team C", new List<RosteredPlayer> { new("P3", "3", "P3", "OF", false) })
        });
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 20m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m }) },
            ["3"] = new List<StatLine> { new("3", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m }) }
        };

        var standings = new RotoStandingsCalculator().ComputeStandings(league, new List<string> { "homeRuns" }, new List<string>(), statsByPlayerId);

        var teamA = standings.Standings.Single(s => s.TeamName == "Team A");
        var teamB = standings.Standings.Single(s => s.TeamName == "Team B");
        var teamC = standings.Standings.Single(s => s.TeamName == "Team C");
        Assert.Equal(1m, teamA.Rank);
        Assert.Equal(3m, teamA.RotoPoints);
        Assert.Equal(2.5m, teamB.Rank);
        Assert.Equal(2.5m, teamC.Rank);
        Assert.Equal(1.5m, teamB.RotoPoints);
        Assert.Equal(1.5m, teamC.RotoPoints);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RotoStandingsCalculatorTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Implement the models and calculator**

`backend/FantasyAnalysis.Api/Models/RotoStandings.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record TeamCategoryStanding(string TeamName, string CategoryKey, decimal Value, decimal Rank, decimal RotoPoints);

public record RotoStandings(IReadOnlyList<TeamCategoryStanding> Standings);
```

`backend/FantasyAnalysis.Api/Services/RotoStandingsCalculator.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class RotoStandingsCalculator
{
    public RotoStandings ComputeStandings(
        League league,
        IReadOnlyList<string> hittingCategoryKeys,
        IReadOnlyList<string> pitchingCategoryKeys,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)
    {
        var activeCategoryKeys = hittingCategoryKeys.Concat(pitchingCategoryKeys).ToList();
        var standings = new List<TeamCategoryStanding>();

        foreach (var categoryKey in activeCategoryKeys)
        {
            var definition = RotoCategoryReference.Categories[categoryKey];

            var teamValues = league.Teams
                .Select(t => (Team: t, Value: RotoStatMath.ComputeCategoryValue(
                    t.Players.SelectMany(p => statsByPlayerId.TryGetValue(p.PlayerId, out var lines) ? lines : Array.Empty<StatLine>()),
                    definition)))
                .Where(x => x.Value is not null)
                .ToList();

            var bestFirst = definition.Direction == StatDirection.HigherIsBetter
                ? teamValues.OrderByDescending(x => x.Value!.Value).ToList()
                : teamValues.OrderBy(x => x.Value!.Value).ToList();

            var rankByTeamName = AssignRanksWithTiesSplit(bestFirst.Select(x => (x.Team.TeamName, x.Value!.Value)).ToList());
            var teamCount = bestFirst.Count;

            foreach (var (team, value) in bestFirst.Select(x => (x.Team, x.Value!.Value)))
            {
                var rank = rankByTeamName[team.TeamName];
                standings.Add(new TeamCategoryStanding(team.TeamName, categoryKey, value, rank, teamCount - rank + 1));
            }
        }

        return new RotoStandings(standings);
    }

    private static Dictionary<string, decimal> AssignRanksWithTiesSplit(List<(string TeamName, decimal Value)> bestFirstOrder)
    {
        var result = new Dictionary<string, decimal>();
        var i = 0;
        while (i < bestFirstOrder.Count)
        {
            var j = i;
            while (j + 1 < bestFirstOrder.Count && bestFirstOrder[j + 1].Value == bestFirstOrder[i].Value)
            {
                j++;
            }
            // Ranks i+1..j+1 (1-based) are tied; each tied team gets the average of those ranks.
            var averageRank = Enumerable.Range(i + 1, j - i + 1).Average(r => (decimal)r);
            for (var k = i; k <= j; k++)
            {
                result[bestFirstOrder[k].TeamName] = averageRank;
            }
            i = j + 1;
        }
        return result;
    }
}
```

Add `using System.Linq;` to the top of the file.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter RotoStandingsCalculatorTests`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/RotoStandings.cs backend/FantasyAnalysis.Api/Services/RotoStandingsCalculator.cs backend/FantasyAnalysis.Api.Tests/RotoStandingsCalculatorTests.cs
git commit -m "Add roto standings calculator"
```

---

### Task 4: Weak-category waiver shortlist (replaces FantasyValueRanker)

**Files:**
- Create: `backend/FantasyAnalysis.Api/Services/WeakCategoryWaiverShortlist.cs`
- Test: `backend/FantasyAnalysis.Api.Tests/WeakCategoryWaiverShortlistTests.cs`

**Interfaces:**
- Consumes: `RotoStatMath.ComputeCategoryValue`/`ConvertToTrueInnings` (Task 2), `RotoCategoryReference`/`StatDirection` (Task 1), `RotoStandings`/`TeamCategoryStanding` (Task 3), `MlbPlayer`/`StatLine` (existing).
- Produces: `class WeakCategoryWaiverShortlist` with `IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> ShortlistForWeakCategories(RotoStandings standings, string yourTeamName, IReadOnlyList<MlbPlayer> waiverPool, IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)` — identifies the 3 categories where `yourTeamName`'s current rank is worst (highest rank number), then for each shortlists the top 5 waiver-pool candidates by raw production in that category (respecting direction), skipping a rate-stat candidate below a minimum sample size (50 plate appearances for hitting, 20 true innings pitched for pitching) so a small-sample outlier can't rank above a real season. Not filtered by roster position. Note this method takes NO `ScoringSettings` parameter at all — it was never coupled to that type, which is why it can be built here, before Task 5 changes `ScoringSettings`'s shape.
- **Note on `FantasyValueRanker`:** this class is `FantasyValueRanker`'s full replacement, but `FantasyValueRanker.cs` is NOT deleted in this task — `ClaudeRecommendationEngine.cs` and `RecommendationOrchestrationService.cs` (both migrated in Task 5) still construct and call it, and the whole test project must compile as one unit. `FantasyValueRanker.cs` and its test are deleted in Task 5, the only point in the sequence where every one of its consumers has already been migrated off it.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Linq;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class WeakCategoryWaiverShortlistTests
{
    private const string YourTeam = "Rhino Wranglers";

    private static RotoStandings StandingsWithWeakCategories(params string[] weakestFirstCategoryKeys)
    {
        // Build standings where YourTeam's rank gets progressively worse for each key listed,
        // so the weakest categories are exactly the ones the test names, in that order.
        var entries = weakestFirstCategoryKeys
            .Select((key, index) => new TeamCategoryStanding(YourTeam, key, 0m, Rank: 10m - index, RotoPoints: index))
            .ToList();
        return new RotoStandings(entries);
    }

    [Fact]
    public void ShortlistForWeakCategories_IdentifiesTheThreeWorstRankedCategories()
    {
        var standings = StandingsWithWeakCategories("stolenBases", "era", "whip", "homeRuns");
        var pool = new List<MlbPlayer> { new("1", "Speedy Guy", "OF", false, 108) };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["stolenBases"] = 40m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.Equal(new[] { "stolenBases", "era", "whip" }.OrderBy(x => x), shortlist.Keys.OrderBy(x => x));
    }

    [Fact]
    public void ShortlistForWeakCategories_CountingCategory_RanksCandidatesByRawProductionDescending()
    {
        var standings = StandingsWithWeakCategories("stolenBases");
        var pool = new List<MlbPlayer>
        {
            new("1", "Slow Guy", "OF", false, 108),
            new("2", "Fast Guy", "OF", false, 108)
        };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["stolenBases"] = 2m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["stolenBases"] = 25m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.Equal("Fast Guy", shortlist["stolenBases"][0].FullName);
    }

    [Fact]
    public void ShortlistForWeakCategories_LowerIsBetterCategory_RanksCandidatesAscending()
    {
        var standings = StandingsWithWeakCategories("era");
        var pool = new List<MlbPlayer>
        {
            new("1", "Bad ERA", "SP", true, 108),
            new("2", "Good ERA", "SP", true, 108)
        };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 50m, ["inningsPitched"] = 100m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 20m, ["inningsPitched"] = 100m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.Equal("Good ERA", shortlist["era"][0].FullName);
    }

    [Fact]
    public void ShortlistForWeakCategories_RateStatBelowSampleSizeFloor_IsExcluded()
    {
        var standings = StandingsWithWeakCategories("obp");
        var pool = new List<MlbPlayer>
        {
            new("1", "Tiny Sample Hot Streak", "OF", false, 108),
            new("2", "Real Season", "OF", false, 108)
        };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            // 3-for-3, perfect but only 3 plate appearances - below the 50 PA floor.
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["hits"] = 3m, ["baseOnBalls"] = 0m, ["hitByPitch"] = 0m, ["atBats"] = 3m, ["sacFlies"] = 0m, ["plateAppearances"] = 3m }) },
            // A real, unremarkable OBP over a full sample.
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["hits"] = 90m, ["baseOnBalls"] = 30m, ["hitByPitch"] = 0m, ["atBats"] = 350m, ["sacFlies"] = 0m, ["plateAppearances"] = 380m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.DoesNotContain(shortlist["obp"], p => p.FullName == "Tiny Sample Hot Streak");
        Assert.Contains(shortlist["obp"], p => p.FullName == "Real Season");
    }

    [Fact]
    public void ShortlistForWeakCategories_FewerCandidatesThanTheCap_ReturnsAllAvailableWithoutError()
    {
        var standings = StandingsWithWeakCategories("homeRuns");
        var pool = new List<MlbPlayer> { new("1", "Only Candidate", "OF", false, 108) };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 5m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.Single(shortlist["homeRuns"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter WeakCategoryWaiverShortlistTests`
Expected: FAIL (type doesn't exist yet)

- [ ] **Step 3: Implement**

`backend/FantasyAnalysis.Api/Services/WeakCategoryWaiverShortlist.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class WeakCategoryWaiverShortlist
{
    private const int WeakCategoryCount = 3;
    private const int TopCandidatesPerCategory = 5;
    private const decimal MinimumPlateAppearances = 50m;
    private const decimal MinimumInningsPitched = 20m;

    public IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> ShortlistForWeakCategories(
        RotoStandings standings,
        string yourTeamName,
        IReadOnlyList<MlbPlayer> waiverPool,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)
    {
        var weakCategoryKeys = standings.Standings
            .Where(s => s.TeamName == yourTeamName)
            .OrderByDescending(s => s.Rank)
            .Take(WeakCategoryCount)
            .Select(s => s.CategoryKey)
            .ToList();

        var result = new Dictionary<string, IReadOnlyList<MlbPlayer>>();
        foreach (var categoryKey in weakCategoryKeys)
        {
            var definition = RotoCategoryReference.Categories[categoryKey];

            var scored = waiverPool
                .Select(p => (Player: p, Lines: statsByPlayerId.TryGetValue(p.Id, out var lines) ? lines : Array.Empty<StatLine>()))
                .Where(x => !definition.IsRateStat || MeetsSampleSizeFloor(x.Lines, definition))
                .Select(x => (x.Player, Value: RotoStatMath.ComputeCategoryValue(x.Lines, definition)))
                .Where(x => x.Value is not null)
                .ToList();

            var ordered = definition.Direction == StatDirection.HigherIsBetter
                ? scored.OrderByDescending(x => x.Value!.Value)
                : scored.OrderBy(x => x.Value!.Value);

            result[categoryKey] = ordered.Take(TopCandidatesPerCategory).Select(x => x.Player).ToList();
        }

        return result;
    }

    private static bool MeetsSampleSizeFloor(IReadOnlyList<StatLine> lines, RotoCategoryDefinition definition)
    {
        var relevantLine = lines.FirstOrDefault(l => l.Group == definition.Group);
        if (relevantLine is null) return false;

        if (definition.Group == "hitting")
        {
            return relevantLine.Stats.TryGetValue("plateAppearances", out var pa) && pa >= MinimumPlateAppearances;
        }

        return relevantLine.Stats.TryGetValue("inningsPitched", out var ip)
            && RotoStatMath.ConvertToTrueInnings(ip) >= MinimumInningsPitched;
    }
}
```

Add `using System.Linq;` to the top of the file.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj --filter WeakCategoryWaiverShortlistTests`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/FantasyAnalysis.Api/Services/WeakCategoryWaiverShortlist.cs backend/FantasyAnalysis.Api.Tests/WeakCategoryWaiverShortlistTests.cs
git commit -m "Add weak-category waiver shortlist (FantasyValueRanker's replacement)"
```

---

### Task 5: ScoringSettings redesign + engine/orchestration migration + FantasyValueRanker removal

This is the one task in this plan where several files move together atomically — see the Global Constraints note on dependency ordering for why. Because of that coupling, this task can't do a clean per-change TDD red/green cycle (a red state caused by one sub-change would be masked by an unrelated compile error from another file that hasn't been updated yet). Instead: update every test file first (each written against the END state), then make every production change together, then run the full suite once at the end. This is a deliberate, documented exception to this plan's usual per-step red/green pattern — every other task in this plan follows it normally.

**Files:**
- Modify: `backend/FantasyAnalysis.Api/Models/ScoringSettings.cs`
- Modify: `backend/FantasyAnalysis.Api/Endpoints/ScoringSettingsEndpoints.cs`
- Modify: `backend/FantasyAnalysis.Api/Services/ClaudeRecommendationEngine.cs`
- Modify: `backend/FantasyAnalysis.Api/Services/RecommendationOrchestrationService.cs`
- Modify: `backend/FantasyAnalysis.Api/Program.cs`
- Delete: `backend/FantasyAnalysis.Api/Services/FantasyValueRanker.cs`
- Delete: `backend/FantasyAnalysis.Api.Tests/FantasyValueRankerTests.cs`
- Modify: `backend/FantasyAnalysis.Api.Tests/FileScoringSettingsStoreTests.cs`
- Modify: `backend/FantasyAnalysis.Api.Tests/ScoringSettingsEndpointsTests.cs`
- Modify: `backend/FantasyAnalysis.Api.Tests/ClaudeRecommendationEngineTests.cs`
- Modify: `backend/FantasyAnalysis.Api.Tests/RecommendationOrchestrationServiceTests.cs`
- Modify: `backend/FantasyAnalysis.Api.Tests/RecommendationEndpointsTests.cs`

**Interfaces:**
- Consumes: `RotoStandingsCalculator` (Task 3), `WeakCategoryWaiverShortlist` (Task 4), `RotoStatMath`/`RotoCategoryReference` (Tasks 1-2), everything `FantasyValueRanker`, `ClaudeRecommendationEngine`, and `RecommendationOrchestrationService` already consumed.
- Produces: `record ScoringSettings(IReadOnlyList<string> HittingCategoryKeys, IReadOnlyList<string> PitchingCategoryKeys, IReadOnlyDictionary<string, int> RosterSlots)` (replaces the old `ScoringCategory`/points-per-stat shape entirely); `GET /api/settings/scoring/categories` returning `[{ statKey, displayName, group }]` for every entry in `RotoCategoryReference.Categories`; `ClaudeRecommendationEngine(IRecommendationClient client)` (constructor no longer takes a ranker) with `Task<RecommendationSet> GenerateRecommendationsAsync(League league, string yourTeamName, RotoStandings standings, IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> weakCategoryShortlist, IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)`; `RecommendationOrchestrationService` with the same public surface as before (`RefreshAsync`/`GetLast`) but a new constructor taking `RotoStandingsCalculator`/`WeakCategoryWaiverShortlist` in place of `FantasyValueRanker`. `FileScoringSettingsStore`/`IScoringSettingsStore` need NO code changes — they already serialize/deserialize whatever `ScoringSettings` looks like — only their tests change. This is the last backend task; Task 6/7 are frontend-only.

- [ ] **Step 1: Update every test to the end-state shape**

Replace the body of `SaveAndLoad_RoundTrips` in `backend/FantasyAnalysis.Api.Tests/FileScoringSettingsStoreTests.cs`:

```csharp
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new FileScoringSettingsStore(_tempDir);
        var settings = new ScoringSettings(
            new List<string> { "homeRuns", "stolenBases" },
            new List<string> { "strikeoutsPer9Inn" },
            new Dictionary<string, int> { ["C"] = 1, ["1B"] = 1, ["SP"] = 5 });

        store.Save(settings);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(new[] { "homeRuns", "stolenBases" }, loaded!.HittingCategoryKeys);
        Assert.Equal(5, loaded.RosterSlots["SP"]);
    }
```

Replace `backend/FantasyAnalysis.Api.Tests/ScoringSettingsEndpointsTests.cs`:

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class ScoringSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScoringSettingsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["AnthropicApiKey"] = "test-key" });
            });
        });
    }

    [Fact]
    public async Task GetThenPut_RoundTripsSettings()
    {
        var client = _factory.CreateClient();
        var settings = new ScoringSettings(
            new List<string> { "homeRuns" },
            new List<string> { "strikeoutsPer9Inn" },
            new Dictionary<string, int> { ["C"] = 1 });

        var putResponse = await client.PutAsJsonAsync("/api/settings/scoring", settings);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/settings/scoring");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var loaded = await getResponse.Content.ReadFromJsonAsync<ScoringSettings>();
        Assert.Equal(new[] { "homeRuns" }, loaded!.HittingCategoryKeys);
    }

    [Fact]
    public async Task GetAvailableCategories_ReturnsAllElevenKnownCategoriesWithGroups()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/settings/scoring/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<ScoringCategoryOptionDto>>();
        Assert.Equal(11, categories!.Count);
        Assert.Contains(categories, c => c.StatKey == "era" && c.Group == "pitching");
        Assert.Contains(categories, c => c.StatKey == "obp" && c.Group == "hitting");
    }

    private record ScoringCategoryOptionDto(string StatKey, string DisplayName, string Group);
}
```

Replace `backend/FantasyAnalysis.Api.Tests/ClaudeRecommendationEngineTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class ClaudeRecommendationEngineTests
{
    private static readonly League League = new(
        System.DateTimeOffset.UtcNow,
        new List<TeamRoster>
        {
            new("Rhino Wranglers", new List<RosteredPlayer>
            {
                new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
            }),
            new("Sea Dogs", new List<RosteredPlayer>
            {
                new("Juan Soto", "665742", "Juan Soto", "OF", false)
            })
        });

    private static readonly RotoStandings Standings = new(new List<TeamCategoryStanding>
    {
        new("Rhino Wranglers", "homeRuns", 10m, 2m, 1m),
        new("Sea Dogs", "homeRuns", 20m, 1m, 2m)
    });

    [Fact]
    public async Task GenerateRecommendationsAsync_ParsesClientJsonIntoRecommendationSet()
    {
        var json = """
        {
          "waiverSuggestions": [
            { "summary": "Pick up X", "reasoning": "Hot streak", "involvedPlayerIds": ["123"], "citations": ["https://example.com"] }
          ],
          "tradeSuggestions": []
        }
        """;
        var fakeClient = new FakeRecommendationClient(json);
        var engine = new ClaudeRecommendationEngine(fakeClient);

        var result = await engine.GenerateRecommendationsAsync(
            League,
            "Rhino Wranglers",
            Standings,
            new Dictionary<string, IReadOnlyList<MlbPlayer>>(),
            new Dictionary<string, IReadOnlyList<StatLine>>());

        var suggestion = Assert.Single(result.WaiverSuggestions);
        Assert.Equal("Pick up X", suggestion.Summary);
        Assert.Equal(RecommendationType.Waiver, suggestion.Type);
        Assert.Equal(1, suggestion.Rank);
        Assert.Empty(result.TradeSuggestions);
    }

    [Fact]
    public async Task GenerateRecommendationsAsync_PromptMentionsTeamsAndWeakCategoryStandings()
    {
        var json = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";
        var fakeClient = new FakeRecommendationClient(json);
        var engine = new ClaudeRecommendationEngine(fakeClient);
        var shortlist = new Dictionary<string, IReadOnlyList<MlbPlayer>>
        {
            ["homeRuns"] = new List<MlbPlayer> { new("999", "Waiver Guy", "OF", false, 108) }
        };
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["999"] = new List<StatLine> { new("999", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 30m }) }
        };

        await engine.GenerateRecommendationsAsync(League, "Rhino Wranglers", Standings, shortlist, statsByPlayerId);

        Assert.Contains("Rhino Wranglers", fakeClient.LastUserPrompt);
        Assert.Contains("Sea Dogs", fakeClient.LastUserPrompt);
        Assert.Contains("Waiver Guy", fakeClient.LastUserPrompt);
        Assert.Contains("homeRuns", fakeClient.LastUserPrompt);
    }
}
```

Replace `backend/FantasyAnalysis.Api.Tests/RecommendationOrchestrationServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RecommendationOrchestrationServiceTests
{
    private static readonly League League = new(
        System.DateTimeOffset.UtcNow,
        new List<TeamRoster>
        {
            new("Rhino Wranglers", new List<RosteredPlayer>
            {
                new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
            })
        });

    private static readonly ScoringSettings Settings = new(
        new List<string> { "homeRuns" },
        new List<string>(),
        new Dictionary<string, int>());

    private static RecommendationOrchestrationService BuildService(
        League? league,
        ScoringSettings? settings,
        out FakeRecommendationDataStore recommendationStore)
    {
        return BuildService(league, settings, out recommendationStore, out _);
    }

    private static RecommendationOrchestrationService BuildService(
        League? league,
        ScoringSettings? settings,
        out FakeRecommendationDataStore recommendationStore,
        out FakeStatsCache statsCache)
    {
        var pool = new List<MlbPlayer> { new("665742", "Juan Soto", "OF", false, 121) };
        var statsProvider = new FakeStatsProvider(pool, new List<StatLine>
        {
            new("665742", SeasonClock.Current, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m })
        });
        var leagueStore = new FakeLeagueDataStore();
        if (league is not null) leagueStore.SaveLeague(league);

        var responseJson = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";
        var engine = new ClaudeRecommendationEngine(new FakeRecommendationClient(responseJson));
        recommendationStore = new FakeRecommendationDataStore();
        statsCache = new FakeStatsCache();

        return new RecommendationOrchestrationService(
            leagueStore,
            new FakeScoringSettingsStore(settings),
            statsProvider,
            statsCache,
            new WaiverPoolCalculator(),
            new RotoStandingsCalculator(),
            new WeakCategoryWaiverShortlist(),
            engine,
            recommendationStore);
    }

    [Fact]
    public async Task RefreshAsync_NoLeagueImported_ThrowsPrerequisiteException()
    {
        var service = BuildService(null, Settings, out _);

        await Assert.ThrowsAsync<RecommendationPrerequisiteException>(() => service.RefreshAsync("Rhino Wranglers"));
    }

    [Fact]
    public async Task RefreshAsync_NoScoringSettings_ThrowsPrerequisiteException()
    {
        var service = BuildService(League, null, out _);

        await Assert.ThrowsAsync<RecommendationPrerequisiteException>(() => service.RefreshAsync("Rhino Wranglers"));
    }

    [Fact]
    public async Task RefreshAsync_HappyPath_SavesAndReturnsRecommendations()
    {
        var service = BuildService(League, Settings, out var recommendationStore, out var statsCache);

        var result = await service.RefreshAsync("Rhino Wranglers");

        Assert.NotNull(result);
        Assert.Same(result, recommendationStore.Saved);
        Assert.Equal(result, service.GetLast());

        // Proves the cache-miss -> fetch -> store sequence actually ran with the right data,
        // not just that some result came back.
        Assert.NotNull(statsCache.LastStored);
        Assert.Equal(SeasonClock.Current, statsCache.LastStored!.Value.Season);
        Assert.Contains(statsCache.LastStored.Value.StatLines, s => s.PlayerId == "665742");
    }
}
```

Also fix `backend/FantasyAnalysis.Api.Tests/RecommendationEndpointsTests.cs` — an existing test that constructs `ScoringSettings` with the old shape (found during the pre-flight scan, not part of the original test-file list scanned when this plan was written). Replace its one `ScoringSettings` construction line:

```csharp
        var settings = new ScoringSettings(new List<string>(), new List<string>(), new Dictionary<string, int>());
```

(This is the only line in that file referencing the old shape — the rest of the file, including its two test methods, is unaffected and needs no other changes.)

- [ ] **Step 2: Delete FantasyValueRanker and its test**

```bash
git rm backend/FantasyAnalysis.Api/Services/FantasyValueRanker.cs backend/FantasyAnalysis.Api.Tests/FantasyValueRankerTests.cs
```

- [ ] **Step 3: Redesign the ScoringSettings model**

Replace `backend/FantasyAnalysis.Api/Models/ScoringSettings.cs`:

```csharp
namespace FantasyAnalysis.Api.Models;

public record ScoringSettings(
    IReadOnlyList<string> HittingCategoryKeys,
    IReadOnlyList<string> PitchingCategoryKeys,
    IReadOnlyDictionary<string, int> RosterSlots);
```

- [ ] **Step 4: Add the available-categories endpoint**

Add to `backend/FantasyAnalysis.Api/Endpoints/ScoringSettingsEndpoints.cs`, inside `MapScoringSettingsEndpoints`, after the existing `MapPut` block:

```csharp
        app.MapGet("/api/settings/scoring/categories", () =>
        {
            var categories = RotoCategoryReference.Categories.Values
                .Select(c => new { statKey = c.StatKey, displayName = c.DisplayName, group = c.Group })
                .ToList();
            return Results.Ok(categories);
        });
```

Add `using System.Linq;` to the top of the file if not already present.

- [ ] **Step 5: Rewrite ClaudeRecommendationEngine**

Replace `backend/FantasyAnalysis.Api/Services/ClaudeRecommendationEngine.cs`:

```csharp
using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class ClaudeRecommendationEngine
{
    private readonly IRecommendationClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ClaudeRecommendationEngine(IRecommendationClient client)
    {
        _client = client;
    }

    public async Task<RecommendationSet> GenerateRecommendationsAsync(
        League league,
        string yourTeamName,
        RotoStandings standings,
        IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> weakCategoryShortlist,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)
    {
        var systemPrompt =
            "You are a fantasy baseball analyst for a Rotisserie-style league: every team is ranked " +
            "1st-to-last in each scoring category and awarded points by rank, summed for an overall " +
            "standing. Given the league's current category standings, one team's weakest categories " +
            "with a shortlist of available waiver-wire candidates strong in those categories, and " +
            "every team's roster, recommend waiver pickups and trades that would improve the given " +
            "team's standing. Use web search to check recent news, injuries, or performance trends " +
            "that could affect a recommendation, and cite any URLs you used. Respond only with JSON " +
            "matching the provided schema.";

        var userPrompt = BuildUserPrompt(league, yourTeamName, standings, weakCategoryShortlist, statsByPlayerId);

        var json = await _client.GetRecommendationsJsonAsync(systemPrompt, userPrompt);
        return ParseResponse(json);
    }

    private static string BuildUserPrompt(
        League league,
        string yourTeamName,
        RotoStandings standings,
        IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> weakCategoryShortlist,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)
    {
        object RosterPayload(RosteredPlayer p) => new { playerId = p.PlayerId, fullName = p.PlayerFullName, position = p.Position };

        var yourTeam = league.Teams.First(t => t.TeamName == yourTeamName);
        var otherTeams = league.Teams.Where(t => t.TeamName != yourTeamName);

        var payload = new
        {
            yourTeam = new { teamName = yourTeam.TeamName, players = yourTeam.Players.Select(RosterPayload) },
            otherTeams = otherTeams.Select(t => new { teamName = t.TeamName, players = t.Players.Select(RosterPayload) }),
            standings = standings.Standings.Select(s => new
            {
                teamName = s.TeamName,
                category = s.CategoryKey,
                value = s.Value,
                rank = s.Rank,
                rotoPoints = s.RotoPoints
            }),
            weakCategoryShortlist = weakCategoryShortlist.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(p => new
                {
                    playerId = p.Id,
                    fullName = p.FullName,
                    position = p.Position,
                    categoryValue = RotoStatMath.ComputeCategoryValue(
                        statsByPlayerId.TryGetValue(p.Id, out var lines) ? lines : Array.Empty<StatLine>(),
                        RotoCategoryReference.Categories[kv.Key])
                }))
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static RecommendationSet ParseResponse(string json)
    {
        ClaudeRecommendationSetDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ClaudeRecommendationSetDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new RecommendationClientException("Claude's recommendation response was not valid JSON.", ex);
        }

        if (dto is null)
        {
            throw new RecommendationClientException("Claude's recommendation response deserialized to null.");
        }

        IReadOnlyList<Recommendation> ToRecommendations(List<ClaudeRecommendationDto>? items, RecommendationType type) =>
            (items ?? new List<ClaudeRecommendationDto>())
                .Select((item, index) => new Recommendation(
                    type,
                    item.Summary,
                    item.Reasoning,
                    item.InvolvedPlayerIds ?? new List<string>(),
                    item.Citations ?? new List<string>(),
                    index + 1))
                .ToList();

        return new RecommendationSet(
            DateTimeOffset.UtcNow,
            ToRecommendations(dto.WaiverSuggestions, RecommendationType.Waiver),
            ToRecommendations(dto.TradeSuggestions, RecommendationType.Trade));
    }

    private class ClaudeRecommendationDto
    {
        public string Summary { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public List<string>? InvolvedPlayerIds { get; set; }
        public List<string>? Citations { get; set; }
    }

    private class ClaudeRecommendationSetDto
    {
        public List<ClaudeRecommendationDto>? WaiverSuggestions { get; set; }
        public List<ClaudeRecommendationDto>? TradeSuggestions { get; set; }
    }
}
```

Add `using System.Linq;` to the top of the file.

- [ ] **Step 6: Rewire RecommendationOrchestrationService**

Replace `backend/FantasyAnalysis.Api/Services/RecommendationOrchestrationService.cs`:

```csharp
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class RecommendationOrchestrationService
{
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);

    private readonly ILeagueDataStore _leagueStore;
    private readonly IScoringSettingsStore _settingsStore;
    private readonly IStatsProvider _statsProvider;
    private readonly IStatsCache _statsCache;
    private readonly WaiverPoolCalculator _waiverPoolCalculator;
    private readonly RotoStandingsCalculator _standingsCalculator;
    private readonly WeakCategoryWaiverShortlist _shortlistBuilder;
    private readonly ClaudeRecommendationEngine _engine;
    private readonly IRecommendationDataStore _recommendationStore;

    public RecommendationOrchestrationService(
        ILeagueDataStore leagueStore,
        IScoringSettingsStore settingsStore,
        IStatsProvider statsProvider,
        IStatsCache statsCache,
        WaiverPoolCalculator waiverPoolCalculator,
        RotoStandingsCalculator standingsCalculator,
        WeakCategoryWaiverShortlist shortlistBuilder,
        ClaudeRecommendationEngine engine,
        IRecommendationDataStore recommendationStore)
    {
        _leagueStore = leagueStore;
        _settingsStore = settingsStore;
        _statsProvider = statsProvider;
        _statsCache = statsCache;
        _waiverPoolCalculator = waiverPoolCalculator;
        _standingsCalculator = standingsCalculator;
        _shortlistBuilder = shortlistBuilder;
        _engine = engine;
        _recommendationStore = recommendationStore;
    }

    public async Task<RecommendationSet> RefreshAsync(string yourTeamName)
    {
        var league = _leagueStore.LoadLeague()
            ?? throw new RecommendationPrerequisiteException("A league must be imported before generating recommendations.");
        var settings = _settingsStore.Load()
            ?? throw new RecommendationPrerequisiteException("Scoring settings must be saved before generating recommendations.");

        var season = SeasonClock.Current;
        var allPlayers = await _statsProvider.GetAllActivePlayersAsync(season);
        var waiverPool = _waiverPoolCalculator.ComputeWaiverPool(allPlayers, league);

        var statLines = _statsCache.GetIfFresh(season, CacheMaxAge);
        if (statLines is null)
        {
            var rosteredIds = league.Teams.SelectMany(t => t.Players).Select(p => p.PlayerId);
            var idsNeeded = rosteredIds.Concat(waiverPool.Select(p => p.Id)).Distinct().ToList();
            statLines = await _statsProvider.GetPlayerStatsAsync(idsNeeded, season);
            _statsCache.Store(season, statLines);
        }

        var statsByPlayerId = statLines
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StatLine>)g.ToList());

        var standings = _standingsCalculator.ComputeStandings(league, settings.HittingCategoryKeys, settings.PitchingCategoryKeys, statsByPlayerId);
        var shortlist = _shortlistBuilder.ShortlistForWeakCategories(standings, yourTeamName, waiverPool, statsByPlayerId);

        var recommendations = await _engine.GenerateRecommendationsAsync(league, yourTeamName, standings, shortlist, statsByPlayerId);
        _recommendationStore.Save(recommendations);
        return recommendations;
    }

    public RecommendationSet? GetLast() => _recommendationStore.Load();
}
```

- [ ] **Step 7: Update Program.cs's DI registration**

In `backend/FantasyAnalysis.Api/Program.cs`, replace the line `builder.Services.AddSingleton<FantasyValueRanker>();` with:

```csharp
builder.Services.AddSingleton<RotoStandingsCalculator>();
builder.Services.AddSingleton<WeakCategoryWaiverShortlist>();
```

- [ ] **Step 8: Run the full backend suite**

Run: `dotnet test backend/FantasyAnalysis.Api.Tests/FantasyAnalysis.Api.Tests.csproj`
Expected: PASS, all tests — this is the first point since Task 1 where a full-suite run is meaningful; confirm the count matches what's expected (every prior task's tests, plus this task's, minus the two `FantasyValueRankerTests.cs` cases that no longer exist).

- [ ] **Step 9: Commit**

```bash
git add backend/FantasyAnalysis.Api/Models/ScoringSettings.cs backend/FantasyAnalysis.Api/Endpoints/ScoringSettingsEndpoints.cs backend/FantasyAnalysis.Api/Services/ClaudeRecommendationEngine.cs backend/FantasyAnalysis.Api/Services/RecommendationOrchestrationService.cs backend/FantasyAnalysis.Api/Program.cs backend/FantasyAnalysis.Api.Tests/FileScoringSettingsStoreTests.cs backend/FantasyAnalysis.Api.Tests/ScoringSettingsEndpointsTests.cs backend/FantasyAnalysis.Api.Tests/ClaudeRecommendationEngineTests.cs backend/FantasyAnalysis.Api.Tests/RecommendationOrchestrationServiceTests.cs backend/FantasyAnalysis.Api.Tests/RecommendationEndpointsTests.cs
git commit -m "Redesign scoring settings, migrate recommendation engine/orchestration to roto standings, remove FantasyValueRanker"
```

---

### Task 6: Frontend types + API client for category keys

**Files:**
- Modify: `frontend/src/types.ts`
- Modify: `frontend/src/api/client.ts`
- Modify: `frontend/src/App.test.tsx`

**Interfaces:**
- Consumes: nothing new from the backend beyond what Task 5 already shipped (`GET /api/settings/scoring/categories`, and the new `ScoringSettings` JSON shape from the existing `GET`/`PUT /api/settings/scoring`).
- Produces: `ScoringSettings { hittingCategoryKeys: string[]; pitchingCategoryKeys: string[]; rosterSlots: Record<string, number> }` (replaces the old `ScoringCategory`/`hittingCategories` shape); `ScoringCategoryOption { statKey: string; displayName: string; group: "hitting" | "pitching" }`; `getAvailableScoringCategories(): Promise<ScoringCategoryOption[]>` in `api/client.ts`, calling `GET /api/settings/scoring/categories` via the existing `request<T>` helper. `getScoringSettings`/`saveScoringSettings` keep their existing signatures — only the `ScoringSettings` type they carry changes. Task 7's checkbox-based `ScoringSettingsScreen` consumes all three of these.

- [ ] **Step 1: Update the shared types**

Replace `ScoringCategory`/`ScoringSettings` in `frontend/src/types.ts`:

```typescript
export interface ScoringSettings {
  hittingCategoryKeys: string[];
  pitchingCategoryKeys: string[];
  rosterSlots: Record<string, number>;
}

export interface ScoringCategoryOption {
  statKey: string;
  displayName: string;
  group: "hitting" | "pitching";
}
```

- [ ] **Step 2: Add the new API function**

Append to `frontend/src/api/client.ts` (add `ScoringCategoryOption` to the existing type import from `../types`):

```typescript
export function getAvailableScoringCategories(): Promise<ScoringCategoryOption[]> {
  return request<ScoringCategoryOption[]>("/api/settings/scoring/categories");
}
```

- [ ] **Step 3: Fix the now-broken ScoringSettings fixtures in App.test.tsx**

`frontend/src/App.test.tsx` has two `const settings: ScoringSettings = { hittingCategories: [], pitchingCategories: [], rosterSlots: {} };` literals (one per test) left over from the old shape. Replace both with:

```typescript
const settings: ScoringSettings = { hittingCategoryKeys: [], pitchingCategoryKeys: [], rosterSlots: {} };
```

- [ ] **Step 4: Run the full frontend suite and build**

Run: `npm run test --prefix frontend`
Expected: FAIL — `ScoringSettingsScreen.tsx` and `ScoringSettingsScreen.test.tsx` still reference the old `ScoringCategory`/`hittingCategories` shape (they're rewritten in Task 7). Confirm the ONLY failures/build errors trace back to those two files.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/types.ts frontend/src/api/client.ts frontend/src/App.test.tsx
git commit -m "Update frontend types and API client for roto category-key scoring settings"
```

---

### Task 7: Scoring settings screen as category checkboxes

**Files:**
- Modify: `frontend/src/screens/ScoringSettingsScreen.tsx`
- Modify: `frontend/src/screens/ScoringSettingsScreen.test.tsx`

**Interfaces:**
- Consumes: `getScoringSettings`, `saveScoringSettings`, `getAvailableScoringCategories` (Task 6), `ScoringSettings`/`ScoringCategoryOption` (Task 6).
- Produces: the finished `ScoringSettingsScreen` — same props (`{ onSaved: (settings: ScoringSettings) => void }`) and the same roster-slots UI as before (unchanged), but hitting/pitching categories are now checkboxes built from `getAvailableScoringCategories()`'s response, checked when the category's `statKey` is present in the loaded settings' `hittingCategoryKeys`/`pitchingCategoryKeys`, rather than free-typed add/remove rows with a manually-entered points value.

- [ ] **Step 1: Write the failing test**

Replace `frontend/src/screens/ScoringSettingsScreen.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ScoringSettingsScreen } from "./ScoringSettingsScreen";
import type { ScoringCategoryOption, ScoringSettings } from "../types";

describe("ScoringSettingsScreen", () => {
  const availableCategories: ScoringCategoryOption[] = [
    { statKey: "homeRuns", displayName: "Home Runs", group: "hitting" },
    { statKey: "stolenBases", displayName: "Stolen Bases", group: "hitting" },
    { statKey: "era", displayName: "ERA", group: "pitching" }
  ];

  it("loads available categories, checks a hitting category, and saves", async () => {
    const saved: ScoringSettings = {
      hittingCategoryKeys: ["homeRuns"],
      pitchingCategoryKeys: [],
      rosterSlots: {}
    };
    const fetchMock = vi.fn((url: string) => {
      if (url.includes("/api/settings/scoring/categories")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(availableCategories) });
      }
      if (url.includes("/api/settings/scoring")) {
        return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
      }
      return Promise.resolve({ ok: false, status: 404, json: () => Promise.resolve({}) });
    });
    vi.stubGlobal("fetch", fetchMock);
    const onSaved = vi.fn();

    render(<ScoringSettingsScreen onSaved={onSaved} />);
    await waitFor(() => expect(screen.getByLabelText(/home runs/i)).toBeInTheDocument());

    fireEvent.click(screen.getByLabelText(/home runs/i));
    fireEvent.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved));

    const saveCall = fetchMock.mock.calls.find(
      (call) => typeof call[0] === "string" && call[0].includes("/api/settings/scoring") && !call[0].includes("categories")
    )!;
    const savedBody = JSON.parse((saveCall[1] as RequestInit).body as string);
    expect(savedBody.hittingCategoryKeys).toEqual(["homeRuns"]);
    expect(savedBody.pitchingCategoryKeys).toEqual([]);

    vi.unstubAllGlobals();
  });

  it("pre-checks categories already present in previously saved settings", async () => {
    const existing: ScoringSettings = {
      hittingCategoryKeys: ["stolenBases"],
      pitchingCategoryKeys: ["era"],
      rosterSlots: {}
    };
    const fetchMock = vi.fn((url: string) => {
      if (url.includes("/api/settings/scoring/categories")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(availableCategories) });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve(existing) });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<ScoringSettingsScreen onSaved={vi.fn()} />);

    await waitFor(() => expect(screen.getByLabelText(/stolen bases/i)).toBeChecked());
    expect(screen.getByLabelText(/^era$/i)).toBeChecked();
    expect(screen.getByLabelText(/home runs/i)).not.toBeChecked();

    vi.unstubAllGlobals();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm run test --prefix frontend`
Expected: FAIL (`ScoringSettingsScreen` still renders the old free-typed rows, not checkboxes with these labels)

- [ ] **Step 3: Implement the screen**

Replace `frontend/src/screens/ScoringSettingsScreen.tsx`:

```tsx
import { useEffect, useState } from "react";
import { getAvailableScoringCategories, getScoringSettings, saveScoringSettings } from "../api/client";
import type { ScoringCategoryOption, ScoringSettings } from "../types";

interface ScoringSettingsScreenProps {
  onSaved: (settings: ScoringSettings) => void;
}

export function ScoringSettingsScreen({ onSaved }: ScoringSettingsScreenProps) {
  const [availableCategories, setAvailableCategories] = useState<ScoringCategoryOption[]>([]);
  const [hittingKeys, setHittingKeys] = useState<string[]>([]);
  const [pitchingKeys, setPitchingKeys] = useState<string[]>([]);
  const [rosterSlots, setRosterSlots] = useState<[string, number][]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    Promise.all([getAvailableScoringCategories(), getScoringSettings()]).then(([categories, settings]) => {
      setAvailableCategories(categories);
      if (!settings) return;
      setHittingKeys(settings.hittingCategoryKeys);
      setPitchingKeys(settings.pitchingCategoryKeys);
      setRosterSlots(Object.entries(settings.rosterSlots));
    });
  }, []);

  function toggleCategory(keys: string[], setKeys: (v: string[]) => void, statKey: string, checked: boolean) {
    setKeys(checked ? [...keys, statKey] : keys.filter((k) => k !== statKey));
  }

  function categoryCheckboxes(
    label: string,
    group: "hitting" | "pitching",
    keys: string[],
    setKeys: (v: string[]) => void
  ) {
    return (
      <fieldset>
        <legend>{label}</legend>
        {availableCategories
          .filter((c) => c.group === group)
          .map((category) => (
            <div key={category.statKey}>
              <label htmlFor={`category-${category.statKey}`}>{category.displayName}</label>
              <input
                id={`category-${category.statKey}`}
                type="checkbox"
                checked={keys.includes(category.statKey)}
                onChange={(e) => toggleCategory(keys, setKeys, category.statKey, e.target.checked)}
              />
            </div>
          ))}
      </fieldset>
    );
  }

  function rosterSlotRows() {
    return (
      <fieldset>
        <legend>Roster Slots</legend>
        {rosterSlots.map(([position, count], index) => (
          <div key={index}>
            <label htmlFor={`roster-slot-position-${index}`}>Roster slot position {index}</label>
            <input
              id={`roster-slot-position-${index}`}
              value={position}
              onChange={(e) => {
                const next = [...rosterSlots];
                next[index] = [e.target.value, next[index][1]];
                setRosterSlots(next);
              }}
            />
            <label htmlFor={`roster-slot-count-${index}`}>Roster slot count {index}</label>
            <input
              id={`roster-slot-count-${index}`}
              type="number"
              value={count}
              onChange={(e) => {
                const next = [...rosterSlots];
                next[index] = [next[index][0], Number(e.target.value)];
                setRosterSlots(next);
              }}
            />
            <button type="button" onClick={() => setRosterSlots(rosterSlots.filter((_, i) => i !== index))}>
              Remove
            </button>
          </div>
        ))}
        <button type="button" onClick={() => setRosterSlots([...rosterSlots, ["", 0]])}>
          Add Roster Slot
        </button>
      </fieldset>
    );
  }

  async function handleSave() {
    setSaving(true);
    try {
      const settings: ScoringSettings = {
        hittingCategoryKeys: hittingKeys,
        pitchingCategoryKeys: pitchingKeys,
        rosterSlots: Object.fromEntries(rosterSlots)
      };
      const result = await saveScoringSettings(settings);
      onSaved(result);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <h1>Scoring Settings</h1>
      {categoryCheckboxes("Hitting", "hitting", hittingKeys, setHittingKeys)}
      {categoryCheckboxes("Pitching", "pitching", pitchingKeys, setPitchingKeys)}
      {rosterSlotRows()}
      <button onClick={handleSave} disabled={saving}>
        Save
      </button>
    </div>
  );
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm run test --prefix frontend`
Expected: PASS

- [ ] **Step 5: Verify the production build works**

Run: `npm run build --prefix frontend`
Expected: builds successfully.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/screens/ScoringSettingsScreen.tsx frontend/src/screens/ScoringSettingsScreen.test.tsx
git commit -m "Redesign scoring settings screen as category checkboxes"
```

---

## Plan Self-Review Notes

- **Spec coverage:** Category reference table with verified direction/rate metadata (Task 1) · shared, single-location rate-stat/innings-conversion math (Task 2) · team standings with correct rate recombination and tie-splitting (Task 3) · weak-category-targeted waiver shortlisting with a sample-size floor (Task 4) · full removal of the points-per-stat model and `FantasyValueRanker` (Task 5) · Claude prompt carrying real standings/category values instead of a blended score (Task 5) · new available-categories endpoint (Task 5) and checkbox-driven settings UI removing user-configured direction/rate-ness entirely (Task 7). The spec's explicitly deferred items (dashboard standings view, Quality Starts, H2H) have no task, by design — they're Non-Goals.
- **Dependency-ordering fix applied during writing:** the initial draft had `RotoStandingsCalculator` taking a `ScoringSettings` object, which would have made every task between the `ScoringSettings` shape change and `FantasyValueRanker`'s removal fail to build project-wide. Fixed by decoupling `RotoStandingsCalculator` from `ScoringSettings` (plain category-key-list parameters instead) and combining the `ScoringSettings` change with `FantasyValueRanker`'s full removal into one atomic task (Task 5) — see the Global Constraints note.
- **Type consistency check:** `RotoStandings`/`TeamCategoryStanding` (Task 3) are consumed identically by `WeakCategoryWaiverShortlist` (Task 4) and `ClaudeRecommendationEngine` (Task 5) with no field renames across tasks. `RotoCategoryReference.Categories` (Task 1) is keyed and typed identically everywhere it's read (Tasks 2-5). `ScoringSettings.HittingCategoryKeys`/`PitchingCategoryKeys` (Task 5) match `hittingCategoryKeys`/`pitchingCategoryKeys` in the frontend (Task 6) via `JsonNamingPolicy.CamelCase`, consistent with every other model in this app.
