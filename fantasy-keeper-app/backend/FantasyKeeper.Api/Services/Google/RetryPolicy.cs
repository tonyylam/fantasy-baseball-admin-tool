namespace FantasyKeeper.Api.Services.Google;

public static class RetryPolicy
{
    public static async Task<T> WithOneRetryAsync<T>(Func<Task<T>> action, TimeSpan delay, CancellationToken ct = default)
    {
        try
        {
            return await action();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await Task.Delay(delay, ct);
            return await action();
        }
    }

    public static async Task WithOneRetryAsync(Func<Task> action, TimeSpan delay, CancellationToken ct = default)
    {
        try
        {
            await action();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await Task.Delay(delay, ct);
            await action();
        }
    }
}
