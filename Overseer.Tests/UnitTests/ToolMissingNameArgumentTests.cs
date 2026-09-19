using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The four source-code tools that require a <c>name</c> argument reject a missing, null,
/// non-string or blank one with <c>Success = false</c> and "Missing name parameter", rather than
/// throwing out of <see cref="IToolHandler.ExecuteAsync"/>.
/// </summary>
public class ToolMissingNameArgumentTests : IDisposable
{
    private readonly string _sourceDir;

    public ToolMissingNameArgumentTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "ToolMissingNameArgumentTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        File.WriteAllText(Path.Combine(_sourceDir, "src", "util.c"),
            "/* util.c */\r\n" +
            "int\r\n" +
            "bar(void)\r\n" +
            "{\r\n" +
            "    return 1;\r\n" +
            "}\r\n");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    private IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
            })
            .Build();
    }

    public static TheoryData<string, string> Cases()
    {
        var tools = new[] { "get_function_definition", "get_item_stats", "get_monster_stats", "get_artifact_stats" };
        var arguments = new[]
        {
            "{}",
            "{\"type\": \"function\"}",
            "{\"name\": null}",
            "{\"name\": 42}",
            "{\"name\": \"   \"}"
        };

        var data = new TheoryData<string, string>();
        foreach (var tool in tools)
        {
            foreach (var json in arguments)
            {
                data.Add(tool, json);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task MissingOrInvalidName_ReturnsMissingNameError(string toolName, string argumentsJson)
    {
        var config = CreateConfiguration();

        using var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(CancellationToken.None);
        Assert.True(service.IsIndexingComplete);

        // The NetHack repository is left unconfigured: no case here selects it.
        using var netHackService = new NetHackSourceCodeService(config, NullLogger<NetHackSourceCodeService>.Instance);

        IToolHandler tool = toolName switch
        {
            "get_function_definition" => new GetFunctionDefinitionTool(service, netHackService),
            "get_item_stats" => new GetItemStatsTool(service, config),
            "get_monster_stats" => new GetMonsterStatsTool(service, config),
            "get_artifact_stats" => new GetArtifactStatsTool(service, config),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, null)
        };

        var arguments = JsonDocument.Parse(argumentsJson).RootElement;
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4001) };

        var result = await tool.ExecuteAsync(arguments, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Missing name parameter", result.ErrorMessage);
    }
}
