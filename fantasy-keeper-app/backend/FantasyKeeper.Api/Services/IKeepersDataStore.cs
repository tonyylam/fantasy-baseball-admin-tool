using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public interface IKeepersDataStore
{
    KeepersData? LoadData();
    void SaveData(KeepersData data);
    void SaveWorkbook(byte[] bytes);
    byte[]? LoadWorkbook();
}
