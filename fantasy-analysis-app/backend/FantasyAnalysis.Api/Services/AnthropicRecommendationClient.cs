using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class AnthropicRecommendationClient : IRecommendationClient
{
    private readonly AnthropicClient _client;

    public AnthropicRecommendationClient(AnthropicClient client)
    {
        _client = client;
    }

    public async Task<string> GetRecommendationsJsonAsync(string systemPrompt, string userPrompt)
    {
        var parameters = new MessageCreateParams
        {
            Model = "claude-sonnet-5",
            MaxTokens = 16000,
            System = new List<TextBlockParam> { new() { Text = systemPrompt } },
            Messages = [new() { Role = Role.User, Content = userPrompt }],
            Tools = [new ToolUnion(new WebSearchTool20260209())],
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = BuildSchema() } },
        };

        Message response;
        try
        {
            response = await _client.Messages.Create(parameters);
        }
        catch (Exception ex)
        {
            throw new RecommendationClientException("Failed to get recommendations from Claude.", ex);
        }

        // Web search results and other server-tool blocks may precede the final structured
        // answer, so take the LAST text block, not the first.
        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().LastOrDefault()?.Text;
        if (text is null)
        {
            throw new RecommendationClientException("Claude response contained no text content.");
        }

        return text;
    }

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
            required = new[] { "summary", "reasoning", "involvedPlayerIds", "citations" }
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                waiverSuggestions = new { type = "array", items = recommendationSchema },
                tradeSuggestions = new { type = "array", items = recommendationSchema }
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "waiverSuggestions", "tradeSuggestions" })
        };
    }
}
