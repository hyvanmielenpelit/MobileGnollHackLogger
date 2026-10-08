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

    /// <summary>
    /// A miss under every kind (<c>any</c>, or no kind) is not told to retry with <c>kind: "any"</c>;
    /// a miss under one kind is.
    /// </summary>
    [Theory]
    [InlineData("""{"symbol": "baz"}""", false)]
    [InlineData("""{"symbol": "baz", "kind": "any"}""", false)]
    [InlineData("""{"symbol": "baz", "kind": "function"}""", true)]
    public async Task SymbolOccursButNoKindMatches_GuidanceFollowsTheRequestedKind(string json, bool suggestsAny)
    {
        var (service, netHackService) = await CreateServicesAsync();
        using (service)
        using (netHackService)
        {
            var tool = new SearchDefinitionsTool(service, netHackService);
            var arguments = JsonDocument.Parse(json).RootElement;

            var result = await tool.ExecuteAsync(arguments, CreateContext(4004), CancellationToken.None);

            Assert.True(result.Success);
            if (suggestsAny)
            {
                Assert.Contains(" The symbol occurs but no definition line matched this kind: try `kind: \"any\"`, or `source_code_search` with `context_lines` on the named file.", result.Content);
            }
            else
            {
                Assert.Contains(" The symbol occurs but no definition line matched: try `source_code_search` with `context_lines` on the named file.", result.Content);
                Assert.DoesNotContain("kind: \"any\"", result.Content);
            }
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
    /// An identifier-shaped probe matches whole words only: <c>disarm_bonus</c> is not an occurrence
    /// of <c>ARM_BONUS</c>, so a corpus holding only the former reports the latter as absent.
    /// </summary>
    [Fact]
    public async Task IdentifierProbe_DoesNotCountAnOccurrenceInsideALongerName()
    {
        WriteSource("do_wear.c",
            "/* do_wear.c */\r\n" +
            "    total += disarm_bonus(uarm);\r\n" +
            "    total -= disarm_bonus(uarmc);\r\n");

        var (service, netHackService) = await CreateServicesAsync();
        using (service)
        using (netHackService)
        {
            var tool = new SearchDefinitionsTool(service, netHackService);
            var arguments = JsonDocument.Parse("""{"symbol": "ARM_BONUS"}""").RootElement;

            var result = await tool.ExecuteAsync(arguments, CreateContext(4005), CancellationToken.None);

            Assert.True(result.Success);
            Assert.StartsWith("No definition found for '", result.Content);
            Assert.Contains("'ARM_BONUS' does not occur in the indexed gnollhack source.", result.Content);
            Assert.DoesNotContain("do_wear.c", result.Content);
        }
    }

    /// <summary>
    /// A whole-word occurrence of an identifier-shaped probe is still found, and is the only one
    /// counted when a longer name containing it occurs elsewhere.
    /// </summary>
    [Fact]
    public async Task IdentifierProbe_FindsAWholeWordOccurrence()
    {
        WriteSource("do_wear.c",
            "/* do_wear.c */\r\n" +
            "    total += disarm_bonus(uarm);\r\n" +
            "    total -= disarm_bonus(uarmc);\r\n");
        WriteSource("armor.c",
            "/* armor.c */\r\n" +
            "    total += ARM_BONUS(uarm);\r\n");

        var (service, netHackService) = await CreateServicesAsync();
        using (service)
        using (netHackService)
        {
            var tool = new SearchDefinitionsTool(service, netHackService);
            var arguments = JsonDocument.Parse("""{"symbol": "ARM_BONUS"}""").RootElement;

            var result = await tool.ExecuteAsync(arguments, CreateContext(4006), CancellationToken.None);

            Assert.True(result.Success);
            Assert.StartsWith("No definition found for '", result.Content);
            Assert.Contains("'ARM_BONUS' occurs in 1 lines across src/armor.c.", result.Content);
            Assert.DoesNotContain("do_wear.c", result.Content);
        }
    }

    /// <summary>
    /// A probe query that is not identifier-shaped stays a case-insensitive substring search, so it
    /// still matches inside a longer name.
    /// </summary>
    [Fact]
    public async Task NonIdentifierProbe_StaysASubstringSearch()
    {
        WriteSource("do_wear.c",
            "/* do_wear.c */\r\n" +
            "    total += disarm_bonus(uarm);\r\n" +
            "    total -= disarm_bonus(uarmc);\r\n");

        var (service, netHackService) = await CreateServicesAsync();
        using (service)
        using (netHackService)
        {
            var tool = new SearchDefinitionsTool(service, netHackService);
            var arguments = JsonDocument.Parse("""{"symbol": "ARM_BONUS("}""").RootElement;

            var result = await tool.ExecuteAsync(arguments, CreateContext(4007), CancellationToken.None);

            Assert.True(result.Success);
            Assert.StartsWith("No definition found for '", result.Content);
            Assert.Contains("'ARM_BONUS(' occurs in 2 lines across src/do_wear.c.", result.Content);
        }
    }

    private void WriteSource(string fileName, string content)
    {
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        File.WriteAllText(Path.Combine(_sourceDir, "src", fileName), content);
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
