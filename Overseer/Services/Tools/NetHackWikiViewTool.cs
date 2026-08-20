using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class NetHackWikiViewTool : IToolHandler
    {
        private readonly NetHackWikiService _netHackWikiService;

        public string ToolName => "nethack_wiki_view";
        public string Description { get; set; } = "View a specific NetHack wiki article by title. Use when you already know which article you want.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public NetHackWikiViewTool(NetHackWikiService netHackWikiService)
        {
            _netHackWikiService = netHackWikiService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""article"": {
                        ""type"": ""string"",
                        ""description"": ""The article title to retrieve (e.g. 'Cockatrice', 'Wand of digging', 'Elbereth')""
                    },
                    ""section"": {
                        ""type"": ""string"",
                        ""description"": ""Optional. A specific section heading to extract (e.g. 'Strategy', 'Generation')""
                    }
                },
                ""required"": [""article""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            string article = "";
            if (parameters.TryGetProperty("article", out var articleElem))
            {
                article = articleElem.GetString() ?? "";
            }

            string? section = null;
            if (parameters.TryGetProperty("section", out var sectionElem))
            {
                section = sectionElem.GetString();
            }

            if (string.IsNullOrWhiteSpace(article))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing article parameter" });
            }

            string? content = _netHackWikiService.GetArticle(article, section);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = $"NetHack wiki article matching '{article}' not found." });
            }

            if (context.SpoilerFreeMode)
            {
                content += "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
    }
}
