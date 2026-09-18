namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Xunit;

/// <summary>
/// get_monster_stats against every macro src/monst.c defines a monster with. The five forms share
/// argument slots 0-26 and differ only in the animation, enlargement and replacement slots appended
/// after them, so each must be found by name and must yield the same positional Level 1 parse.
/// The fixture is a synthetic monst.c holding one entry per form, each with its own level and
/// difficulty so a parse that read the wrong entry or the wrong slot cannot pass.
/// </summary>
public class MonsterStatsMacroFormTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly SourceCodeService _service;

    public MonsterStatsMacroFormTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "MonsterStatsMacroFormTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        File.WriteAllText(Path.Combine(_sourceDir, "src", "monst.c"), FixtureMonstC);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
            })
            .Build();

        _service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        _service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _service.Dispose();
        try
        {
            if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    [Theory]
    [InlineData("fixture newt", "S_LIZARD", 1, 11)]          /* MON */
    [InlineData("fixture fox", "S_DOG", 2, 12)]              /* ANIMATED_MON */
    [InlineData("fixture jackal", "S_DOG", 3, 13)]           /* ENLARGED_MON */
    [InlineData("fixture hell hound", "S_DOG", 4, 14)]       /* ENLARGED_ANIMATED_MON */
    [InlineData("fixture little dog", "S_DOG", 5, 15)]       /* GENERAL_MON */
    public void EveryMonsterMacroForm_IsFoundAndParsedPositionally(string name, string mlet, int level, int difficulty)
    {
        var response = _service.GetMonsterStats(name);

        Assert.Null(response.Error);
        Assert.Null(response.RawDefinition);
        Assert.NotNull(response.Stats);
        var fields = response.Stats!.Fields;
        Assert.Equal(name, fields["mname"]);
        Assert.Equal(mlet, fields["mlet"]);
        Assert.Equal(level, fields["mlevel"]);
        Assert.Equal(difficulty, fields["difficulty"]);
        Assert.Equal("HI_DOMESTIC", fields["mcolor"]);
    }

    [Fact]
    public void GeneralMon_TrailingActionInfoSlotsDoNotShiftTheParse()
    {
        var response = _service.GetMonsterStats("fixture little dog");

        Assert.NotNull(response.Stats);
        var fields = response.Stats!.Fields;
        Assert.Equal("small domestic canine", fields["mdescription"]);
        Assert.Equal(18, fields["mmove"]);
        Assert.Equal(6, fields["ac"]);
        Assert.Equal(new List<string> { "G_GENO" }, fields["geno"]);
        Assert.Equal(1, fields["geno_frequency"]);
        Assert.Equal(150, fields["cwt"]);
        Assert.Equal("MS_BARK", fields["msound"]);
        Assert.Equal(8, fields["str"]);
        Assert.Equal(2, fields["cha"]);
        Assert.Equal("MR_NONE", fields["mresists"]);
        Assert.Equal(
            new[] { "M1_ANIMAL", "M1_NOHANDS", "M1_CARNIVORE" },
            Assert.IsAssignableFrom<IEnumerable<string>>(fields["mflags1"]));
        Assert.Equal("M8_NONE", fields["mflags8"]);

        var attacks = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object>>>(fields["mattk"]).ToList();
        Assert.Equal("AT_BITE", attacks[0]["aatyp"]);
        Assert.Equal(1, attacks[0]["damn"]);
        Assert.Equal(6, attacks[0]["damd"]);
    }

    [Fact]
    public void SearchMonsters_ListsEveryMacroForm()
    {
        var names = _service.SearchMonsters("fixture").ToList();

        Assert.Equal(
            new[] { "fixture newt", "fixture fox", "fixture jackal", "fixture hell hound", "fixture little dog" },
            names);
    }

    [Fact]
    public void UnknownMonster_StillReportsNotFound()
    {
        var response = _service.GetMonsterStats("fixture nonesuch");

        Assert.Null(response.Stats);
        Assert.StartsWith("No monster named 'fixture nonesuch' found in the game data.", response.Error);
    }

    /* Shaped like the real src/monst.c: the macro definitions, then one entry per macro form. The
       GENERAL_MON entry keeps the real file's layout, its six trailing slots on their own lines with
       nested ACTION_INFO calls in the last two. */
    private const string FixtureMonstC = @"/* monst.c */
#define GENERAL_MON(nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, col, soundset, female_soundset, soundset_subtype, anim, female_anim, enlarge, female_enlarge, replacement, female_replacement) \
    { nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, C(col), soundset, female_soundset, soundset_subtype, anim, female_anim, enlarge, female_enlarge, replacement, female_replacement }

#define ENLARGED_ANIMATED_MON(nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, col, soundset, female_soundset, soundset_subtype, anim, female_anim, enlarge, female_enlarge) \
    { nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, C(col), soundset, female_soundset, soundset_subtype, anim, female_anim, enlarge, female_enlarge, NO_ACTION_INFO, NO_ACTION_INFO }

#define ENLARGED_MON(nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, col, soundset, female_soundset, soundset_subtype, enlarge, female_enlarge) \
    { nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, C(col), soundset, female_soundset, soundset_subtype, NO_ACTION_INFO, NO_ACTION_INFO, enlarge, female_enlarge, NO_ACTION_INFO, NO_ACTION_INFO }

#define ANIMATED_MON(nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, col, soundset, female_soundset, soundset_subtype, anim, female_anim) \
    { nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, C(col), soundset, female_soundset, soundset_subtype, anim, female_anim, NO_ACTION_INFO, NO_ACTION_INFO, NO_ACTION_INFO, NO_ACTION_INFO }

#define MON(nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, col, soundset, female_soundset, soundset_subtype) \
    { nam, title, desc, femalename, commonname, sym, lvl, gen, atk, siz, stats, mr1, mr2, mc1, flg1, flg2, flg3, flg4, flg5, flg6, flg7, flg8, d, C(col), soundset, female_soundset, soundset_subtype, NO_ACTION_INFO, NO_ACTION_INFO, NO_ACTION_INFO, NO_ACTION_INFO, NO_ACTION_INFO, NO_ACTION_INFO }

#define STATS(str, dex, con, intl, wis, cha) str, dex, con, intl, wis, cha
#define LVL(lvl, mov, ac, mc, mr, aln) lvl, mov, ac, mc, mr, aln
#define SIZ(wt, nut, snd, siz, heads, lightrange, body_material) wt, nut, snd, siz, heads, lightrange, body_material
#define ATTK(at, ad, n, d, p, adj, mlvl, range, aflags, atile) { at, ad, n, d, p, adj, mlvl, range, aflags, atile }
#define A(a1, a2, a3, a4, a5, a6, a7, a8) { a1, a2, a3, a4, a5, a6, a7, a8 }

struct permonst mons[] = {
    MON(""fixture newt"", None, ""small lizard"", None, None, S_LIZARD, LVL(1, 6, 8, 0, 0, 0), (G_GENO | 5),
        A(ATTK(AT_BITE, AD_PHYS, 1, 3, 0, 0, 0, 0, 0UL, 0), NO_ATTK, NO_ATTK, NO_ATTK, NO_ATTK,
          NO_ATTK, NO_ATTK, NO_ATTK),
        SIZ(10, 20, MS_SILENT, MZ_TINY, 1, 0, MAT_FLESH), STATS(3, 10, 10, 1, 1, 1), MR_NONE, MR2_NONE, MC_NONE,
        M1_ANIMAL | M1_NOHANDS, M2_HOSTILE, M3_NONE, M4_NONE, M5_NONE, M6_NONE, M7_NONE, M8_NONE,
        11, HI_DOMESTIC, MONSTER_SOUNDSET_NONE, MONSTER_SOUNDSET_NONE, NO_SOUNDSET_SUBTYPE),
    ANIMATED_MON(""fixture fox"", None, ""small wild canine"", None, None, S_DOG, LVL(2, 14, 7, 0, 0, 0), (G_GENO | 1),
        A(ATTK(AT_BITE, AD_PHYS, 1, 3, 0, 0, 0, 0, 0UL, 0), NO_ATTK, NO_ATTK, NO_ATTK, NO_ATTK,
          NO_ATTK, NO_ATTK, NO_ATTK),
        SIZ(300, 250, MS_BARK, MZ_SMALL, 1, 0, MAT_FLESH), STATS(6, 14, 12, 2, 2, 2), MR_NONE, MR2_NONE, MC_NONE,
        M1_ANIMAL | M1_NOHANDS, M2_HOSTILE, M3_INFRAVISIBLE, M4_NONE, M5_NONE, M6_NONE, M7_NONE, M8_NONE,
        12, HI_DOMESTIC, MONSTER_SOUNDSET_DOG, MONSTER_SOUNDSET_DOG, NO_SOUNDSET_SUBTYPE,
        ACTION_INFO(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, FOX_ANIMATION, 0, 0, 0),
        NO_ACTION_INFO),
    ENLARGED_MON(""fixture jackal"", None, ""wild canine"", None, None, S_DOG, LVL(3, 12, 7, 0, 0, 0), (G_GENO | G_SGROUP | 3),
        A(ATTK(AT_BITE, AD_PHYS, 1, 2, 0, 0, 0, 0, 0UL, 0), NO_ATTK, NO_ATTK, NO_ATTK, NO_ATTK,
          NO_ATTK, NO_ATTK, NO_ATTK),
        SIZ(300, 250, MS_BARK, MZ_SMALL, 1, 0, MAT_FLESH), STATS(7, 13, 12, 2, 2, 2), MR_NONE, MR2_NONE, MC_NONE,
        M1_ANIMAL | M1_NOHANDS, M2_HOSTILE, M3_INFRAVISIBLE, M4_NONE, M5_NONE, M6_NONE, M7_NONE, M8_NONE,
        13, HI_DOMESTIC, MONSTER_SOUNDSET_DOG, MONSTER_SOUNDSET_DOG, NO_SOUNDSET_SUBTYPE,
        ACTION_INFO(JACKAL_ENLARGEMENT, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        NO_ACTION_INFO),
    ENLARGED_ANIMATED_MON(""fixture hell hound"", None, ""infernal canine"", None, None, S_DOG, LVL(4, 14, 2, 0, 20, 0), (G_HELL | G_GENO | 1),
        A(ATTK(AT_BITE, AD_PHYS, 3, 6, 0, 0, 0, 0, 0UL, 0), NO_ATTK, NO_ATTK, NO_ATTK, NO_ATTK,
          NO_ATTK, NO_ATTK, NO_ATTK),
        SIZ(600, 300, MS_BARK, MZ_MEDIUM, 1, 0, MAT_FLESH), STATS(16, 15, 16, 3, 3, 3), MR_FIRE, MR2_NONE, MC_FIRE,
        M1_ANIMAL | M1_NOHANDS, M2_HOSTILE, M3_INFRAVISIBLE, M4_NONE, M5_NONE, M6_NONE, M7_NONE, M8_NONE,
        14, HI_DOMESTIC, MONSTER_SOUNDSET_DOG, MONSTER_SOUNDSET_DOG, NO_SOUNDSET_SUBTYPE,
        ACTION_INFO(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, HELL_HOUND_ANIMATION, 0, 0, 0),
        NO_ACTION_INFO,
        ACTION_INFO(HELL_HOUND_ENLARGEMENT, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        NO_ACTION_INFO),
    GENERAL_MON(""fixture little dog"", None, ""small domestic canine"", None, None, S_DOG, LVL(5, 18, 6, 0, 0, 0), (G_GENO | 1),
        A(ATTK(AT_BITE, AD_PHYS, 1, 6, 0, 0, 0, 0, 0UL, 0), NO_ATTK, NO_ATTK, NO_ATTK, NO_ATTK,
          NO_ATTK, NO_ATTK, NO_ATTK),
        SIZ(150, 150, MS_BARK, MZ_SMALL, 1, 0, MAT_FLESH), STATS(8, 14, 18, 2, 2, 2), MR_NONE, MR2_NONE, MC_NONE,
        M1_ANIMAL | M1_NOHANDS | M1_CARNIVORE, M2_DOMESTIC, M3_INFRAVISIBLE, M4_SMELLS_BURIED_SEARCHABLE, M5_HALF_SIZED_MONSTER_TILE, M6_HATCHLING | M6_USES_DOG_SUBTYPES, M7_NONE, M8_NONE,
        15, HI_DOMESTIC, MONSTER_SOUNDSET_DOG, MONSTER_SOUNDSET_DOG, NO_SOUNDSET_SUBTYPE,
        NO_ACTION_INFO,
        NO_ACTION_INFO,
        NO_ACTION_INFO,
        NO_ACTION_INFO,
        ACTION_INFO(LITTLE_DOG_REPLACEMENT, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, LITTLE_DOG_STATUE_REPLACEMENT, 0, 0),
        ACTION_INFO(LITTLE_DOG_REPLACEMENT, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, LITTLE_DOG_STATUE_REPLACEMENT, 0, 0)),
};
";
}
