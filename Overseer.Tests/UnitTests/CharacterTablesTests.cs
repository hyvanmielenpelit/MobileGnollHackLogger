namespace Overseer.Tests.UnitTests;

using System;
using System.IO;
using System.Linq;
using Overseer.Services;
using Xunit;

/// <summary>
/// <see cref="CharacterTables"/> over a synthetic src/role.c, src/attrib.c and src/prop.c that
/// carry the traps of the real files (a skill table inside each role, an <c>#if 0</c> block inside
/// a rank table, a commented-out ability table and mapping, a stale <c>#if 0</c> definition), and
/// over the real GnollHack files when the clone is present on this machine.
/// </summary>
public class CharacterTablesTests
{
    internal const string SyntheticRoleC =
        "#include \"hack.h\"\n" +
        "#define SKILL_PROGRESS_NONE {0, 0, 0, 0, 0, 0, 0}\n" +
        "\n" +
        "const struct Role roles[] = {\n" +
        "    { { \"Healer\", 0 },\n" +
        "      { { \"Rhizotomist\", 0 },\n" +
        "        { \"Empiric\", 0 } },\n" +
        "      \"_Athena\", \"Hermes\", \"Poseidon\", /* Greek */\n" +
        "      \"Hea\",\n" +
        "      \"the Temple of Epidaurus\",\n" +
        "      \"the Temple of Coeus\",\n" +
        "      ROLE_HEALER,\n" +
        "      \"Healer, with commas, in a description\",\n" +
        "      { \"Heals, and \\\"cures\\\"\", \"\", \"\", \"\", \"\" },\n" +
        "      PM_HEALER,\n" +
        "      NON_PM,\n" +
        "      PM_HIPPOCRATES,\n" +
        "      PM_ATTENDANT,\n" +
        "      PM_CYCLOPS,\n" +
        "      PM_GIANT_RAT,\n" +
        "      PM_SNAKE,\n" +
        "      S_RODENT,\n" +
        "      S_YETI,\n" +
        "      ART_STAFF_OF_AESCULAPIUS,\n" +
        "      MH_HUMAN | MH_GNOLL | ROLE_MALE | ROLE_FEMALE\n" +
        "          | ROLE_NEUTRAL,\n" +
        "      /* Str Int Wis Dex Con Cha */\n" +
        "      { 7, 7, 13, 7, 11, 16 },\n" +
        "      { 15, 20, 20, 15, 25, 5 },\n" +
        "      { 11, 0, 0, 8, 1, 0 }, /* Hit points */\n" +
        "      { 1, 4, 0, 1, 0, 2 }, /* Energy */\n" +
        "      20,\n" +
        "      10,\n" +
        "      3,\n" +
        "        {\n" +
        "            SKILL_PROGRESS_NONE, /* P_NONE */\n" +
        "            SKILL_PROGRESS_NONE  /* P_DAGGER, it's a dagger */\n" +
        "        }\n" +
        "    },\n" +
        "    { { \"Caveman\", \"Cavewoman\" },\n" +
        "      {\n" +
        "#if 0 /* OBSOLETE */\n" +
        "        {\"Don't\", \"parse\"},   /* ['a] guess\n" +
        "        \"Solonor\", \"Lolth\",\n" +
        "#endif\n" +
        "        { \"Troglodyte\", 0 } },\n" +
        "      \"Anu\", \"_Ishtar\", \"Anshar\",\n" +
        "      \"Cav\", \"the Caves of the Ancestors\", \"the Dragon's Lair\",\n" +
        "      ROLE_CAVEMAN, \"Primitive\", { \"\", \"\", \"\", \"\", \"\" },\n" +
        "      PM_CAVEMAN, PM_LITTLE_DOG, PM_SHAMAN_KARNOV, PM_NEANDERTHAL, PM_CHROMATIC_DRAGON,\n" +
        "      PM_BUGBEAR, PM_HILL_GIANT, S_HUMANOID, S_GIANT,\n" +
        "      ART_SCEPTRE_OF_MIGHT,\n" +
        "      MH_HUMAN | MH_DWARF | MH_GNOLL | ROLE_MALE | ROLE_FEMALE | ROLE_LAWFUL | ROLE_NEUTRAL,\n" +
        "      { 10, 7, 7, 7, 8, 6 }, { 30, 6, 7, 20, 30, 7 }, { 14, 0, 0, 8, 2, 0 }, { 1, 2, 0, 2, 0, 2 },\n" +
        "      10, 0, 12,\n" +
        "      { SKILL_PROGRESS_NONE }\n" +
        "    },\n" +
        "    /* Array terminator */\n" +
        "    { { 0, 0 } }\n" +
        "};\n" +
        "\n" +
        "struct Role urole = {\n" +
        "    { \"Undefined\", 0 },\n" +
        "};\n" +
        "\n" +
        "/* Table of all races */\n" +
        "const struct Race races[] = {\n" +
        "    {\n" +
        "        \"human\",\n" +
        "        \"human\",\n" +
        "        \"humanity\",\n" +
        "        \"Hum\",\n" +
        "        { \"man\", \"woman\" },\n" +
        "        0,\n" +
        "        \"Versatile but lacks traits\",\n" +
        "        { \"\", \"\", \"\", \"\", \"\"},\n" +
        "        PM_HUMAN,\n" +
        "        PM_HUMAN_MUMMY,\n" +
        "        PM_HUMAN_ZOMBIE,\n" +
        "        MH_HUMAN | ROLE_MALE | ROLE_FEMALE | ROLE_LAWFUL | ROLE_NEUTRAL | ROLE_CHAOTIC,\n" +
        "        MH_HUMAN,\n" +
        "        0,\n" +
        "        MH_GNOLL,\n" +
        "        { 3, 3, 3, 3, 3, 3 },\n" +
        "        { STR18(100), 18, 18, 18, 18, 18 },\n" +
        "        { 12, 0, 1, 2, 1, 0 }, /* Hit points */\n" +
        "        { 2, 3, 0, 2, 0, 2 }  /* Energy */\n" +
        "    },\n" +
        "    {\n" +
        "        \"dwarf\", \"dwarven\", \"dwarvenkind\", \"Dwa\", { 0, 0 }, 1, \"Sturdy\",\n" +
        "        { \"Increased nutrition from cram ration\", \"\", \"\", \"\", \"\"},\n" +
        "        PM_DWARF, PM_DWARF_MUMMY, PM_DWARF_ZOMBIE,\n" +
        "        MH_DWARF | ROLE_MALE | ROLE_FEMALE | ROLE_LAWFUL,\n" +
        "        MH_DWARF, MH_DWARF, M2_GNOLL,\n" +
        "        { 3, 3, 3, 3, 3, 3 }, { STR18(100), 16, 16, 20, 20, 16 }, { 17, 0, 1, 3, 2, 0 }, { 0, 2, -1, 2, -1, 2 }\n" +
        "    },\n" +
        "    {\n" +
        "        \"gnoll\", \"gnollish\", \"gnollkind\", \"Gnl\", { 0, 0 }, NUM_RACES - 1, \"Keen smell\",\n" +
        "        { \"Smells rotten food\", \"Recognizes corpse properties\", \"\", \"\", \"\" },\n" +
        "        PM_GNOLL, PM_GNOLL_MUMMY, PM_GNOLL_ZOMBIE,\n" +
        "        MH_GNOLL | ROLE_MALE | ROLE_FEMALE | ROLE_NEUTRAL | ROLE_CHAOTIC,\n" +
        "        MH_GNOLL, MH_GNOLL, MH_HUMAN | MH_DWARF,\n" +
        "        { 3, 3, 3, 3, 3, 3 }, { STR18(100), 16, 16, 19, 19, 16 }, { 13, 0, 1, 3, 1, 0 }, { 1, 3, 1, 0, 1, 0 }\n" +
        "    },\n" +
        "    /* Array terminator */\n" +
        "    { 0, 0, 0, 0 }\n" +
        "};\n";

    internal const string SyntheticAttribC =
        "static const struct innate\n" +
        "  hea_abil[] = { { 1, POISON_RESISTANCE, \"\", \"\" },\n" +
        "                 { 1, CORPSE_PROPERTY_APPRAISAL, \"sensitive to corpses\", \"\" },\n" +
        "                 { 0, 0, 0, 0 } },\n" +
        "\n" +
        "  cav_abil[] = { { 10, CORPSE_PROPERTY_APPRAISAL, \"sensitive to corpses\", \"\" },\n" +
        "                 { 7, FAST, \"quick\", \"slow\" },\n" +
        "                 { 0, 0, 0, 0 } },\n" +
        "\n" +
        "  dwa_abil[] = { { 1, INFRAVISION, \"\", \"\" },\n" +
        "                 { 0, 0, 0, 0 } },\n" +
        "/* Gnome has been removed -- JG\n" +
        "  gno_abil[] = { { 1, INFRAVISION, \"\", \"\" },\n" +
        "                 { 0, 0, 0, 0 } },\n" +
        "*/\n" +
        "  gnl_abil[] = { { 1, LYCANTHROPY_RESISTANCE, \"\", \"\" },\n" +
        "                 { 0, 0, 0, 0 } },\n" +
        "\n" +
        "  hum_abil[] = { { 0, 0, 0, 0 } };\n" +
        "\n" +
        "#if 0\n" +
        "static const struct innate *check_innate_abil(int64_t *, int64_t);\n" +
        "#endif\n" +
        "\n" +
        "#if 0\n" +
        "const struct innate *\n" +
        "race_abil(int r)\n" +
        "{\n" +
        "    switch (r) {\n" +
        "    case PM_GNOLL:\n" +
        "        rabil = gno_abil;\n" +
        "        break;\n" +
        "    }\n" +
        "}\n" +
        "#endif\n" +
        "\n" +
        "const struct innate *\n" +
        "role_abil(int r)\n" +
        "{\n" +
        "    /*\n" +
        "    const struct {\n" +
        "        short role;\n" +
        "        const struct innate *abil;\n" +
        "    } roleabils[] = {\n" +
        "        { PM_HEALER, cav_abil },\n" +
        "        { 0, 0 }\n" +
        "    };\n" +
        "    case PM_HEALER: abil = cav_abil;\n" +
        "    */\n" +
        "    const struct innate* abil = (struct innate*)0;\n" +
        "    switch (r) {\n" +
        "    case PM_HEALER:\n" +
        "        abil = hea_abil;\n" +
        "        break;\n" +
        "    case PM_CAVEMAN:\n" +
        "        abil = cav_abil;\n" +
        "        break;\n" +
        "    default:\n" +
        "        break;\n" +
        "    }\n" +
        "    return abil;\n" +
        "}\n" +
        "\n" +
        "const struct innate*\n" +
        "race_abil(int r)\n" +
        "{\n" +
        "    const struct innate* rabil = (struct innate*)0;\n" +
        "    switch (r) {\n" +
        "    case PM_HUMAN:\n" +
        "        rabil = hum_abil;\n" +
        "        break;\n" +
        "    case PM_DWARF:\n" +
        "        rabil = dwa_abil;\n" +
        "        break;\n" +
        "    case PM_GNOLL:\n" +
        "        rabil = gnl_abil;\n" +
        "        break;\n" +
        "    default:\n" +
        "        break;\n" +
        "    }\n" +
        "    return rabil;\n" +
        "}\n";

    internal const string SyntheticPropC =
        "const struct propname propertynames[] = {\n" +
        "    { POISON_RESISTANCE, \"resistant to poison\", \"poison resistance\" },\n" +
        "    { CORPSE_PROPERTY_APPRAISAL, \"recognizing corpse properties\", \"recognition of corpse properties\" },\n" +
        "    { FAST, \"fast\", \"speed\", AC_MC_DESC(1, 2) },\n" +
        "    { LYCANTHROPY_RESISTANCE, \"resistant to lycanthropy\", \"lycanthropy resistance\" },\n" +
        "    {  0, 0, 0 },\n" +
        "};\n";

    internal const string SyntheticArtilistH =
        "    A(\"\", None, None, STRANGE_OBJECT, STRANGE_OBJECT,\n" +
        "    A(\"The Staff of Aesculapius\", \"serpent-entwined quarterstaff\", None, STAFF_OF_LIFE, QUARTERSTAFF,\n";

    internal static string[] Lines(string text) => text.Split('\n');

    private static CharacterTables Synthetic()
        => CharacterTables.Parse(Lines(SyntheticRoleC), Lines(SyntheticAttribC), Lines(SyntheticPropC), Lines(SyntheticArtilistH));

    [Fact]
    public void Synthetic_ReadsEveryRoleAndRace_AndSkipsTheTerminators()
    {
        var tables = Synthetic();

        Assert.Empty(tables.Problems);
        Assert.Equal(new[] { "Healer", "Caveman" }, tables.Roles.Select(r => r.Name));
        Assert.Equal(new[] { "human", "dwarf", "gnoll" }, tables.Races.Select(r => r.Noun));
        Assert.All(tables.Roles, r => Assert.Null(r.ParseError));
        Assert.All(tables.Races, r => Assert.Null(r.ParseError));
    }

    [Fact]
    public void Synthetic_Role_ReadsNamesTraitsQuestArtifactAndAllowMask()
    {
        var tables = Synthetic();

        var healer = tables.Roles[0];
        Assert.Null(healer.FemaleName);
        Assert.Equal(new[] { "Heals, and \"cures\"" }, healer.Traits);
        Assert.Equal("PM_HEALER", healer.MonsterConstant);
        Assert.Equal("ART_STAFF_OF_AESCULAPIUS", healer.QuestArtifactConstant);
        Assert.Equal("The Staff of Aesculapius", healer.QuestArtifactName);

        var caveman = tables.Roles[1];
        Assert.Equal("Cavewoman", caveman.FemaleName);
        Assert.Equal("ART_SCEPTRE_OF_MIGHT", caveman.QuestArtifactConstant);
        Assert.Null(caveman.QuestArtifactName);
        Assert.Equal(new[] { "lawful", "neutral" }, CharacterTables.Alignments(caveman.Allow));
    }

    [Fact]
    public void Synthetic_AllowedRolesAndRaces_ComeFromTheRoleRaceBitsAndTheSelfMasks()
    {
        var tables = Synthetic();

        Assert.Equal(new[] { "Healer", "Caveman" }, tables.RolesAllowing(tables.FindRace("human")!).Select(r => r.Name));
        Assert.Equal(new[] { "Caveman" }, tables.RolesAllowing(tables.FindRace("dwarf")!).Select(r => r.Name));
        Assert.Equal(new[] { "human", "gnoll" }, tables.RacesAllowedBy(tables.FindRole("Healer")!).Select(r => r.Noun));
        Assert.Equal(new[] { "human", "dwarf", "gnoll" }, tables.RacesAllowedBy(tables.FindRole("Caveman")!).Select(r => r.Noun));
        Assert.Equal(new[] { "neutral", "chaotic" }, CharacterTables.Alignments(tables.FindRace("gnoll")!.Allow));
    }

    [Fact]
    public void Synthetic_RaceAttitudes_DecodeTheMasksToRaceNouns_M2ReadAsMH()
    {
        var tables = Synthetic();

        var human = tables.FindRace("human")!;
        Assert.Empty(tables.RaceNouns(human.LoveMask));
        Assert.Equal(new[] { "gnoll" }, tables.RaceNouns(human.HateMask));

        var dwarf = tables.FindRace("dwarf")!;
        Assert.Equal(new[] { "dwarf" }, tables.RaceNouns(dwarf.LoveMask));
        Assert.Equal(new[] { "gnoll" }, tables.RaceNouns(dwarf.HateMask));

        var gnoll = tables.FindRace("gnoll")!;
        Assert.Equal(new[] { "gnoll" }, tables.RaceNouns(gnoll.LoveMask));
        Assert.Equal(new[] { "human", "dwarf" }, tables.RaceNouns(gnoll.HateMask));
    }

    [Fact]
    public void Synthetic_Abilities_FollowTheSwitchMappings_OrderedByLevel_NamedFromPropC()
    {
        var tables = Synthetic();

        Assert.Equal(
            new[] { (1, "resistant to poison"), (1, "recognizing corpse properties") },
            tables.FindRole("Healer")!.Abilities.Select(a => (a.Level, a.Name)));
        Assert.Equal(
            new[] { (7, "fast"), (10, "recognizing corpse properties") },
            tables.FindRole("Caveman")!.Abilities.Select(a => (a.Level, a.Name)));

        /* The commented-out gno_abil and the #if 0 race_abil map the gnoll to INFRAVISION; neither counts. */
        Assert.Equal(new[] { (1, "resistant to lycanthropy") }, tables.FindRace("gnoll")!.Abilities.Select(a => (a.Level, a.Name)));

        /* INFRAVISION has no propertynames[] row, so the constant names it. */
        Assert.Equal(new[] { (1, "INFRAVISION") }, tables.FindRace("dwarf")!.Abilities.Select(a => (a.Level, a.Name)));
        Assert.Empty(tables.FindRace("human")!.Abilities);
    }

    [Theory]
    [InlineData("gnoll", "gnoll")]
    [InlineData("gnollish", "gnoll")]
    [InlineData("GNOLLS", "gnoll")]
    [InlineData("gnollkind", "gnoll")]
    [InlineData("dwarves", "dwarf")]
    [InlineData("dwarfs", "dwarf")]
    [InlineData("Humans", "human")]
    [InlineData("woman", "human")]
    [InlineData(" elven ", null)]
    public void FindRace_MatchesNounAdjectivePluralAndIndividualForms(string query, string? expected)
    {
        Assert.Equal(expected, Synthetic().FindRace(query)?.Noun);
    }

    [Theory]
    [InlineData("Healer", "Healer")]
    [InlineData("healers", "Healer")]
    [InlineData("Cavewoman", "Caveman")]
    [InlineData("cavemen", "Caveman")]
    [InlineData("CAVEWOMEN", "Caveman")]
    [InlineData("Wizard", null)]
    public void FindRole_MatchesMaleAndFemaleNamesAndPlurals(string query, string? expected)
    {
        Assert.Equal(expected, Synthetic().FindRole(query)?.Name);
    }

    [Fact]
    public void RoleRowWithTooFewFields_KeepsItsNameAndRawDefinition_AndSaysWhy()
    {
        string roleC =
            "const struct Role roles[] = {\n" +
            "    { { \"Tourist\", 0 },\n" +
            "      { { \"Rambler\", 0 } },\n" +
            "      \"Blind Io\", \"_The Lady\", \"Offler\" },\n" +
            "    { { 0, 0 } }\n" +
            "};\n";

        var tables = CharacterTables.Parse(Lines(roleC), Lines(SyntheticAttribC), Lines(SyntheticPropC));

        var tourist = tables.FindRole("tourists");
        Assert.NotNull(tourist);
        Assert.NotNull(tourist!.ParseError);
        Assert.Contains("top-level fields", tourist.ParseError);
        Assert.Contains("\"Tourist\"", tourist.RawDefinition);
        Assert.Contains("Offler", tourist.RawDefinition);
    }

    [Fact]
    public void MissingFiles_AreListedAsProblems_AndNeverThrow()
    {
        var tables = CharacterTables.Parse(null, null, null);

        Assert.Empty(tables.Roles);
        Assert.Empty(tables.Races);
        Assert.Contains(tables.Problems, p => p.Contains("src/role.c", StringComparison.Ordinal));
        Assert.Contains(tables.Problems, p => p.Contains("src/attrib.c", StringComparison.Ordinal));
        Assert.Contains(tables.Problems, p => p.Contains("src/prop.c", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingAttribC_LeavesRolesAndRacesReadable_WithoutAbilities()
    {
        var tables = CharacterTables.Parse(Lines(SyntheticRoleC), null, Lines(SyntheticPropC));

        Assert.Equal(2, tables.Roles.Count);
        Assert.Empty(tables.FindRole("Healer")!.Abilities);
        Assert.Contains(tables.Problems, p => p.StartsWith("src/attrib.c", StringComparison.Ordinal));
    }

    /* ==================================================================================
     * The real GnollHack files.
     * =============================================================================== */

    internal const string GnollHackSourceDir = @"C:\hmp\GnollHack";

    internal static readonly string[] RealFiles =
    {
        Path.Combine("src", "role.c"),
        Path.Combine("src", "attrib.c"),
        Path.Combine("src", "prop.c"),
        Path.Combine("include", "artilist.h")
    };

    internal static bool RealSourceAvailable =>
        Array.TrueForAll(RealFiles, f => File.Exists(Path.Combine(GnollHackSourceDir, f)));

    internal const string RealSourceUnavailableReason =
        "The GnollHack clone's src/role.c, src/attrib.c, src/prop.c and include/artilist.h are not all present under " + GnollHackSourceDir + ".";

    private static CharacterTables Real()
    {
        string[] Read(int i) => File.ReadAllLines(Path.Combine(GnollHackSourceDir, RealFiles[i]));
        return CharacterTables.Parse(Read(0), Read(1), Read(2), Read(3));
    }

    [Fact]
    public void RealSource_EveryRoleAndRace_Parses()
    {
        if (!RealSourceAvailable) Assert.Skip(RealSourceUnavailableReason);

        var tables = Real();

        Assert.Empty(tables.Problems);
        Assert.Equal(13, tables.Roles.Count);
        Assert.Equal(new[] { "human", "elf", "dwarf", "gnoll", "orc" }, tables.Races.Select(r => r.Noun));
        Assert.All(tables.Roles, r => Assert.Null(r.ParseError));
        Assert.All(tables.Races, r => Assert.Null(r.ParseError));
        Assert.All(tables.Roles, r => Assert.NotNull(r.QuestArtifactName));
        Assert.Equal("The Master Key of Thievery", tables.FindRole("Rogue")!.QuestArtifactName);
    }

    [Theory]
    [InlineData("human", new string[0], new[] { "gnoll", "orc" })]
    [InlineData("orc", new string[0], new[] { "human", "elf", "dwarf" })]
    [InlineData("gnoll", new[] { "gnoll" }, new[] { "human" })]
    [InlineData("elf", new[] { "elf" }, new[] { "orc", "gnoll" })]
    [InlineData("dwarf", new[] { "dwarf" }, new[] { "orc" })]
    public void RealSource_RaceAttitudes(string race, string[] alwaysPeaceful, string[] alwaysHostile)
    {
        if (!RealSourceAvailable) Assert.Skip(RealSourceUnavailableReason);

        var tables = Real();
        var entry = tables.FindRace(race)!;

        Assert.Equal(alwaysPeaceful.OrderBy(s => s), tables.RaceNouns(entry.LoveMask).OrderBy(s => s));
        Assert.Equal(alwaysHostile.OrderBy(s => s), tables.RaceNouns(entry.HateMask).OrderBy(s => s));
    }

    [Theory]
    [InlineData("Healer", 1)]
    [InlineData("Priest", 4)]
    [InlineData("Archaeologist", 7)]
    [InlineData("Caveman", 10)]
    public void RealSource_CorpsePropertyRecognition_ByRole(string role, int level)
    {
        if (!RealSourceAvailable) Assert.Skip(RealSourceUnavailableReason);

        var abilities = Real().FindRole(role)!.Abilities;

        Assert.Contains(abilities, a => a.Level == level && a.Name == "recognizing corpse properties");
        Assert.Single(abilities, a => a.Property == "CORPSE_PROPERTY_APPRAISAL");
    }

    [Fact]
    public void RealSource_GnollAbilities_AreTheGnlTable()
    {
        if (!RealSourceAvailable) Assert.Skip(RealSourceUnavailableReason);

        var gnoll = Real().FindRace("gnollish")!;

        Assert.Equal(
            new[] { "LYCANTHROPY_RESISTANCE", "CORPSE_PROPERTY_APPRAISAL", "EDIBILITY_APPRAISAL" },
            gnoll.Abilities.Select(a => a.Property));
        Assert.All(gnoll.Abilities, a => Assert.Equal(1, a.Level));
    }
}
