namespace Overseer.Tests.UnitTests;

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

/// <summary>
/// The miss paths of source_code_search and get_function_definition. A miss that says only "not
/// found" gives a model nothing to
/// correct with, so its cheapest recovery is another guess — which is how one benchmark question
/// spent twenty tool rounds guessing identifiers against a corpus that never held them. These
/// assert that a miss names a next action, and that it stays small: every tool result is re-sent on
/// each subsequent round of the same question, so a verbose miss would cost more than it saves.
/// </summary>
public class SourceCodeSearchMissTests : IDisposable
{
    private readonly string _sourceDir;

    public SourceCodeSearchMissTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "SourceCodeSearchMissTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        File.WriteAllText(Path.Combine(_sourceDir, "src", "encounter.c"), @"/* encounter.c */
#include ""hack.h""

void
build_encounter_list(int difficulty)
{
    /* encounter_list is the table the generator walks */
    struct encounter_list *el = &encounter_lists[difficulty];
    if (el->encounter_count > 0)
        pick_encounter(el);
}
");
        File.WriteAllText(Path.Combine(_sourceDir, "src", "makemon.c"), @"/* makemon.c */
#include ""hack.h""

void
makemon_group(int count)
{
    /* group size is decided here */
    int group_size = rnd(count);
    while (group_size-- > 0)
        makemon_one();
}
");
        File.WriteAllText(Path.Combine(_sourceDir, "include", "hack.h"), @"/* hack.h */
#define MAX_ENCOUNTERS 64
");
        /* A struct member reached through a macro alias: the shape that has no body under its own name. */
        File.WriteAllText(Path.Combine(_sourceDir, "include", "winprocs.h"), @"/* winprocs.h */
struct windowprocs { void (*win_print_glyph)(int, int, int); };
#define print_glyph (*windowprocs.win_print_glyph)
");
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

    private (SourceCodeService Service, IConfiguration Config) CreateService(string? sourceCodePath = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", sourceCodePath ?? _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800"),
                new KeyValuePair<string, string?>("Tools:source_code_search:MaxResults", "10"),
                new KeyValuePair<string, string?>("Tools:source_code_search:ContextLines", "5")
            })
            .Build();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (service, config);
    }

    private async Task<ToolResult> SearchAsync(string argsJson)
    {
        var (service, config) = CreateService();
        using (service)
        {
            var netService = new NetHackSourceCodeService(config, NullLogger<NetHackSourceCodeService>.Instance);
            using (netService)
            {
                netService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                var tool = new SourceCodeSearchTool(service, netService, config);
                return await tool.ExecuteAsync(
                    JsonDocument.Parse(argsJson).RootElement,
                    new ToolExecutionContext(),
                    TestContext.Current.CancellationToken);
            }
        }
    }

    private async Task<ToolResult> GetDefinitionAsync(string argsJson, string? sourceCodePath = null)
    {
        var (service, config) = CreateService(sourceCodePath);
        using (service)
        {
            var netService = new NetHackSourceCodeService(config, NullLogger<NetHackSourceCodeService>.Instance);
            using (netService)
            {
                netService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                var tool = new GetFunctionDefinitionTool(service, netService);
                return await tool.ExecuteAsync(
                    JsonDocument.Parse(argsJson).RootElement,
                    new ToolExecutionContext(),
                    TestContext.Current.CancellationToken);
            }
        }
    }

    /// <summary>
    /// The dominant recorded miss shape: one guessed identifier the corpus does not contain, but
    /// whose parts it does. Naming what *is* there is the difference between a recoverable miss and
    /// another round of guessing.
    /// </summary>
    [Fact]
    public async Task Miss_OnAGuessedIdentifier_NamesWhatDoesExist()
    {
        var result = await SearchAsync(@"{""query"": ""ENCOUNTER_GROUP_TABLE""}");

        Assert.True(result.Success, "A miss is not a tool failure.");
        Assert.Contains("No relevant source code found for 'ENCOUNTER_GROUP_TABLE'", result.Content);
        // ENCOUNTER and GROUP are both in the corpus even though the whole identifier is not.
        Assert.Contains("matches", result.Content);
        Assert.Contains("src/encounter.c", result.Content);
        Assert.Contains("search_definitions", result.Content);
        Assert.Contains("list_indexed_files", result.Content);
    }

    [Fact]
    public async Task Miss_OnAnIdentifierWithNoNeighbours_SaysSoRatherThanStayingSilent()
    {
        var result = await SearchAsync(@"{""query"": ""zzzqqqxxx""}");

        Assert.True(result.Success);
        Assert.Contains("No relevant source code found for 'zzzqqqxxx'", result.Content);
        Assert.Contains("No shorter form of this identifier matched either.", result.Content);
        Assert.Contains("search_definitions", result.Content);
    }

    /// <summary>
    /// Five of six recorded identifier misses were scoped to one file, and the model was never told
    /// that the filter rather than the corpus may have been what excluded the hit.
    /// </summary>
    [Fact]
    public async Task Miss_UnderAFileFilter_NamesTheFilter()
    {
        var result = await SearchAsync(@"{""query"": ""encounter_list"", ""file_filter"": ""makemon.c""}");

        Assert.True(result.Success);
        Assert.Contains("file_filter='makemon.c' may be excluding the match", result.Content);
    }

    /// <summary>
    /// A multi-word query matches only where those exact characters are contiguous on one line. A
    /// reader who does not know that reads the miss as "the game does not do this".
    /// </summary>
    [Fact]
    public async Task Miss_OnAMultiWordPhrase_StatesTheLiteralSubstringRuleAndProbesTheTerms()
    {
        var result = await SearchAsync(@"{""query"": ""encounter group size table""}");

        Assert.True(result.Success);
        Assert.Contains("No line contains it as one literal substring", result.Content);
        Assert.Contains("spacing matters", result.Content);
        // The individual terms do occur, in different files.
        Assert.Contains("'encounter' matches", result.Content);
        Assert.Contains("'group' matches", result.Content);
    }

    [Fact]
    public async Task Miss_OnAPhraseWhoseSpacingIsWrong_ReportsTheCollapsedForm()
    {
        // The corpus has "group_size", never "group _size".
        var result = await SearchAsync(@"{""query"": ""group_ size""}");

        Assert.True(result.Success);
        Assert.Contains("With whitespace removed, 'group_size' matches", result.Content);
        Assert.Contains("src/makemon.c", result.Content);
    }

    /// <summary>
    /// The cost guard. A miss is re-sent on every subsequent round of the same question, so its
    /// payload has to stay small — this change exists to reduce token spend, not to add to it.
    /// </summary>
    [Fact]
    public async Task Miss_StaysSmallEnoughToBeReSentEveryRound()
    {
        foreach (string args in new[]
        {
            @"{""query"": ""ENCOUNTER_GROUP_TABLE""}",
            @"{""query"": ""encounter group size table""}",
            @"{""query"": ""group_ size""}",
            @"{""query"": ""zzzqqqxxx"", ""file_filter"": ""src/encounter.c""}"
        })
        {
            var result = await SearchAsync(args);
            Assert.True(result.Content.Length < 900,
                $"Miss payload for {args} was {result.Content.Length} characters: {result.Content}");
        }
    }

    /// <summary>
    /// A regex miss takes no probes — a near-neighbour probe on a pattern would be guessing about a
    /// guess — and must still return a well-formed result rather than throwing.
    /// </summary>
    [Fact]
    public async Task Miss_OnARegexQuery_ReturnsAPlainMissWithoutProbing()
    {
        var result = await SearchAsync(@"{""query"": ""zzz[0-9]+qqq"", ""is_regex"": true}");

        Assert.True(result.Success);
        Assert.Contains("No relevant source code found for 'zzz[0-9]+qqq'", result.Content);
        Assert.DoesNotContain("matches", result.Content);
        Assert.Contains("search_definitions", result.Content);
    }

    /// <summary>
    /// An invalid regex never reaches the miss path at all: SearchFiles returns the compiler's
    /// message as ordinary content, and the tool wraps that into a `Success = true` result. Pinned
    /// because it is a standing trap — a "successful" call count can include calls that failed to
    /// compile their own pattern — and because the miss path must not swallow the message either.
    /// </summary>
    [Fact]
    public async Task InvalidRegex_ReturnsTheCompilerMessageAsContent_NotAMiss()
    {
        var result = await SearchAsync(@"{""query"": ""zzz([0-9"", ""is_regex"": true}");

        Assert.True(result.Success);
        Assert.StartsWith("Error: Invalid regular expression.", result.Content);
        Assert.DoesNotContain("No relevant source code found", result.Content);
    }

    /// <summary>A hit is unaffected: the miss path must not have changed what a successful search returns.</summary>
    [Fact]
    public async Task Hit_IsUnchangedByTheMissPath()
    {
        var result = await SearchAsync(@"{""query"": ""encounter_list""}");

        Assert.True(result.Success);
        Assert.Contains("src/encounter.c", result.Content);
        Assert.DoesNotContain("No relevant source code found", result.Content);
    }

    /// <summary>
    /// A struct member reached through a macro alias has no body declared under its own name, so
    /// every extraction of it misses. The payload names the file that does mention it, which is the
    /// one place a model can read it from, and keeps the service's own opening sentence as its prefix
    /// because other tooling matches on that literal.
    /// </summary>
    [Fact]
    public async Task DefinitionMiss_OnAStructMember_NamesTheFileItOccursIn()
    {
        var result = await GetDefinitionAsync(@"{""name"": ""win_print_glyph""}");

        Assert.True(result.Success, "A miss is not a tool failure.");
        Assert.StartsWith("No definition found for 'win_print_glyph' of kind 'any'.", result.Content);
        Assert.Contains("'win_print_glyph' occurs in", result.Content);
        Assert.Contains("include/winprocs.h", result.Content);
        Assert.Contains("source_code_search", result.Content);
        Assert.Contains("context_lines", result.Content);
        Assert.Contains("search_definitions", result.Content);
        Assert.True(result.Content.Length < 600,
            $"Miss payload was {result.Content.Length} characters: {result.Content}");
    }

    [Fact]
    public async Task DefinitionMiss_OnAnIdentifierThatOccursNowhere_SaysSo()
    {
        var result = await GetDefinitionAsync(@"{""name"": ""zzzqqqxxx""}");

        Assert.True(result.Success);
        Assert.StartsWith("No definition found for 'zzzqqqxxx' of kind 'any'.", result.Content);
        Assert.Contains("'zzzqqqxxx' does not occur in the indexed gnollhack source.", result.Content);
        Assert.Contains("search_definitions", result.Content);
        Assert.True(result.Content.Length < 600,
            $"Miss payload was {result.Content.Length} characters: {result.Content}");
    }

    /// <summary>
    /// The probe cannot report anything about a corpus that is not on disk. Indexing still completes
    /// over the empty index, so the tool answers rather than guarding, and the payload invents no
    /// file. This is as close as the miss path's own fallback can be reached from outside: an empty
    /// index makes SearchFiles return an empty string rather than throw, so the "no hit" branch —
    /// not the outer catch — is what answers here.
    /// </summary>
    [Fact]
    public async Task DefinitionMiss_WithNoCorpusOnDisk_NamesNoFile()
    {
        string missing = Path.Combine(_sourceDir, "does_not_exist");
        Assert.False(Directory.Exists(missing));

        var result = await GetDefinitionAsync(@"{""name"": ""win_print_glyph""}", missing);

        Assert.True(result.Success);
        Assert.StartsWith("No definition found for 'win_print_glyph' of kind 'any'.", result.Content);
        Assert.Contains("does not occur in the indexed gnollhack source.", result.Content);
        Assert.DoesNotContain("occurs in ", result.Content);
        Assert.DoesNotContain("winprocs.h", result.Content);
        Assert.True(result.Content.Length < 600,
            $"Miss payload was {result.Content.Length} characters: {result.Content}");
    }
}
