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
/// Covers <see cref="SearchDefinitionsTool"/>'s miss content, built by the shared
/// <see cref="SourceMissContentBuilder"/>: a symbol that occurs in the indexed source but under no
/// matching kind names where it occurs, and a symbol absent from the source says so instead. Also
/// proves that <see cref="GetFunctionDefinitionTool"/>'s own miss payload is unaffected by the
/// delegation to that same shared builder.
/// </summary>
public class SearchDefinitionsToolMissContentTests : IDisposable
{
    private readonly string _sourceDir;

    public SearchDefinitionsToolMissContentTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "SearchDefinitionsToolMissContentTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        // 'baz' occurs only as a plain variable declaration — no function, macro, struct, enum or
        // typedef pattern matches it, so it is a miss under every kind even though it occurs.
        File.WriteAllText(Path.Combine(_sourceDir, "include", "defs.h"),
            "/* defs.h */\r\n" +
            "#define OTHER_LIMIT 1\r\n" +
            "int baz;\r\n");
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

    private static ToolExecutionContext CreateContext(long sessionId)
        => new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(sessionId) };

    private async Task<(SourceCodeService Service, NetHackSourceCodeService NetHackService)> CreateServicesAsync()
    {
        var config = CreateConfiguration();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(CancellationToken.None);

        // The NetHack repository is left unconfigured: nothing here selects it.
        var netHackService = new NetHackSourceCodeService(config, NullLogger<NetHackSourceCodeService>.Instance);

        return (service, netHackService);
    }

    /// <summary>
    /// 'baz' occurs in the corpus but matches no kind, so the miss names the file it occurs in
    /// rather than leaving the model to retry the same name.
    /// </summary>
    [Fact]
    public async Task SymbolOccursButNoKindMatches_MissNamesTheOccurringFile()
    {
        var (service, netHackService) = await CreateServicesAsync();
        using (service)
        using (netHackService)
        {
            var tool = new SearchDefinitionsTool(service, netHackService);
            var arguments = JsonDocument.Parse("""{"symbol": "baz"}""").RootElement;

            var result = await tool.ExecuteAsync(arguments, CreateContext(4001), CancellationToken.None);

            Assert.True(result.Success);
            Assert.StartsWith("No definition found for '", result.Content);
            Assert.True(result.Content.Length <= 600, $"Expected at most 600 characters, got {result.Content.Length}.");
            Assert.Contains("defs.h", result.Content);
        }
    }

    /// <summary>A symbol absent from the corpus altogether is told so, rather than getting an occurrence summary.</summary>
    [Fact]
    public async Task SymbolAbsentFromCorpus_MissSaysItDoesNotOccur()
    {
        var (service, netHackService) = await CreateServicesAsync();
        using (service)
        using (netHackService)
        {
            var tool = new SearchDefinitionsTool(service, netHackService);
            var arguments = JsonDocument.Parse("""{"symbol": "zzznotpresent"}""").RootElement;

            var result = await tool.ExecuteAsync(arguments, CreateContext(4002), CancellationToken.None);

            Assert.True(result.Success);
            Assert.StartsWith("No definition found for '", result.Content);
            Assert.Contains("does not occur in the indexed gnollhack source.", result.Content);
        }
    }

    /// <summary>
    /// <see cref="GetFunctionDefinitionTool"/> delegates its miss content to the same
    /// <see cref="SourceMissContentBuilder"/> that <see cref="SearchDefinitionsTool"/> now uses, but
    /// with its own guidance sentences. This asserts the resulting payload byte-for-byte against the
    /// builder's documented formula, so a change to the shared builder that altered this tool's
    /// wording would be caught here rather than only in <c>GetFunctionDefinitionToolFallbackTests</c>.
    /// </summary>
    [Fact]
    public async Task GetFunctionDefinitionMiss_MatchesTheDocumentedNoHitFormulaByteForByte()
    {
        var (service, netHackService) = await CreateServicesAsync();
        using (service)
        using (netHackService)
        {
            var tool = new GetFunctionDefinitionTool(service, netHackService);
            const string name = "totallyAbsentUniqueSymbolXyz";
            var arguments = JsonDocument.Parse($"{{\"name\": \"{name}\"}}").RootElement;

            var result = await tool.ExecuteAsync(arguments, CreateContext(4003), CancellationToken.None);

            const string expected =
                "No definition found for 'totallyAbsentUniqueSymbolXyz' of kind 'any'."
                + " 'totallyAbsentUniqueSymbolXyz' does not occur in the indexed gnollhack source."
                + " This tool extracts a function, macro or struct body declared under that exact name."
                + " A struct member, function pointer or macro alias has no body here — use source_code_search with context_lines to find where it is declared,"
                + " or search_definitions for the symbol it is assigned from.";

            Assert.True(result.Success);
            Assert.Equal(expected, result.Content);
        }
    }
}
