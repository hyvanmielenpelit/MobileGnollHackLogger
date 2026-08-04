using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class KnowledgeBaseTool : IToolHandler
    {
        private readonly KnowledgeBaseService _knowledgeBaseService;

        public string ToolName => "get_knowledge_article";
        public string Description { get; set; } = "Retrieve curated first-party reference articles about the GnollHack app.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public KnowledgeBaseTool(KnowledgeBaseService knowledgeBaseService)
        {
            _knowledgeBaseService = knowledgeBaseService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""topic"": { ""type"": ""string"", ""description"": ""The topic key to look up (e.g., 'app_navigation')"" }
                },
                ""required"": [""topic""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            if (!parameters.TryGetProperty("topic", out var topicElement) || topicElement.ValueKind != JsonValueKind.String)
            {
                return Task.FromResult(new ToolResult 
                { 
                    Success = false, 
                    ErrorMessage = "Missing or invalid 'topic' parameter. Expected a string."
                });
            }

            string topic = topicElement.GetString()!;
            var articleContent = _knowledgeBaseService.GetArticle(topic);

            if (articleContent != null)
            {
                return Task.FromResult(new ToolResult 
                { 
                    Success = true, 
                    Content = articleContent
                });
            }

            var availableTopics = string.Join(", ", _knowledgeBaseService.GetAvailableTopics());
            return Task.FromResult(new ToolResult 
            { 
                Success = false, 
                ErrorMessage = $"Article not found for topic '{topic}'. Available topics: {availableTopics}"
            });
        }
    }
}
