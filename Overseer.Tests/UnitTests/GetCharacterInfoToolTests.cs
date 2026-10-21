namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// get_character_info against a source index holding the synthetic src/role.c, src/attrib.c and
/// src/prop.c of <see cref="CharacterTablesTests"/>, and against the real GnollHack files when the
/// clone is present on this machine.
/// </summary>
public class GetCharacterInfoToolTests : IDisposable
{
    private readonly string _rootDir;

    public GetCharacterInfoToolTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "GetCharacterInfoToolTests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    private static IConfiguration CreateConfiguration(string sourceDir)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "2000")
            })
            .Build();
    }

    private static async Task<SourceCodeService> StartAsync(string sourceDir)
    {
        var service = new SourceCodeService(CreateConfiguration(sourceDir), NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.InitializationTask;
        return service;
    }

    private async Task<SourceCodeService> CreateSyntheticServiceAsync()
    {
        string sourceDir = Path.Combine(_rootDir, "synthetic");
        Directory.CreateDirectory(Path.Combine(sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "include"));
        File.WriteAllText(Path.Combine(sourceDir, "src", "role.c"), CharacterTablesTests.SyntheticRoleC);
        File.WriteAllText(Path.Combine(sourceDir, "src", "attrib.c"), CharacterTablesTests.SyntheticAttribC);
        File.WriteAllText(Path.Combine(sourceDir, "src", "prop.c"), CharacterTablesTests.SyntheticPropC);
        File.WriteAllText(Path.Combine(sourceDir, "include", "artilist.h"), CharacterTablesTests.SyntheticArtilistH);
        return await StartAsync(sourceDir);
    }

    private async Task<SourceCodeService> CreateRealServiceAsync()
    {
        string sourceDir = Path.Combine(_rootDir, "real");
        Directory.CreateDirectory(Path.Combine(sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "include"));
        foreach (var file in CharacterTablesTests.RealFiles)
        {
            File.Copy(Path.Combine(CharacterTablesTests.GnollHackSourceDir, file), Path.Combine(sourceDir, file));
        }
        return await StartAsync(sourceDir);
    }

    private static Task<ToolResult> ExecuteAsync(IToolHandler tool, object arguments)
    {
        var element = JsonDocument.Parse(JsonSerializer.Serialize(arguments)).RootElement;
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4101) };
        return tool.ExecuteAsync(element, context, TestContext.Current.CancellationToken);
    }

    private static string[] Strings(JsonElement element) => element.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public void Tool_IsExperimental_AndTakesTwoOptionalStrings()
    {
        using var service = new SourceCodeService(CreateConfiguration(_rootDir), NullLogger<SourceCodeService>.Instance);
        IToolHandler tool = new GetCharacterInfoTool(service);

        Assert.True(tool.IsExperimental);
        Assert.Equal("get_character_info", tool.ToolName);
        Assert.False(tool.ParameterSchema.TryGetProperty("required", out _));
        Assert.Equal("string", tool.ParameterSchema.GetProperty("properties").GetProperty("race").GetProperty("type").GetString());
        Assert.Equal("string", tool.ParameterSchema.GetProperty("properties").GetProperty("role").GetProperty("type").GetString());
    }

    [Fact]
    public async Task BeforeIndexing_ReturnsTheSourceIndexingGuard()
    {
        using var service = new SourceCodeService(CreateConfiguration(_rootDir), NullLogger<SourceCodeService>.Instance);
        var tool = new GetCharacterInfoTool(service);

        var result = await ExecuteAsync(tool, new { race = "gnoll" });

        Assert.False(result.Success);
        Assert.Equal(ToolGuardMessages.SourceCodeIndexingInProgress, result.ErrorMessage);
    }

    [Fact]
    public async Task NeitherRaceNorRole_IsAMissingParameter()
    {
        using var service = await CreateSyntheticServiceAsync();
        var tool = new GetCharacterInfoTool(service);

        foreach (var arguments in new object[] { new { }, new { race = "  ", role = "" } })
        {
            var result = await ExecuteAsync(tool, arguments);
            Assert.False(result.Success);
            Assert.Equal("Missing race or role parameter", result.ErrorMessage);
        }
    }

    [Fact]
    public async Task Race_ReturnsAttitudesAllowedRolesAbilitiesTraitsAndTheNote()
    {
        using var service = await CreateSyntheticServiceAsync();
        var tool = new GetCharacterInfoTool(service);

        var result = await ExecuteAsync(tool, new { race = "gnollish" });

        Assert.True(result.Success, result.ErrorMessage);
        using var doc = JsonDocument.Parse(result.Content);
        var race = doc.RootElement.GetProperty("race");
        Assert.Equal("gnoll", race.GetProperty("name").GetString());
        Assert.Equal(new[] { "Healer", "Caveman" }, Strings(race.GetProperty("allowed_roles")));
        Assert.Equal(new[] { "neutral", "chaotic" }, Strings(race.GetProperty("allowed_alignments")));
        Assert.Equal(new[] { "gnoll" }, Strings(race.GetProperty("always_peaceful_races")));
        Assert.Equal(new[] { "human", "dwarf" }, Strings(race.GetProperty("always_hostile_races")));
        Assert.Equal(new[] { "Smells rotten food", "Recognizes corpse properties" }, Strings(race.GetProperty("traits")));

        var ability = Assert.Single(race.GetProperty("abilities_by_level").EnumerateArray());
        Assert.Equal(1, ability.GetProperty("level").GetInt32());
        Assert.Equal("resistant to lycanthropy", ability.GetProperty("ability").GetString());

        Assert.False(doc.RootElement.TryGetProperty("role", out _));
        Assert.False(race.TryGetProperty("raw_definition", out _));
        Assert.Equal(GetCharacterInfoTool.AttitudeNote, doc.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Role_ByFemaleName_ReturnsAllowedRacesAlignmentsQuestArtifactAndAbilities()
    {
        using var service = await CreateSyntheticServiceAsync();
        var tool = new GetCharacterInfoTool(service);

        var result = await ExecuteAsync(tool, new { role = "Cavewoman" });

        Assert.True(result.Success, result.ErrorMessage);
        using var doc = JsonDocument.Parse(result.Content);
        var role = doc.RootElement.GetProperty("role");
        Assert.Equal("Caveman", role.GetProperty("name").GetString());
        Assert.Equal(new[] { "human", "dwarf", "gnoll" }, Strings(role.GetProperty("allowed_races")));
        Assert.Equal(new[] { "lawful", "neutral" }, Strings(role.GetProperty("allowed_alignments")));
        Assert.Equal("ART_SCEPTRE_OF_MIGHT", role.GetProperty("quest_artifact").GetString());
        Assert.Equal(
            new[] { (7, "fast"), (10, "recognizing corpse properties") },
            role.GetProperty("abilities_by_level").EnumerateArray()
                .Select(a => (a.GetProperty("level").GetInt32(), a.GetProperty("ability").GetString()!)));
    }

    [Fact]
    public async Task RaceAndRole_ReturnsBoth()
    {
        using var service = await CreateSyntheticServiceAsync();
        var tool = new GetCharacterInfoTool(service);

        var result = await ExecuteAsync(tool, new { race = "Humans", role = "healers" });

        Assert.True(result.Success, result.ErrorMessage);
        using var doc = JsonDocument.Parse(result.Content);
        Assert.Equal("human", doc.RootElement.GetProperty("race").GetProperty("name").GetString());
        Assert.Equal("Healer", doc.RootElement.GetProperty("role").GetProperty("name").GetString());
        Assert.Equal("The Staff of Aesculapius", doc.RootElement.GetProperty("role").GetProperty("quest_artifact").GetString());
    }

    [Fact]
    public async Task Miss_ListsTheValidRaceAndRoleNames()
    {
        using var service = await CreateSyntheticServiceAsync();
        var tool = new GetCharacterInfoTool(service);

        var result = await ExecuteAsync(tool, new { race = "kobold" });

        Assert.True(result.Success, result.ErrorMessage);
        using var doc = JsonDocument.Parse(result.Content);
        string error = doc.RootElement.GetProperty("error").GetString()!;
        Assert.Contains("No race named 'kobold'.", error);
        Assert.Contains("Valid races: human, dwarf, gnoll.", error);
        Assert.Contains("Valid roles: Healer, Caveman/Cavewoman.", error);
        Assert.False(doc.RootElement.TryGetProperty("note", out _));
    }

    [Fact]
    public void UnparsableRow_ReturnsItsRawDefinitionAndWhy()
    {
        string roleC =
            "const struct Role roles[] = {\n" +
            "    { { \"Tourist\", 0 },\n" +
            "      { { \"Rambler\", 0 } },\n" +
            "      \"Blind Io\", \"_The Lady\", \"Offler\" },\n" +
            "    { { 0, 0 } }\n" +
            "};\n";
        var tables = CharacterTables.Parse(CharacterTablesTests.Lines(roleC), null, null);

        string json = GetCharacterInfoTool.Serialize(GetCharacterInfoTool.BuildResponse(tables, null, "Tourist"));

        using var doc = JsonDocument.Parse(json);
        var role = doc.RootElement.GetProperty("role");
        Assert.Equal("Tourist", role.GetProperty("name").GetString());
        Assert.Contains("Offler", role.GetProperty("raw_definition").GetString());
        Assert.Contains("Could not parse the roles[] entry for 'Tourist'", role.GetProperty("message").GetString());
        Assert.False(role.TryGetProperty("abilities_by_level", out _));
    }

    [Fact]
    public void NoTables_SaysTheyAreNotAvailable()
    {
        string json = GetCharacterInfoTool.Serialize(GetCharacterInfoTool.BuildResponse(null, "gnoll", null));

        using var doc = JsonDocument.Parse(json);
        Assert.Contains("The character tables are not available", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void LongQuery_KeepsTheResultUnderTheCap()
    {
        var tables = CharacterTables.Parse(
            CharacterTablesTests.Lines(CharacterTablesTests.SyntheticRoleC),
            CharacterTablesTests.Lines(CharacterTablesTests.SyntheticAttribC),
            CharacterTablesTests.Lines(CharacterTablesTests.SyntheticPropC));

        string json = GetCharacterInfoTool.Serialize(GetCharacterInfoTool.BuildResponse(tables, new string('x', 20000), new string('y', 20000)));

        Assert.True(json.Length <= GetCharacterInfoTool.MaxResultChars, $"{json.Length} characters");
    }

    [Fact]
    public async Task RealSource_EveryRaceAndRole_FitsUnderTheCap_AndGnollAttitudesHold()
    {
        if (!CharacterTablesTests.RealSourceAvailable) Assert.Skip(CharacterTablesTests.RealSourceUnavailableReason);

        using var service = await CreateRealServiceAsync();
        var tool = new GetCharacterInfoTool(service);
        var tables = service.GetCharacterTables();
        Assert.NotNull(tables);

        foreach (var race in tables!.Races)
        {
            foreach (var role in tables.Roles)
            {
                var result = await ExecuteAsync(tool, new { race = race.Noun, role = role.FemaleName ?? role.Name });
                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(result.Content.Length < GetCharacterInfoTool.MaxResultChars, $"{race.Noun} {role.Name}: {result.Content.Length} characters");
                Assert.DoesNotContain("raw_definition", result.Content);
            }
        }

        var gnoll = await ExecuteAsync(tool, new { race = "gnoll" });
        using var doc = JsonDocument.Parse(gnoll.Content);
        var entry = doc.RootElement.GetProperty("race");
        Assert.Equal(new[] { "gnoll" }, Strings(entry.GetProperty("always_peaceful_races")));
        Assert.Equal(new[] { "human" }, Strings(entry.GetProperty("always_hostile_races")));

        var healer = await ExecuteAsync(tool, new { role = "healers" });
        Assert.Contains("recognizing corpse properties", healer.Content);
        Assert.Contains("The Staff of Aesculapius", healer.Content);
    }
}
