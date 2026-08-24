using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeKeepersDataStore : IKeepersDataStore
{
    public KeepersData? Data { get; set; }
    public byte[]? Workbook { get; set; }

    public KeepersData? LoadData() => Data;
    public void SaveData(KeepersData data) => Data = data;
    public void SaveWorkbook(byte[] bytes) => Workbook = bytes;
    public byte[]? LoadWorkbook() => Workbook;
}
