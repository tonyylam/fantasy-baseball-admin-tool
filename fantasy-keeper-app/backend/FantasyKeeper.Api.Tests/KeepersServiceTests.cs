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
                        new List<int> { 10, 11 },
                        new List<ExistingContractRow>
                        {
                            new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m),
                            new("Other Player", "#1 - 3/3", 5, 2m, 2m)
                        })
                })
        };

        return (config, store, new KeepersService(store, config));
    }

    [Fact]
    public void GetKeeperData_ReturnsStoredRows()
    {
        var (_, _, service) = Build();

        var data = service.GetKeeperData("b-squared", canEdit: true);

        Assert.Equal("B Squared", data.TeamName);
        Assert.True(data.CanEdit);
        Assert.Equal("T. Story", data.NewContracts[0].Player);
        Assert.Equal(1, data.NewContracts[0].ContractType);
        Assert.Equal(14, data.NewContracts[0].Salary);
        Assert.Equal("Jasson Dominguez", data.ExistingContracts[0].Player);
        Assert.False(data.ExistingContracts[0].Deleted);
    }

    [Fact]
    public void GetKeeperData_ReadOnlyViewer_ReturnsCanEditFalse()
    {
        var (_, _, service) = Build();

        var data = service.GetKeeperData("b-squared", canEdit: false);

        Assert.False(data.CanEdit);
    }

    [Fact]
    public void GetKeeperData_NoDataImported_Throws()
    {
        var config = new FakeConfigStore { Teams = new List<Team> { new("b-squared", "B Squared", "1111") } };
        var store = new FakeKeepersDataStore();
        var service = new KeepersService(store, config);

        Assert.Throws<NotFoundException>(() => service.GetKeeperData("b-squared", canEdit: true));
    }

    [Fact]
    public void UpdateKeeperData_ValidSubmission_SavesAndReturnsUpdatedRows()
    {
        var (_, store, service) = Build();
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("New Guy", 1, 10, 2),
                new("", null, null, null)
            },
            new List<int>());

        var result = service.UpdateKeeperData("b-squared", submission);

        Assert.Equal("New Guy", result.NewContracts[0].Player);
        Assert.Equal("New Guy", store.Data!.Teams["b-squared"].NewContracts[0].Player);
    }

    [Fact]
    public void UpdateKeeperData_BumpsLastUpdatedUtc()
    {
        var (_, store, service) = Build();
        var before = store.Data!.LastUpdatedUtc;
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("New Guy", 1, 10, 2),
                new("", null, null, null)
            },
            new List<int>());

        service.UpdateKeeperData("b-squared", submission);

        Assert.True(store.Data!.LastUpdatedUtc > before);
    }

    [Fact]
    public void UpdateKeeperData_InvalidContractType_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("New Guy", 3, 10, 2),
                new("", null, null, null)
            },
            new List<int>());

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Fact]
    public void UpdateKeeperData_WrongRowCount_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow> { new("New Guy", 1, 10, 2) }, new List<int>());

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
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new(playerName, 1, 10, 2),
                new("", null, null, null)
            },
            new List<int>());

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
    }

    [Fact]
    public void UpdateKeeperData_UnknownTeam_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>(), new List<int>());

        Assert.Throws<NotFoundException>(() => service.UpdateKeeperData("nobody", submission));
    }

    [Fact]
    public void UpdateKeeperData_DeletedExistingContractIndex_MarksDeletedAndPersists()
    {
        var (_, store, service) = Build();
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("T. Story", 1, 14, 2),
                new("", null, null, null)
            },
            new List<int> { 0 });

        var result = service.UpdateKeeperData("b-squared", submission);

        Assert.True(result.ExistingContracts[0].Deleted);
        Assert.False(result.ExistingContracts[1].Deleted);
        Assert.Equal("Jasson Dominguez", result.ExistingContracts[0].Player);
        Assert.Equal(3, result.ExistingContracts[0].LastYearSalary);
        Assert.True(store.Data!.Teams["b-squared"].ExistingContracts[0].Deleted);
    }

    [Fact]
    public void UpdateKeeperData_ResubmitWithoutPreviouslyDeletedIndex_UndeletesIt()
    {
        var (_, store, service) = Build();
        var deleteSubmission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("T. Story", 1, 14, 2),
                new("", null, null, null)
            },
            new List<int> { 0 });
        service.UpdateKeeperData("b-squared", deleteSubmission);

        var undeleteSubmission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("T. Story", 1, 14, 2),
                new("", null, null, null)
            },
            new List<int>());
        var result = service.UpdateKeeperData("b-squared", undeleteSubmission);

        Assert.False(result.ExistingContracts[0].Deleted);
        Assert.False(store.Data!.Teams["b-squared"].ExistingContracts[0].Deleted);
    }

    [Fact]
    public void UpdateKeeperData_DeletedExistingContractIndexOutOfRange_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(
            new List<KeeperRow>
            {
                new("T. Story", 1, 14, 2),
                new("", null, null, null)
            },
            new List<int> { 99 });

        Assert.Throws<KeeperValidationException>(() => service.UpdateKeeperData("b-squared", submission));
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
            var submission = new KeeperSubmission(new List<KeeperRow> { new($"Player {i}", 1, 10, 2) }, new List<int>());
            service.UpdateKeeperData("b-squared", submission);
        });

        Assert.False(store.InterleavingDetected, "Two callers were inside the load->save window at once.");
        Assert.StartsWith("Player ", store.Data!.Teams["b-squared"].NewContracts[0].Player);
    }
}
