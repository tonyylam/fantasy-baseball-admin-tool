using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
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
        var client = new AnthropicRecommendationClient(anthropicClient, NullLogger<AnthropicRecommendationClient>.Instance);

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

    [Fact]
    public void BuildSchema_SetsAdditionalPropertiesFalseOnEveryObjectNode()
    {
        // The live Anthropic API rejects output_config.format.schema unless every object-type
        // node - not just the top level - explicitly sets additionalProperties: false. This app
        // hit that exact 400 in production because the nested per-recommendation object (inside
        // waiverSuggestions/tradeSuggestions' array items) was missing it.
        var method = typeof(AnthropicRecommendationClient).GetMethod("BuildSchema", BindingFlags.NonPublic | BindingFlags.Static)!;
        var schema = (Dictionary<string, JsonElement>)method.Invoke(null, null)!;
        var root = JsonSerializer.SerializeToElement(schema);

        AssertAdditionalPropertiesFalseOnEveryObject(root);
    }

    [Fact]
    public void BuildParameters_SetsAOneHourCacheControlBreakpointOnTheWholeRequest()
    {
        // This app makes one Messages.Create call per Analyze click (no multi-turn loop), and
        // the fixed system prompt + web-search tool declaration are far too small on their own
        // to clear the per-model cache-minimum token floor. The breakpoint has to cover the
        // whole prefix - via top-level CacheControl, which auto-places on the last cacheable
        // block (this request's only message, which carries the full league rosters/standings
        // and is what actually clears the floor) - so a repeat Analyze call with unchanged
        // input reads from cache instead of paying full price again.
        var method = typeof(AnthropicRecommendationClient).GetMethod("BuildParameters", BindingFlags.NonPublic | BindingFlags.Static)!;
        var parameters = (MessageCreateParams)method.Invoke(null, new object[] { "system prompt", "user prompt" })!;

        var cacheControl = Assert.IsType<CacheControlEphemeral>(parameters.CacheControl);
        Ttl actualTtl = cacheControl.Ttl!;
        Assert.Equal(Ttl.Ttl1h, actualTtl);
    }

    private static void AssertAdditionalPropertiesFalseOnEveryObject(JsonElement node)
    {
        if (node.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "object")
        {
            Assert.True(
                node.TryGetProperty("additionalProperties", out var additionalProperties)
                    && additionalProperties.ValueKind == JsonValueKind.False,
                $"Object node missing additionalProperties: false: {node.GetRawText()}");
        }

        if (node.TryGetProperty("properties", out var propertiesEl))
        {
            foreach (var property in propertiesEl.EnumerateObject())
            {
                AssertAdditionalPropertiesFalseOnEveryObject(property.Value);
            }
        }

        if (node.TryGetProperty("items", out var itemsEl))
        {
            AssertAdditionalPropertiesFalseOnEveryObject(itemsEl);
        }
    }
}
