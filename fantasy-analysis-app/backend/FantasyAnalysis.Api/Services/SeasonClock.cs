namespace FantasyAnalysis.Api.Services;

public static class SeasonClock
{
    public static int Current => DateTime.UtcNow.Year;
}
