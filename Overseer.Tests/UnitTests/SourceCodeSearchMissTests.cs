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
    /// that the filter rather than the corpus may have been what excluded the hit. The unfiltered
    /// probe now also says where the same query does match.
    /// </summary>
    [Fact]
    public async Task Miss_UnderAFileFilter_NamesTheFilter()
    {
        var result = await SearchAsync(@"{""query"": ""encounter_list"", ""file_filter"": ""makemon.c""}");

        Assert.True(result.Success);
        Assert.Contains("file_filter='makemon.c' may be excluding the match", result.Content);
        Assert.Contains("Without file_filter, 'encounter_list' matches", result.Content);
        Assert.Contains("src/encounter.c", result.Content);
    }

    /// <summary>The unfiltered probe can miss too: the query occurs nowhere, filter or no filter.</summary>
    [Fact]
    public async Task Miss_UnderAFileFilter_WhenQueryExistsNowhere_TheUnfilteredProbeSaysSoToo()
    {
        var result = await SearchAsync(@"{""query"": ""zzzqqqxxx"", ""file_filter"": ""makemon.c""}");

        Assert.True(result.Success);
        Assert.Contains("The same search without file_filter found no match either.", result.Content);
        Assert.DoesNotContain("it does not occur in any indexed file", result.Content);
    }

    /// <summary>
    /// A regex query is rewritten before BuildMissContent sees it under whole_word, and is_regex is
    /// passed through directly; in both cases the unfiltered probe is skipped, deliberately, because
    /// a near-miss probe on a pattern would be guessing about a guess.
    /// </summary>
    [Fact]
    public async Task Miss_UnderAFileFilter_WithRegexOrWholeWord_SkipsTheUnfilteredProbe()
    {
        var regexResult = await SearchAsync(@"{""query"": ""zzz[0-9]+qqq"", ""file_filter"": ""makemon.c"", ""is_regex"": true}");
        Assert.True(regexResult.Success);
        Assert.DoesNotContain("Without file_filter", regexResult.Content);
        Assert.DoesNotContain("without file_filter found no match", regexResult.Content);

        var wholeWordResult = await SearchAsync(@"{""query"": ""zzzqqqxxx"", ""file_filter"": ""makemon.c"", ""whole_word"": true}");
        Assert.True(wholeWordResult.Success);
        Assert.DoesNotContain("Without file_filter", wholeWordResult.Content);
        Assert.DoesNotContain("without file_filter found no match", wholeWordResult.Content);
    }

    /// <summary>
    /// The cost guard for the enriched, file-filtered miss specifically: it carries more text than a
    /// plain miss (the unfiltered file lead), so it gets its own, tighter bound.
    /// </summary>
    [Fact]
    public async Task Miss_UnderAFileFilter_StaysWithinTheEnrichedBound()
    {
        foreach (string args in new[]
        {
            @"{""query"": ""encounter_list"", ""file_filter"": ""makemon.c""}",
            @"{""query"": ""zzzqqqxxx"", ""file_filter"": ""makemon.c""}"
        })
        {
            var result = await SearchAsync(args);
            Assert.True(result.Content.Length <= 600,
                $"Enriched filtered miss for {args} was {result.Content.Length} characters: {result.Content}");
        }
    }

    /// <summary>
    /// A long query and a long file_filter path together would push the enriched miss well past 600
    /// characters without the echoed-query truncation; the truncated echo carries the ellipsis, and
    /// the closing sentence still survives.
    /// </summary>
    [Fact]
    public async Task Miss_UnderAFileFilter_WithALongQueryAndLongPath_StaysUnderTheBoundAndKeepsTheClosingSentence()
    {
        string longQuery = new string('a', 120) + "_zzz_unmatched_identifier";
        string longFilter = "some/deeply/nested/path/that/does/not/exist/in/the/index/either/quite/a/long/filename.c";
        string argsJson = "{\"query\": \"" + longQuery + "\", \"file_filter\": \"" + longFilter + "\"}";

        var result = await SearchAsync(argsJson);

        Assert.True(result.Success);
        Assert.True(result.Content.Length <= 600,
            $"Enriched filtered miss was {result.Content.Length} characters: {result.Content}");
        Assert.Contains("…", result.Content);
        Assert.Contains("Try search_definitions", result.Content);
    }

    /// <summary>
    /// Without a file_filter the miss path takes exactly the branch it took before the unfiltered
    /// probe existed: the new code re-enters through a temporary buffer, but the resulting bytes
    /// must not move.
    /// </summary>
    [Fact]
    public async Task Miss_WithNoFileFilter_IsByteIdenticalToTheUnenrichedPath()
    {
        var result = await SearchAsync(@"{""query"": ""zzzqqqxxx""}");

        Assert.Equal(
            "No relevant source code found for 'zzzqqqxxx'. No shorter form of this identifier matched either." +
            " Try search_definitions for a known symbol, or list_indexed_files to see what is indexed.",
            result.Content);
    }

    /// <summary>
    /// RunProbe's three states, tested directly: SafeProbe collapses an exception, an "Error:"
    /// result and an empty result into one empty string, and SearchFiles is non-virtual, so this is
    /// the only seam that can prove a thrown exception is told apart from a clean miss.
    /// </summary>
    [Fact]
    public void RunProbe_OnAThrowingDelegate_ReturnsFailed()
    {
        var (state, summary) = SourceCodeSearchTool.RunProbe(() => throw new InvalidOperationException("boom"));

        Assert.Equal(SourceCodeSearchTool.ProbeState.Failed, state);
        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void RunProbe_OnAnErrorResult_ReturnsFailed()
    {
        var (state, summary) = SourceCodeSearchTool.RunProbe(() => "Error: invalid pattern");

        Assert.Equal(SourceCodeSearchTool.ProbeState.Failed, state);
        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void RunProbe_OnAnEmptyResult_ReturnsNoMatch()
    {
        var (state, summary) = SourceCodeSearchTool.RunProbe(() => "");

        Assert.Equal(SourceCodeSearchTool.ProbeState.NoMatch, state);
        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void RunProbe_OnAHit_ReturnsTheResultUnchanged()
    {
        var (state, summary) = SourceCodeSearchTool.RunProbe(() => "src/encounter.c (2 matches)");

        Assert.Equal(SourceCodeSearchTool.ProbeState.Hit, state);
        Assert.Equal("src/encounter.c (2 matches)", summary);
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

    /// <summary>
    /// get_item_stats's own miss path: src/objects.c is indexed and holds a real item, but the
    /// query names none of the recognised item macros verbatim (here because the fixture entry is
    /// built through a wrapper macro get_item_stats's line regex does not itself recognise), so
    /// GetItemStats falls into its <c>matchLine == -1</c> branch even though the item is indexed.
    /// The miss names a near neighbour drawn from a word of the query, keeps the existing
    /// item_lookup/wiki_search pointer, and always appends the appearance-vs-discoveries reminder.
    /// </summary>
    [Fact]
    public async Task ItemStatsMiss_NamesANearNeighbourAndAlwaysAppendsTheAppearanceReminder()
    {
        string itemsDir = Path.Combine(Path.GetTempPath(), "SourceCodeItemStatsMissTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(itemsDir, "src"));
        try
        {
            File.WriteAllLines(
                Path.Combine(itemsDir, "src", "objects.c"),
                FixtureObjectsC(DragonSuitEntry("hooded cloak")));

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("SourceCodePath", itemsDir),
                    new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
                })
                .Build();

            var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
            await service.StartAsync(TestContext.Current.CancellationToken);
            using (service)
            {
                // "hooded" (>=4 chars) matches the indexed "hooded cloak"; "mantle" matches nothing.
                var response = service.GetItemStats("hooded mantle");

                Assert.Null(response.Stats);
                Assert.NotNull(response.Error);
                Assert.StartsWith("No item named 'hooded mantle' found in the game data.", response.Error);
                Assert.Contains("Did you mean: hooded cloak.", response.Error);
                Assert.Contains("item_lookup or wiki_search", response.Error);
                Assert.Contains(
                    "A name that is an unidentified appearance — 'hooded cloak', 'orange potion', 'red mushroom' — has no entry: appearances are randomized per game; see the snapshot's Discoveries section.",
                    response.Error);
                Assert.True(response.Error!.Length <= 600,
                    $"Miss payload was {response.Error.Length} characters: {response.Error}");
            }
        }
        finally
        {
            if (Directory.Exists(itemsDir))
            {
                Directory.Delete(itemsDir, true);
            }
        }
    }

    /* ==================================================================================
     * A synthetic objects.c, shaped exactly like the real one, copied from
     * ObjectsMacroResolverTests: two compilation passes over one array, OBJ and the pass-1
     * OBJECT in the first, BITS and the pass-2 OBJECT in the second, and the DRGN_ARMR-style
     * wrapper chain that lets one call site register a real item name without needing
     * objclass.h. Kept local to this test rather than shared, because it exists only to get a
     * real item into ObjectsMacroResolver.FindItemNames for the get_item_stats miss path above.
     * =============================================================================== */

    private static string[] FixtureObjectsC(params string[] entries)
    {
        var lines = new List<string>
        {
            "/* objects.c fixture */",
            "#ifndef OBJECTS_PASS_2_",
            "/* first pass -- object descriptive text */",
            "#define OBJ(name,desc,contentname,contentdesc,itemdesc,height,odflags,stand_anim,enlarge,replacement)  name, desc, contentname, contentdesc, itemdesc, height, odflags, stand_anim, enlarge, replacement",
            "#define OBJECT(obj,bits,prp1,prp2,prp3,pflags,sym,prob,multigen,dly,wt,cost, \\",
            "               dmgtype,sdice,sdam,sdmgplus,ldice,ldam,ldmgplus, edmgtype,edice,edam,edmgplus,aflags,aflags2,critpct,  hitbon,mcadj,fixdmgbon,range,  oc1,oc2,oc3,oc4,oc5,oc6,oc7,oc8,  nut,color, soundset,  dirsubtype,materials,cooldown,special_quality,  powconfermask,permittedtargets,flags,flags2,flags3,flags4,flags5,flags6)  { obj }",
            "#define None (char *) 0",
            "",
            "NEARDATA struct objdescr obj_descr[] =",
            "#else",
            "/* second pass -- object definitions */",
            "#define BITS(nmkn,mrg,uskn,ctnr,mgc,spetype,chrg,recharging,uniq,nwsh,big,tuf,dir,sub,skill,matinit,mtrl) \\",
            "  nmkn,mrg,uskn,0,mgc,spetype,chrg,recharging,uniq,nwsh,big,tuf,dir,matinit,mtrl,sub,skill",
            "#define OBJECT(obj,bits,prp1,prp2,prp3,pflags,sym,prob,multigen,dly,wt,cost,dmgtype,sdice,sdam,sdmgplus,ldice,ldam,ldmgplus,edmgtype,edice,edam,edmgplus,aflags,aflags2,critpct,   hitbon,mcadj,fixdmgbon,range,  oc1,oc2,oc3,oc4,oc5,oc6,oc7,oc8,  nut,color,soundset,  dirsubtype,materials,cooldown,special_quality,  powconfermask,permittedtargets,flags,flags2,flags3,flags4,flags5,flags6) \\",
            "  { 0, 0, (char *) 0, bits, prp1, prp2, prp3, pflags, sym, dly, color, prob, wt, nut,  \\",
            "    cost, dmgtype, sdice, sdam, sdmgplus, ldice, ldam, ldmgplus, edmgtype, edice, edam, edmgplus, aflags, aflags2, hitbon, mcadj, fixdmgbon, range,   oc1, oc2, oc3, oc4, oc5, oc6, oc7, oc8,   dirsubtype, materials, cooldown, special_quality,  flags,flags2,flags3,flags4,flags5,flags6,powconfermask, permittedtargets, critpct, multigen, soundset }",
            "",
            "NEARDATA struct objclass objects[] =",
            "#endif",
            "{",
            "    /* dummy object[0] -- excluded by its ILLOBJ_CLASS */",
            "OBJECT(OBJ(\"strange object\", None, None, None, None, 0, OD_NONE, NO_ANIMATION, NO_ENLARGEMENT, NO_REPLACEMENT), \\",
            "    BITS(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, P_NONE, MATINIT_BASE_MATERIAL, MAT_NONE), \\",
            "    NO_POWER, NO_POWER, NO_POWER, P1_NONE, ILLOBJ_CLASS, 0, MULTIGEN_SINGLE, 0, 0, 0, \\",
            "    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, A1_NONE, A2_NONE, 0, \\",
            "    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, \\",
            "    0, 0, OBJECT_SOUNDSET_NONE, \\",
            "    0, 0, 0, 0, \\",
            "    PERMITTED_ALL, ALL_TARGETS,",
            "    O1_NONE, O2_NONE, O3_NONE, O4_NONE, O5_NONE, O6_NONE),",
            "",
            "/* --- the armour chain: the ac argument is stored as 10 - ac ------------------ */",
            "#define GENERAL_ARMOR(name,desc,kn,mgc,blk,power,power2,power3,pflags,enchtype,prob,delay,wt,  \\",
            "            cost,ac,mgccancel,manabon,hpbon,bonusattrs,attrbonus,splcastpen,sub,skill,matinit,metal,c,height,soundset,\\",
            "            flags,flags2,flags3,flags4,flags5,flags6,powconfermask,odflags,anim,enl,repl)           \\",
            "        OBJECT(OBJ(name, desc, None, None, None, height, odflags, anim, enl, repl),                       \\",
            "            BITS(kn, 0, 1, 0, mgc, enchtype, CHARGED_NOT_CHARGED, RECHARGING_NOT_RECHARGEABLE, 0, 0, blk, 0, 0, sub, skill, matinit, metal),  \\",
            "            power, power2, power3, pflags, ARMOR_CLASS, prob, MULTIGEN_SINGLE, delay, wt, cost,             \\",
            "            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, A1_NONE, A2_NONE, 0, \\",
            "            0, 0, 0, 0, 10 - ac, mgccancel, manabon, hpbon, bonusattrs, attrbonus, splcastpen, 0, \\",
            "            wt, c, soundset,\\",
            "            0, 0, 0, 0, \\",
            "            powconfermask, ALL_TARGETS, flags, flags2, flags3, flags4, flags5, flags6 )",
            "",
            "#define ARMOR(name,desc,kn,mgc,blk,power,power2,power3,pflags,enchtype,prob,delay,wt,  \\",
            "            cost,ac,mgccancel,manabon,hpbon,bonusattrs,attrbonus,splcastpen,sub,skill,matinit,metal,c,height,soundset,flags,flags2,flags3,flags4,flags5,flags6,powconfermask)           \\",
            "        GENERAL_ARMOR(name,desc,kn,mgc,blk,power,power2,power3,pflags,enchtype,prob,delay,wt,  \\",
            "            cost,ac,mgccancel,manabon,hpbon,bonusattrs,attrbonus,splcastpen,sub,skill,matinit,metal,c,height,soundset,flags,flags2,flags3,flags4,flags5,flags6,powconfermask,OD_NONE,NO_ANIMATION,NO_ENLARGEMENT,NO_REPLACEMENT)",
            "",
            "/* A wrapper that injects delay, weight, category and material of its own. */",
            "#define TEST_DRGN_ARMR(name,mgc,power,power2,power3,pflags,cost,ac,mc,manabon,hpbon,bonusattrs,attrbonus,splcastpen,color,soundset,flags,flags2,flags3,flags4,flags5,flags6,powconfermask)  \\",
            "    ARMOR(name, None, 1, mgc, 1, power, power2, power3, pflags, ENCHTYPE_GENERAL_ARMOR, 0, 5, 550,  \\",
            "      cost, ac, mc, manabon, hpbon, bonusattrs, attrbonus, splcastpen, ARM_SUIT, P_NONE, MATINIT_BASE_MATERIAL, MAT_DRAGON_HIDE, color, 0, soundset, flags, flags2, flags3, O4_NON_MYTHIC | O4_CAN_HAVE_EXCEPTIONALITY | flags4, O5_NO_CATALOGUE | flags5, O6_NORMALLY_NON_EXCEPTIONAL | flags6, powconfermask)",
            ""
        };

        lines.AddRange(entries);
        lines.Add("");
        lines.Add("    /* array terminator */");
        lines.Add("OBJECT(OBJ(None, None, None, None, None, 0, OD_NONE, NO_ANIMATION, NO_ENLARGEMENT, NO_REPLACEMENT), \\");
        lines.Add("    BITS(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, P_NONE, MATINIT_BASE_MATERIAL, MAT_NONE), \\");
        lines.Add("    NO_POWER, NO_POWER, NO_POWER, P1_NONE, ILLOBJ_CLASS, 0, MULTIGEN_SINGLE, 0, 0, 0, \\");
        lines.Add("    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, A1_NONE, A2_NONE, 0, \\");
        lines.Add("    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, \\");
        lines.Add("    0, 0, OBJECT_SOUNDSET_NONE, \\");
        lines.Add("    0, 0, 0, 0, \\");
        lines.Add("    PERMITTED_ALL, ALL_TARGETS,");
        lines.Add("    O1_NONE, O2_NONE, O3_NONE, O4_NONE, O5_NONE, O6_NONE)");
        lines.Add("};");
        return lines.ToArray();
    }

    /// <summary>The dragon-hide suit entry, in the shape objects.c writes it, renamed for one item name.</summary>
    private static string[] DragonSuitEntry(string name) => new[]
    {
        $"TEST_DRGN_ARMR(\"{name}\",",
        "    1, COLD_RESISTANCE, REFLECTING, NO_POWER, P1_NONE,",
        "    6000, 1, 4, 0, 0, 0, 0, 5, DRAGON_SILVER, OBJECT_SOUNDSET_GENERIC, ",
        "    O1_NONE, O2_DRAGON_ITEM | O2_MONSTER_SCALE_MAIL, O3_NONE, O4_NONE, O5_NONE, O6_NONE, PERMITTED_ALL),"
    };
}
