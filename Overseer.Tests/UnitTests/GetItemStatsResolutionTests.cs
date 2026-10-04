namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Xunit;

/// <summary>
/// get_item_stats resolves a name that misses to the one item whose name ends in "of &lt;name&gt;",
/// and says so in its message; zero or several such items leave the miss unchanged. Failing that, it
/// resolves a name carrying one or two trailing words to the one item named by the rest, which must
/// keep at least two words. Runs against
/// the real src/objects.c and include/objclass.h, copied from the GnollHack clone when it is present
/// on this machine, so the cases are the real item names a model asks for.
/// </summary>
public class GetItemStatsResolutionTests : IDisposable
{
    private const string GnollHackSourceDir = @"c:\hmp\GnollHack";

    private readonly string _sourceDir;

    public GetItemStatsResolutionTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "GetItemStatsResolutionTests_" + Guid.NewGuid().ToString("N"));
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

    private static bool SourceAvailable =>
        File.Exists(Path.Combine(GnollHackSourceDir, "src", "objects.c"))
        && File.Exists(Path.Combine(GnollHackSourceDir, "include", "objclass.h"));

    private async Task<SourceCodeService> CreateServiceAsync()
    {
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));
        File.Copy(Path.Combine(GnollHackSourceDir, "src", "objects.c"), Path.Combine(_sourceDir, "src", "objects.c"));
        File.Copy(Path.Combine(GnollHackSourceDir, "include", "objclass.h"), Path.Combine(_sourceDir, "include", "objclass.h"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "2000")
            })
            .Build();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);
        return service;
    }

    [Fact]
    public async Task BareSuffix_ResolvesToTheOneItemNamedOfIt_WithANote()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        var experience = service.GetItemStats("experience");
        Assert.Null(experience.Error);
        Assert.NotNull(experience.Stats);
        Assert.Contains("MISCELLANEOUSITEM(\"ioun stone of experience\"", experience.RawDefinition);
        Assert.StartsWith(
            "Resolved 'experience' to 'ioun stone of experience': no item is named 'experience'.",
            experience.Message);

        var strength = service.GetItemStats("hill giant strength");
        Assert.Null(strength.Error);
        Assert.Contains("\"belt of hill giant strength\"", strength.RawDefinition);
        Assert.StartsWith(
            "Resolved 'hill giant strength' to 'belt of hill giant strength': no item is named 'hill giant strength'.",
            strength.Message);
    }

    [Fact]
    public async Task ExactName_ResolvesDirectly_WithoutANote()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        var digging = service.GetItemStats("digging");
        Assert.Null(digging.Error);
        Assert.Contains("WAND(\"digging\"", digging.RawDefinition);
        Assert.DoesNotContain("Resolved '", digging.Message ?? string.Empty);
    }

    [Fact]
    public async Task SuffixSharedByTwoItems_StaysAMissWithSuggestions()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        // Two item names end in "of dexterity", and no item is named "dexterity".
        var response = service.GetItemStats("dexterity");
        Assert.Null(response.Stats);
        Assert.NotNull(response.Error);
        Assert.StartsWith("No item named 'dexterity' found in the game data.", response.Error);
        Assert.Contains("Did you mean:", response.Error);
    }

    [Fact]
    public async Task NameWithNoSuffixMatch_StaysTheUsualMiss()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        var response = service.GetItemStats("quuxwidget");
        Assert.Null(response.Stats);
        Assert.NotNull(response.Error);
        Assert.StartsWith("No item named 'quuxwidget' found in the game data.", response.Error);
    }

    [Fact]
    public async Task ObjectClass_StillFiltersTheSuffixMatch()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        var response = service.GetItemStats("experience", "WEAPON_CLASS");
        Assert.Null(response.Stats);
        Assert.NotNull(response.Error);
        Assert.StartsWith("No item named 'experience' found in the game data.", response.Error);
    }

    [Fact]
    public async Task TrailingWords_ResolveToTheItemNamedByTheRest_WithANote()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        var mail = service.GetItemStats("silver dragon scale mail concept?");
        Assert.Null(mail.Error);
        Assert.Contains("DRGN_ARMR(\"silver dragon scale mail\"", mail.RawDefinition);
        Assert.StartsWith(
            "Resolved 'silver dragon scale mail concept?' to 'silver dragon scale mail': no item is named 'silver dragon scale mail concept?'.",
            mail.Message);

        var belt = service.GetItemStats("belt of hill giant strength excluding");
        Assert.Null(belt.Error);
        Assert.NotNull(belt.Stats);
        Assert.Contains("\"belt of hill giant strength\"", belt.RawDefinition);
        Assert.StartsWith(
            "Resolved 'belt of hill giant strength excluding' to 'belt of hill giant strength': no item is named 'belt of hill giant strength excluding'.",
            belt.Message);

        var twoWords = service.GetItemStats("belt of hill giant strength worth wearing");
        Assert.Null(twoWords.Error);
        Assert.StartsWith(
            "Resolved 'belt of hill giant strength worth wearing' to 'belt of hill giant strength':",
            twoWords.Message);
    }

    [Fact]
    public async Task ThreeTrailingWords_StayAMiss()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        var response = service.GetItemStats("belt of hill giant strength worth wearing now");
        Assert.Null(response.Stats);
        Assert.NotNull(response.Error);
        Assert.StartsWith("No item named 'belt of hill giant strength worth wearing now' found in the game data.", response.Error);
    }

    [Fact]
    public async Task Appearance_StaysAMissWithItsAppearanceNote()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        // "orange" is an item, but a one-word prefix never resolves.
        var response = service.GetItemStats("orange potion");
        Assert.Null(response.Stats);
        Assert.NotNull(response.Error);
        Assert.StartsWith("No item named 'orange potion' found in the game data.", response.Error);
        Assert.Contains("appearances are randomized per game", response.Error);
        Assert.DoesNotContain("Resolved '", response.Message ?? string.Empty);
    }

    [Fact]
    public async Task ObjectClass_StillFiltersTheTrailingWordMatch()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        var excluded = service.GetItemStats("belt of hill giant strength excluding", "WEAPON_CLASS");
        Assert.Null(excluded.Stats);
        Assert.NotNull(excluded.Error);
        Assert.StartsWith("No item named 'belt of hill giant strength excluding' found in the game data.", excluded.Error);

        var included = service.GetItemStats("silver dragon scale mail concept?", "ARMOR_CLASS");
        Assert.Null(included.Error);
        Assert.Contains("DRGN_ARMR(\"silver dragon scale mail\"", included.RawDefinition);
        Assert.StartsWith("Resolved 'silver dragon scale mail concept?' to 'silver dragon scale mail':", included.Message);
    }

    [Fact]
    public async Task ExactMultiWordName_ResolvesDirectly_WithoutANote()
    {
        if (!SourceAvailable) return;

        using var service = await CreateServiceAsync();

        var mail = service.GetItemStats("silver dragon scale mail");
        Assert.Null(mail.Error);
        Assert.Contains("DRGN_ARMR(\"silver dragon scale mail\"", mail.RawDefinition);
        Assert.DoesNotContain("Resolved '", mail.Message ?? string.Empty);
    }
}
