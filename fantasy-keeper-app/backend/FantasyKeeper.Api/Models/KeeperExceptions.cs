namespace FantasyKeeper.Api.Models;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class SeasonNotActiveException : Exception
{
    public SeasonNotActiveException(string seasonId) : base($"Season '{seasonId}' is not the active season.") { }
}

public class KeeperValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public KeeperValidationException(IReadOnlyList<string> errors) : base(string.Join("; ", errors))
    {
        Errors = errors;
    }
}

public class InvalidWorkbookException : Exception
{
    public InvalidWorkbookException(string message) : base(message) { }
}
