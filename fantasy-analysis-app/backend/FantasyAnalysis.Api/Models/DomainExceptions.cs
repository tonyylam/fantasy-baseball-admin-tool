namespace FantasyAnalysis.Api.Models;

public class CsvParseException : Exception
{
    public CsvParseException(string message) : base(message) { }
}

public class StatsProviderException : Exception
{
    public StatsProviderException(string message) : base(message) { }
    public StatsProviderException(string message, Exception innerException) : base(message, innerException) { }
}

public class RecommendationClientException : Exception
{
    public RecommendationClientException(string message) : base(message) { }
    public RecommendationClientException(string message, Exception innerException) : base(message, innerException) { }
}

public class RecommendationPrerequisiteException : Exception
{
    public RecommendationPrerequisiteException(string message) : base(message) { }
}
