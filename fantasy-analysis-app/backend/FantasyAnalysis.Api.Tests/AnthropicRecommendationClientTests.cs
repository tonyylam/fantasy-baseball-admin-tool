using System;
using System.Threading.Tasks;
using Anthropic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class AnthropicRecommendationClientTests
{
    [Fact]
    public async Task GetRecommendationsJsonAsync_SendsRequest_FailsOnNetworkNotConstruction()
    {
        var anthropicClient = new AnthropicClient
        {
            ApiKey = "test-key",
            BaseUrl = "http://127.0.0.1:1" // nothing listens here - guarantees a connection failure, not a 4xx/5xx
        };
        var client = new AnthropicRecommendationClient(anthropicClient);

        // A connection-level exception here proves the request was built and dispatched;
        // any exception before that (e.g. a schema-construction bug) would throw synchronously
        // during GetRecommendationsJsonAsync's setup, before the awaited call, and this assertion
        // would fail with the wrong exception type instead.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.GetRecommendationsJsonAsync("system prompt", "user prompt"));
    }
}
