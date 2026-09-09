namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Xunit;

/// <summary>
/// Two layers, deliberately. The synthetic-fixture tests carry the mechanics the resolver could
/// silently get wrong — the two-pass OBJECT definition, a wrapper macro injecting its own
/// constants, the 10 - ac inversion, BITS's argument reordering, the ternary that needs
/// objclass.h's enum values, a name several object classes share — and run everywhere. The
/// on-disk tests then assert the values a reader would look up for real items, against the
/// GnollHack working copy, and skip with a reason when that repository is not present rather
/// than passing quietly.
/// </summary>
public class ObjectsMacroResolverTests
{
    private static ObjectsMacroResolver NewResolver() => new(new GameDataParser());

    /* ==================================================================================
     * A synthetic objects.c, shaped exactly like the real one: two compilation passes over
     * one array, OBJ and the pass-1 OBJECT in the first, BITS and the pass-2 OBJECT in the
     * second. The wrapper chain below it mirrors DRGN_ARMR -> ARMOR -> GENERAL_ARMOR.
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

    /// <summary>The dragon-hide suit entry, in the shape objects.c writes it.</summary>
    private static string[] DragonSuitEntry(string name) => new[]
    {
        $"TEST_DRGN_ARMR(\"{name}\",",
        "    1, COLD_RESISTANCE, REFLECTING, NO_POWER, P1_NONE,",
        "    6000, 1, 4, 0, 0, 0, 0, 5, DRAGON_SILVER, OBJECT_SOUNDSET_GENERIC, ",
        "    O1_NONE, O2_DRAGON_ITEM | O2_MONSTER_SCALE_MAIL, O3_NONE, O4_NONE, O5_NONE, O6_NONE, PERMITTED_ALL),"
    };

    private static long AsLong(object value) => value switch
    {
        int i => i,
        long l => l,
        _ => throw new InvalidOperationException($"Expected a number, got {value?.GetType().Name}: {value}")
    };

    // --- The armour chain, end to end ------------------------------------------------

    [Fact]
    public void Resolve_ExpandsAWrapperChainAndInvertsTheStoredArmourClass()
    {
        var resolver = NewResolver();
        resolver.Load(FixtureObjectsC(DragonSuitEntry("test dragon scale mail")));

        var resolution = resolver.Resolve("test dragon scale mail");

        Assert.True(resolution.Success, resolution.FailureReason);
        Assert.Equal("ARMOR_CLASS", resolution.ObjectClass);
        Assert.Equal("TEST_DRGN_ARMR", resolution.MacroName);

        // GENERAL_ARMOR stores 10 - ac, so an ac argument of 1 becomes a stored bonus of 9. Both
        // are reported, because the number alone cannot say which unit it is in.
        Assert.Equal(9L, AsLong(resolution.Fields["ac_bonus"]));
        Assert.Equal(1L, AsLong(resolution.Fields["base_ac"]));
        Assert.Equal(4L, AsLong(resolution.Fields["magic_cancellation"]));
        Assert.Equal(5L, AsLong(resolution.Fields["spell_casting_penalty"]));
        Assert.Equal(150L, AsLong(resolution.Fields["spell_casting_penalty_percent"]));

        // Constants the wrapper injects arrive through the same substitution as the call's own
        // arguments, which is the whole point of expanding rather than pattern-matching.
        Assert.Equal(5L, AsLong(resolution.Fields["delay"]));
        Assert.Equal(550L, AsLong(resolution.Fields["weight"]));
        Assert.Equal("MAT_DRAGON_HIDE", resolution.Fields["material"]);
        Assert.Equal("ARM_SUIT", resolution.Fields["armor_category"]);

        // BITS reorders its arguments and discards ctnr; oc_uses_known comes from the literal 1
        // GENERAL_ARMOR passes, not from the call site.
        Assert.Equal(1L, AsLong(resolution.Fields["uses_known"]));
        Assert.Equal(1L, AsLong(resolution.Fields["magic"]));
    }

    /// <summary>
    /// A flag union becomes a list, which is the shape get_monster_stats already returns for its
    /// own flag fields. One string holding <c>"A | B"</c> reads as a single opaque value.
    /// </summary>
    [Fact]
    public void Resolve_SplitsFlagUnionsIntoLists()
    {
        var resolver = NewResolver();
        resolver.Load(FixtureObjectsC(DragonSuitEntry("test dragon scale mail")));

        var resolution = resolver.Resolve("test dragon scale mail");

        var objectFlags2 = Assert.IsType<List<string>>(resolution.Fields["object_flags2"]);
        Assert.Equal(new[] { "O2_DRAGON_ITEM", "O2_MONSTER_SCALE_MAIL" }, objectFlags2);

        // The wrapper ORs two of its own flags onto the call site's flags4, so the union is
        // three tokens even though the call passed one.
        var objectFlags4 = Assert.IsType<List<string>>(resolution.Fields["object_flags4"]);
        Assert.Contains("O4_NON_MYTHIC", objectFlags4);
        Assert.Contains("O4_CAN_HAVE_EXCEPTIONALITY", objectFlags4);

        // A single token stays a single token rather than becoming a one-element list.
        Assert.Equal("O1_NONE", resolution.Fields["object_flags"]);
    }

    /// <summary>
    /// The AC, MC and spellcasting conventions travel with the values. Without them a reader has
    /// no way to tell the stored bonus from the source's own argument, or to know that the stored
    /// bonus is negated into the hero's AC — which is how a correct answer gets marked wrong.
    /// </summary>
    [Fact]
    public void Resolve_StatesTheArmourConventionsAsData()
    {
        var resolver = NewResolver();
        resolver.Load(FixtureObjectsC(DragonSuitEntry("test dragon scale mail")));

        var resolution = resolver.Resolve("test dragon scale mail");
        string notes = string.Join("\n", resolution.Notes);

        Assert.Contains("10 - ac", notes);
        Assert.Contains("negates it into the hero's AC", notes);
        Assert.Contains("ARM_MC_BONUS", notes);
        Assert.Contains("ARMOR_SPELL_CASTING_PENALTY_MULTIPLIER", notes);

        // The notes reach the model through Fields, not only through the Notes property.
        var mirrored = Assert.IsType<List<string>>(resolution.Fields["notes"]);
        Assert.Equal(resolution.Notes.Count, mirrored.Count);
    }

    /// <summary>
    /// MATINIT_BASE_MATERIAL is the one init type under which the reported material is the
    /// material a player sees, so only the other ones earn the caveat.
    /// </summary>
    [Fact]
    public void Resolve_CaveatsTheMaterialOnlyWhenItIsNotTheBaseMaterial()
    {
        var resolver = NewResolver();
        resolver.Load(FixtureObjectsC(DragonSuitEntry("test dragon scale mail")));

        var baseMaterial = resolver.Resolve("test dragon scale mail");
        Assert.Equal("MATINIT_BASE_MATERIAL", baseMaterial.Fields["material_init_type"]);
        Assert.DoesNotContain("material is the base material only", string.Join("\n", baseMaterial.Notes));

        var randomised = NewResolver();
        var entry = DragonSuitEntry("test dragon scale mail");
        var lines = FixtureObjectsC(entry)
            .Select(l => l.Replace("MATINIT_BASE_MATERIAL, MAT_DRAGON_HIDE", "MATINIT_RANDOM_MATERIAL, MAT_DRAGON_HIDE"))
            .ToArray();
        randomised.Load(lines);

        var resolution = randomised.Resolve("test dragon scale mail");
        Assert.True(resolution.Success, resolution.FailureReason);
        Assert.Contains("material is the base material only", string.Join("\n", resolution.Notes));
        Assert.Contains("material_wishing_definitions[]", string.Join("\n", resolution.Notes));
    }

    /// <summary>
    /// The last three struct objdescr members were silently discarded, so an entry that names a
    /// replacement object lost it. They are emitted when set and omitted at their NO_* defaults.
    /// </summary>
    [Fact]
    public void Resolve_EmitsTheDescriptorMembersThatWereDiscarded()
    {
        var withDefaults = NewResolver();
        withDefaults.Load(FixtureObjectsC(DragonSuitEntry("test dragon scale mail")));
        var defaulted = withDefaults.Resolve("test dragon scale mail");

        Assert.False(defaulted.Fields.ContainsKey("stand_animation"));
        Assert.False(defaulted.Fields.ContainsKey("enlargement"));
        Assert.False(defaulted.Fields.ContainsKey("replacement"));

        // GENERAL_ARMOR takes the three as parameters, so an entry can set them.
        var entry = new[]
        {
            "GENERAL_ARMOR(\"test plate\", None, 1, 0, 1, NO_POWER, NO_POWER, NO_POWER, P1_NONE, ENCHTYPE_GENERAL_ARMOR, 44, 5, 450,",
            "    600, 3, 2, 0, 0, 0, 0, 0, ARM_SUIT, P_NONE, MATINIT_BASE_MATERIAL, MAT_IRON, CLR_GRAY, 0, OBJECT_SOUNDSET_GENERIC,",
            "    O1_NONE, O2_NONE, O3_NONE, O4_NONE, O5_NONE, O6_NONE, PERMITTED_ALL, OD_NONE, TEST_PLATE_ANIMATION, TEST_PLATE_ENLARGEMENT, TEST_PLATE_REPLACEMENT),"
        };

        var resolver = NewResolver();
        resolver.Load(FixtureObjectsC(entry));
        var resolution = resolver.Resolve("test plate");

        Assert.True(resolution.Success, resolution.FailureReason);
        Assert.Equal("TEST_PLATE_ANIMATION", resolution.Fields["stand_animation"]);
        Assert.Equal("TEST_PLATE_ENLARGEMENT", resolution.Fields["enlargement"]);
        Assert.Equal("TEST_PLATE_REPLACEMENT", resolution.Fields["replacement"]);
    }

    // --- Names shared by several object classes ---------------------------------------

    [Fact]
    public void Resolve_NamesTheOtherObjectClassesThatShareAnItemName()
    {
        var scrollDefines = new[]
        {
            "#define TEST_SCROLL(name,desc,prob,cost,power,mgc,color)  \\",
            "    OBJECT(OBJ(name, desc, None, None, None, 0, OD_NONE, NO_ANIMATION, NO_ENLARGEMENT, NO_REPLACEMENT),  \\",
            "        BITS(0, 1, 0, 0, mgc, ENCHTYPE_NONE, CHARGED_NOT_CHARGED, RECHARGING_NOT_RECHARGEABLE, 0, 0, 0, 0, 0, 0, P_NONE, MATINIT_BASE_MATERIAL, MAT_PAPER),  \\",
            "        power, NO_POWER, NO_POWER, P1_NONE, SCROLL_CLASS, prob, MULTIGEN_SINGLE, 0, 5, cost,  \\",
            "        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, A1_NONE, A2_NONE, 0,  \\",
            "        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  \\",
            "        0, color, OBJECT_SOUNDSET_GENERIC,  \\",
            "        0, 0, 0, 0,  \\",
            "        PERMITTED_ALL, ALL_TARGETS, O1_NONE, O2_NONE, O3_NONE, O4_NONE, O5_NONE, O6_NONE)",
            "#define TEST_WAND(name,desc,prob,cost,power,mgc,color)  \\",
            "    OBJECT(OBJ(name, desc, None, None, None, 0, OD_NONE, NO_ANIMATION, NO_ENLARGEMENT, NO_REPLACEMENT),  \\",
            "        BITS(0, 0, 0, 0, mgc, ENCHTYPE_NONE, CHARGED_CHARGED, RECHARGING_RECHARGEABLE, 0, 0, 0, 0, 0, 0, P_NONE, MATINIT_BASE_MATERIAL, MAT_WOOD),  \\",
            "        power, NO_POWER, NO_POWER, P1_NONE, WAND_CLASS, prob, MULTIGEN_SINGLE, 0, 7, cost,  \\",
            "        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, A1_NONE, A2_NONE, 0,  \\",
            "        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  \\",
            "        0, color, OBJECT_SOUNDSET_GENERIC,  \\",
            "        0, 0, 0, 0,  \\",
            "        PERMITTED_ALL, ALL_TARGETS, O1_NONE, O2_NONE, O3_NONE, O4_NONE, O5_NONE, O6_NONE)",
            "TEST_SCROLL(\"shared name\", \"unlabeled\", 180, 20, NO_POWER, 0, CLR_WHITE),",
            "TEST_WAND(\"shared name\", \"glass\", 55, 150, NO_POWER, 1, HI_METAL),"
        };

        var resolver = NewResolver();
        resolver.Load(FixtureObjectsC(scrollDefines));

        Assert.Equal(new[] { "SCROLL_CLASS", "WAND_CLASS" }, resolver.ObjectClassesOf("shared name"));

        // The name appears once in the name list, and the classes that hold it are reachable.
        Assert.Single(resolver.ItemNames, n => n == "shared name");

        // Unqualified: the first entry in file order, and the answer says so.
        var unqualified = resolver.Resolve("shared name");
        Assert.True(unqualified.Success, unqualified.FailureReason);
        Assert.Equal("SCROLL_CLASS", unqualified.ObjectClass);
        string notes = string.Join("\n", unqualified.Notes);
        Assert.Contains("names 2 entries", notes);
        Assert.Contains("WAND_CLASS", notes);
        Assert.Equal(
            new[] { "SCROLL_CLASS", "WAND_CLASS" },
            Assert.IsType<List<string>>(unqualified.Fields["ambiguous_object_classes"]));

        // Qualified: the other one, with no ambiguity note, because nothing was chosen for the
        // caller.
        var qualified = resolver.Resolve("shared name", "WAND_CLASS");
        Assert.True(qualified.Success, qualified.FailureReason);
        Assert.Equal("WAND_CLASS", qualified.ObjectClass);
        Assert.DoesNotContain("names 2 entries", string.Join("\n", qualified.Notes));
    }

    [Fact]
    public void Resolve_ReportsAnObjectClassThatDoesNotHoldTheName()
    {
        var resolver = NewResolver();
        resolver.Load(FixtureObjectsC(DragonSuitEntry("test dragon scale mail")));

        var resolution = resolver.Resolve("test dragon scale mail", "POTION_CLASS");

        Assert.False(resolution.Success);
        Assert.Contains("not in object class 'POTION_CLASS'", resolution.FailureReason);
        Assert.Contains("ARMOR_CLASS", resolution.FailureReason);
    }

    // --- Failure, never an exception --------------------------------------------------

    [Fact]
    public void Resolve_ReturnsAFailureReasonRatherThanThrowing()
    {
        var resolver = NewResolver();

        // Not indexed at all.
        var unloaded = resolver.Resolve("anything");
        Assert.False(unloaded.Success);
        Assert.Contains("has not been indexed", unloaded.FailureReason);

        resolver.Load(FixtureObjectsC(DragonSuitEntry("test dragon scale mail")));

        var missing = resolver.Resolve("no such item");
        Assert.False(missing.Success);
        Assert.NotNull(missing.FailureReason);
        Assert.Contains("no item entry named 'no such item'", missing.FailureReason);

        var empty = resolver.Resolve("   ");
        Assert.False(empty.Success);
        Assert.NotNull(empty.FailureReason);

        // The two array sentinels are well-formed OBJECT calls and must not be item entries.
        Assert.False(resolver.Resolve("strange object").Success);
        Assert.DoesNotContain("strange object", resolver.ItemNames);
    }

    [Fact]
    public void Load_RejectsNullSourceWithoutThrowingFromResolve()
    {
        var resolver = NewResolver();

        Assert.Throws<ArgumentNullException>(() => resolver.Load(null!));

        var resolution = resolver.Resolve(null!, null, "anything");
        Assert.False(resolution.Success);
        Assert.Contains("was not supplied", resolution.FailureReason);
    }

    /* ==================================================================================
     * On-disk spot-checks against the GnollHack working copy.
     * =============================================================================== */

    /// <summary>
    /// The GnollHack source root, from the same <c>SourceCodePath</c> key the application reads,
    /// falling back to the conventional sibling checkout. Null when neither is present.
    /// </summary>
    private static string? ResolveGnollHackRoot()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<ObjectsMacroResolverTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var candidates = new[]
        {
            config["SourceCodePath"],
            Path.Combine(Path.GetDirectoryName(Directory.GetCurrentDirectory()) ?? ".", "GnollHack"),
            @"C:\hmp\GnollHack"
        };

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .FirstOrDefault(c => File.Exists(Path.Combine(c!, "src", "objects.c")));
    }

    /// <summary>
    /// Loads the real objects.c and objclass.h, or skips with the reason. A silent pass would
    /// make a broken resolver look tested.
    /// </summary>
    private static ObjectsMacroResolver LoadRealCorpusOrSkip()
    {
        string? root = ResolveGnollHackRoot();
        if (root == null)
        {
            Assert.Skip(
                "The GnollHack working copy was not found. Set the SourceCodePath user secret "
                + "for Overseer.Tests, or check GnollHack out beside this repository, to run the "
                + "on-disk item spot-checks.");
        }

        var resolver = NewResolver();
        resolver.Load(
            File.ReadAllLines(Path.Combine(root!, "src", "objects.c")),
            File.ReadAllLines(Path.Combine(root!, "include", "objclass.h")));
        return resolver;
    }

    [Fact]
    public void RealCorpus_SilverDragonScaleMail()
    {
        var resolver = LoadRealCorpusOrSkip();
        var r = resolver.Resolve("silver dragon scale mail");

        Assert.True(r.Success, r.FailureReason);
        Assert.Equal("ARMOR_CLASS", r.ObjectClass);
        Assert.Equal(1L, AsLong(r.Fields["base_ac"]));
        Assert.Equal(9L, AsLong(r.Fields["ac_bonus"]));
        Assert.Equal(4L, AsLong(r.Fields["magic_cancellation"]));
        Assert.Equal(5L, AsLong(r.Fields["spell_casting_penalty"]));
        Assert.Equal(150L, AsLong(r.Fields["spell_casting_penalty_percent"]));
        Assert.Equal(5L, AsLong(r.Fields["delay"]));
        Assert.Equal(550L, AsLong(r.Fields["weight"]));
        Assert.Equal("MAT_DRAGON_HIDE", r.Fields["material"]);
        Assert.Equal("ARM_SUIT", r.Fields["armor_category"]);
        Assert.Contains("O2_DRAGON_ITEM", Assert.IsType<List<string>>(r.Fields["object_flags2"]));

        // Armour carries no damage triples; reporting zeroes there would name a mechanic the
        // entry does not have.
        Assert.False(r.Fields.ContainsKey("small_damage"));
        Assert.False(r.Fields.ContainsKey("large_damage"));
    }

    [Fact]
    public void RealCorpus_LongSword()
    {
        var resolver = LoadRealCorpusOrSkip();
        var r = resolver.Resolve("long sword");

        Assert.True(r.Success, r.FailureReason);
        Assert.Equal("WEAPON_CLASS", r.ObjectClass);
        Assert.True(r.Fields.ContainsKey("weight"));
        Assert.True(AsLong(r.Fields["weight"]) > 0);
        Assert.True(r.Fields.ContainsKey("skill"));
    }

    [Fact]
    public void RealCorpus_ElvenMithrilCoat()
    {
        var resolver = LoadRealCorpusOrSkip();

        // Hyphenated in the source. The unhyphenated spelling is not an entry, and the resolver
        // must say so rather than resolving something nearby.
        var r = resolver.Resolve("elven mithril-coat");
        Assert.True(r.Success, r.FailureReason);
        Assert.Equal("ARMOR_CLASS", r.ObjectClass);
        Assert.True(r.Fields.ContainsKey("ac_bonus"));
        Assert.True(r.Fields.ContainsKey("base_ac"));

        Assert.False(resolver.Resolve("elven mithril coat").Success);
    }

    [Fact]
    public void RealCorpus_ScrollAndWandUseTheBareOcName()
    {
        var resolver = LoadRealCorpusOrSkip();

        // The lookup key is the bare oc_name, not the display name.
        var identify = resolver.Resolve("identify");
        Assert.True(identify.Success, identify.FailureReason);
        Assert.False(resolver.Resolve("scroll of identify").Success);

        var digging = resolver.Resolve("digging");
        Assert.True(digging.Success, digging.FailureReason);
        Assert.False(resolver.Resolve("wand of digging").Success);
    }

    [Fact]
    public void RealCorpus_SharedNamesAreReachableByObjectClass()
    {
        var resolver = LoadRealCorpusOrSkip();

        // "identify" is a scroll, a wand and a spellbook; an unqualified resolve returns one of
        // them and must say which, and each is reachable by its class.
        var classes = resolver.ObjectClassesOf("identify");
        Assert.True(classes.Count > 1, $"Expected 'identify' in several object classes, got: {string.Join(", ", classes)}");

        var unqualified = resolver.Resolve("identify");
        Assert.Contains("names " + classes.Count + " entries", string.Join("\n", unqualified.Notes));

        foreach (string objectClass in classes)
        {
            var r = resolver.Resolve("identify", objectClass);
            Assert.True(r.Success, r.FailureReason);
            Assert.Equal(objectClass, r.ObjectClass);
        }
    }

    /// <summary>
    /// Every entry in the file resolves. A macro family the resolver cannot expand would
    /// otherwise show up only as one item's raw dump in production.
    /// </summary>
    [Fact]
    public void RealCorpus_KeepsEveryEntryOfAName()
    {
        var resolver = LoadRealCorpusOrSkip();

        int names = resolver.ItemNames.Count;
        int entries = resolver.ItemNames.Sum(n => resolver.ObjectClassesOf(n).Count);
        int shared = resolver.ItemNames.Count(n => resolver.ObjectClassesOf(n).Count > 1);

        // A name-keyed, first-wins index loses one entry per extra class sharing a name. The
        // exact figures move with the game source; what must hold is that the entries outnumber
        // the names by the number of extra classes, and that none of them was dropped.
        Assert.True(shared > 0, "Expected objects.c to hold at least one name in several object classes.");
        Assert.True(entries > names,
            $"Expected more entries than distinct names, got {entries} entries for {names} names.");
        Assert.Equal(entries - names, resolver.ItemNames.Sum(n => resolver.ObjectClassesOf(n).Count - 1));
    }

    [Fact]
    public void RealCorpus_EveryItemEntryResolves()
    {
        var resolver = LoadRealCorpusOrSkip();

        var failures = new List<string>();
        foreach (string name in resolver.ItemNames)
        {
            var r = resolver.Resolve(name);
            if (!r.Success) failures.Add($"{name}: {r.FailureReason}");
        }

        Assert.True(resolver.ItemNames.Count > 500,
            $"Expected the whole objects.c array, indexed {resolver.ItemNames.Count} names.");
        Assert.Empty(failures);
    }
}
