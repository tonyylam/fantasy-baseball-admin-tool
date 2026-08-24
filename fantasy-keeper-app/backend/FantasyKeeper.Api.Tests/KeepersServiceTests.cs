using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeepersServiceTests
{
    private static (FakeConfigStore Config, FakeKeepersDataStore Store, KeepersService Service) Build()
    {
        var config = new FakeConfigStore
        {
            Teams = new List<Team> { new("b-squared", "B Squared", "1111") }
        };

        var store = new FakeKeepersDataStore
        {
            Data = new KeepersData(
                "test.xlsx",
                "2026 Keepers",
                DateTimeOffset.UtcNow,
                new Dictionary<string, StoredTeamKeepers>
                {
                    ["b-squared"] = new StoredTeamKeepers(
                        "B Squared",
                        7,
                        new List<int> { 8, 9 },
                        new List<KeeperRow>
                        {
                            new("T. Story", 1, 14, 2),
                            new("", null, null, null)
                        },
                        new List<int>(),
                        new List<ExistingContractRow>
                        {
                            new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                        })
                })
        };

        return (config, store, new KeepersService(store, config));
    }

    [Fact]
    public void GetKeeperData_ReturnsStoredRows()
    {
        var (_, _, service) = Build();

        var data = service.GetKeeperData("b-squared");

        Assert.Equal("B Squared", data.TeamName);
        Assert.Equal("T. Story", data.NewContracts[0].Player);
        Assert.Equal(1, data.NewContracts[0].ContractType);
        Assert.Equal(14, data.NewContracts[0].Salary);
        Assert.Equal("Jasson Dominguez", data.ExistingContracts[0].Player);
    }

    [Fact]
    public void GetKeeperData_NoDataImported_Throws()
    {
        var config = new FakeConfigStore { Teams = new List<Team> { new("b-squared", "B Squared", "1111") } };
        var store = new FakeKeepersDataStore();
        var service = new KeepersService(store, config);

        Assert.Throws<NotFoundException>(() => service.GetKeeperData("b-squared"));
    }

    [Fact]
    public void UpdateKeeperData_ValidSubmission_SavesAndReturnsUpdatedRows()
    {
        var (_, store, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        var result = service.UpdateKeeperData("b-squared", submission);

        Assert.Equal("New Guy", result.NewContracts[0].Player);
        Assert.Equal("New Guy", store.Data!.Teams["b-squared"].NewContracts[0].Player);
    }

    [Fact]
    public void UpdateKeeperData_BumpsLastUpdatedUtc()
    {
        var (_, store, service) = Build();
        var before = store.Data!.LastUpdatedUtc;
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        service.UpdateKeeperData("b-squared", submission);

        Assert.True(store.Data!.LastUpdatedUtc > before);
    }

    [Fact]
    public void UpdateKeeperData_InvalidContractType_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 3, 10, 2),
            new("", null, null, null)
        });

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Fact]
    public void UpdateKeeperData_WrongRowCount_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow> { new("New Guy", 1, 10, 2) });

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Theory]
    [InlineData("=ARRAYFORMULA(A1:A10)")]
    [InlineData("+1+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    public void UpdateKeeperData_PlayerNameStartsWithFormulaChar_Throws(string playerName)
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new(playerName, 1, 10, 2),
            new("", null, null, null)
        });

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Fact]
    public void UpdateKeeperData_UnknownTeam_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>());

        Assert.Throws<NotFoundException>(() => service.UpdateKeeperData("nobody", submission));
    }

    /// <summary>
    /// Instruments the load -> save window so a test can tell whether two callers were ever
    /// inside it at the same time (which is what causes a lost update).
    /// </summary>
    private class InterleaveDetectingStore : IKeepersDataStore
    {
        private int _insideLoadToSaveWindow;

        public KeepersData? Data { get; set; }
        public byte[]? Workbook { get; set; }
        public bool InterleavingDetected { get; private set; }

        public KeepersData? LoadData()
        {
            if (Interlocked.Increment(ref _insideLoadToSaveWindow) > 1)
            {
                InterleavingDetected = true;
            }
            // Widen the window so an unsynchronized read-modify-write reliably overlaps.
            Thread.Sleep(25);
            return Data;
        }

        public void SaveData(KeepersData data)
        {
            Data = data;
            Interlocked.Decrement(ref _insideLoadToSaveWindow);
        }

        public void SaveWorkbook(byte[] bytes) => Workbook = bytes;
        public byte[]? LoadWorkbook() => Workbook;
    }

    [Fact]
    public void UpdateKeeperData_ConcurrentCallers_DoNotInterleaveReadModifyWrite()
    {
        var config = new FakeConfigStore { Teams = new List<Team> { new("b-squared", "B Squared", "1111") } };
        var store = new InterleaveDetectingStore
        {
            Data = new KeepersData(
                "test.xlsx",
                "2026 Keepers",
                DateTimeOffset.UtcNow,
                new Dictionary<string, StoredTeamKeepers>
                {
                    ["b-squared"] = new StoredTeamKeepers(
                        "B Squared",
                        7,
                        new List<int> { 8 },
                        new List<KeeperRow> { new("T. Story", 1, 14, 2) },
                        new List<int>(),
                        new List<ExistingContractRow>())
                })
        };
        var service = new KeepersService(store, config);

        Parallel.For(0, 8, i =>
        {
            var submission = new KeeperSubmission(new List<KeeperRow> { new($"Player {i}", 1, 10, 2) });
            service.UpdateKeeperData("b-squared", submission);
        });

        Assert.False(store.InterleavingDetected, "Two callers were inside the load->save window at once.");
        Assert.StartsWith("Player ", store.Data!.Teams["b-squared"].NewContracts[0].Player);
    }
}
