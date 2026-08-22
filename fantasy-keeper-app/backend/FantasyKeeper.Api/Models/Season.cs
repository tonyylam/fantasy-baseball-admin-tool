namespace FantasyKeeper.Api.Models;

public record Season(string Id, string Label, string GoogleSheetId, string Status, DateTimeOffset CreatedAt)
{
    public bool IsActive => Status == "active";
}
