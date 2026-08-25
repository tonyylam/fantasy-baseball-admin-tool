using System;
using System.Net.Http;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
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

        // AnthropicRecommendationClient catches any exception thrown while awaiting the SDK
        // call and wraps it as RecommendationClientException, preserving the original as
        // InnerException. Because that catch is inside the async method body, a plain
        // Assert.ThrowsAnyAsync<Exception> would pass identically whether the failure came
        // from the network or from a bug in request/schema construction (e.g. a
        // NullReferenceException in BuildSchema) - both get captured into the returned
        // Task's fault state the same way. To actually distinguish them, assert on the
        // wrapped exception's InnerException type: the Anthropic SDK surfaces a connection
        // failure as AnthropicIOException, whose own InnerException is the underlying
        // HttpRequestException ("actively refused") from HttpClient. A construction-time bug
        // would surface as some other exception type instead, so this assertion would
        // correctly fail if that regression were reintroduced.
        var ex = await Assert.ThrowsAsync<RecommendationClientException>(() =>
            client.GetRecommendationsJsonAsync("system prompt", "user prompt"));

        var ioException = Assert.IsType<AnthropicIOException>(ex.InnerException);
        Assert.IsType<HttpRequestException>(ioException.InnerException);
    }
}
