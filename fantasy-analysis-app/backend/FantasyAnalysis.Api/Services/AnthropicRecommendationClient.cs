using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using FantasyAnalysis.Api.Models;
using Microsoft.Extensions.Logging;

namespace FantasyAnalysis.Api.Services;

public class AnthropicRecommendationClient : IRecommendationClient
{
    private readonly AnthropicClient _client;
    private readonly ILogger<AnthropicRecommendationClient> _logger;

    public AnthropicRecommendationClient(AnthropicClient client, ILogger<AnthropicRecommendationClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string> GetRecommendationsJsonAsync(string systemPrompt, string userPrompt)
    {
        var parameters = BuildParameters(systemPrompt, userPrompt);

        Message response;
        try
        {
            response = await _client.Messages.Create(parameters);
        }
        catch (Exception ex)
        {
            throw new RecommendationClientException("Failed to get recommendations from Claude.", ex);
        }

        _logger.LogInformation(
            "Claude token usage: {CacheReadTokens} read from cache, {CacheWriteTokens} written to cache, {UncachedInputTokens} uncached input, {OutputTokens} output",
            response.Usage.CacheReadInputTokens,
            response.Usage.CacheCreationInputTokens,
            response.Usage.InputTokens,
            response.Usage.OutputTokens);

        // Web search results and other server-tool blocks may precede the final structured
        // answer, so take the LAST text block, not the first.
        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().LastOrDefault()?.Text;
        if (text is null)
        {
            throw new RecommendationClientException("Claude response contained no text content.");
        }

        return text;
    }

    private static MessageCreateParams BuildParameters(string systemPrompt, string userPrompt) => new()
    {
        Model = "claude-sonnet-5",
        MaxTokens = 16000,
        System = new List<TextBlockParam> { new() { Text = systemPrompt } },
        Messages = [new() { Role = Role.User, Content = userPrompt }],
        Tools = [new ToolUnion(new WebSearchTool20260209())],
        OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = BuildSchema() } },
        // This is a single-shot request (no multi-turn loop), and the fixed system prompt +
        // web-search tool declaration alone are far too small to independently clear the
        // per-model cache-minimum token floor - so the breakpoint belongs on the *whole*
        // prefix (tools + system + the user prompt, which carries the full league rosters/
        // standings and dwarfs that floor), not on the system block alone. Top-level
        // CacheControl auto-places on the last cacheable block, which is this request's only
        // message. A repeat Analyze call - e.g. retrying after a transient failure, or
        // re-running without anything having changed - reads this from cache instead of
        // paying full price. 1-hour TTL (vs. the 5-minute default) trades a higher write
        // premium for surviving realistic gaps between manually-triggered Analyze clicks.
        CacheControl = new CacheControlEphemeral { Ttl = Ttl.Ttl1h },
    };

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        var recommendationSchema = new
        {
            type = "object",
            properties = new
            {
                summary = new { type = "string" },
                reasoning = new { type = "string" },
                involvedPlayerIds = new { type = "array", items = new { type = "string" } },
                citations = new { type = "array", items = new { type = "string" } }
            },
            required = new[] { "summary", "reasoning", "involvedPlayerIds", "citations" },
            additionalProperties = false
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                waiverSuggestions = new { type = "array", items = recommendationSchema },
                tradeSuggestions = new { type = "array", items = recommendationSchema }
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "waiverSuggestions", "tradeSuggestions" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
        };
    }
}
