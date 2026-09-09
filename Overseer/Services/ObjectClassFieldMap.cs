using System;
using System.Collections.Generic;
using System.Linq;

namespace Overseer.Services
{
    /// <summary>
    /// The two hand-authored tables that tie objects.c's OBJECT / OBJ / BITS macro slots to
    /// <c>struct objclass</c> and <c>struct objdescr</c> in include/objclass.h. Everything else
    /// about an item -- wrapper-injected constants, arithmetic, reordering -- is derived by
    /// symbolic expansion in <see cref="ObjectsMacroResolver"/>.
    ///
    /// Table 1 is the slot order (<see cref="ObjectSlotFields"/>, <see cref="BitsFields"/>,
    /// <see cref="ObjDescriptorFields"/>). Table 2 is the per-object-class meaning of the eight
    /// general purpose <c>oc_oc1</c>..<c>oc_oc8</c> slots and of the other overloaded fields.
    /// </summary>
    public static class ObjectClassFieldMap
    {
        /// <summary>Number of arguments the pass-2 OBJECT macro takes (objects.c:81).</summary>
        public const int ObjectSlotCount = 53;

        /// <summary>Number of values the OBJ macro expands to (objects.c:71).</summary>
        public const int ObjDescriptorSlotCount = 10;

        /// <summary>Number of values the pass-2 BITS macro expands to (objects.c:79).</summary>
        public const int BitsSlotCount = 17;

        /// <summary>Index of the OBJ(...) block inside an OBJECT argument list.</summary>
        public const int ObjBlockSlot = 0;

        /// <summary>Index of the BITS(...) block inside an OBJECT argument list.</summary>
        public const int BitsBlockSlot = 1;

        /* ---------------------------------------------------------------------------------
         * Table 1a -- OBJ(name,desc,contentname,contentdesc,itemdesc,height,odflags,
         *                 stand_anim,enlarge,replacement)   objects.c:71
         * maps one-to-one, in order, onto struct objdescr   objclass.h:1192-1203
         * ------------------------------------------------------------------------------ */
        private static readonly string[] _objDescriptorFields =
        {
            "oc_name",
            "oc_descr",
            "oc_content_name",
            "oc_content_description",
            "oc_item_description",
            "oc_tile_floor_height",
            "oc_descr_flags",
            "stand_animation",
            "enlargement",
            "replacement"
        };

        /* ---------------------------------------------------------------------------------
         * Table 1b -- the values the pass-2 BITS macro emits, in emission order
         *             (objects.c:79-80), landing on the first uchar block of
         *             struct objclass (objclass.h:423-441).
         *
         * BITS takes seventeen arguments and emits seventeen values, but not the same
         * seventeen and not in the same order: the 4th argument (ctnr) is discarded and a
         * literal 0 is emitted into oc_pre_discovered instead, and sub/skill move to the
         * end, after matinit/mtrl.
         * ------------------------------------------------------------------------------ */
        private static readonly string[] _bitsFields =
        {
            "oc_name_known",
            "oc_merge",
            "oc_uses_known",
            "oc_pre_discovered",
            "oc_magic",
            "oc_enchantable",
            "oc_charged",
            "oc_recharging",
            "oc_unique",
            "oc_nowish",
            "oc_big",
            "oc_tough",
            "oc_dir",
            "oc_material_init_type",
            "oc_material",
            "oc_subtyp",
            "oc_skill"
        };

        /* ---------------------------------------------------------------------------------
         * Table 1c -- the pass-2 OBJECT parameter list (objects.c:81) in declaration order,
         *             named by the struct objclass field each parameter initialises
         *             (objects.c:82-83, objclass.h:419-511 and 683-905).
         *
         * oc_name_idx, oc_descr_idx and oc_uname are not OBJECT parameters; the macro body
         * writes 0, 0 and (char *) 0 into them.
         * ------------------------------------------------------------------------------ */
        private static readonly string[] _objectSlotFields =
        {
            "@obj",                             /*  1 obj      -- the OBJ(...) block, Table 1a */
            "@bits",                            /*  2 bits     -- the BITS(...) block, Table 1b */
            "oc_oprop",                         /*  3 prp1 */
            "oc_oprop2",                        /*  4 prp2 */
            "oc_oprop3",                        /*  5 prp3 */
            "oc_pflags",                        /*  6 pflags */
            "oc_class",                         /*  7 sym */
            "oc_prob",                          /*  8 prob */
            "oc_multigen_type",                 /*  9 multigen */
            "oc_delay",                         /* 10 dly */
            "oc_weight",                        /* 11 wt */
            "oc_cost",                          /* 12 cost */
            "oc_damagetype",                    /* 13 dmgtype */
            "oc_wsdice",                        /* 14 sdice */
            "oc_wsdam",                         /* 15 sdam */
            "oc_wsdmgplus",                     /* 16 sdmgplus */
            "oc_wldice",                        /* 17 ldice */
            "oc_wldam",                         /* 18 ldam */
            "oc_wldmgplus",                     /* 19 ldmgplus */
            "oc_extra_damagetype",              /* 20 edmgtype */
            "oc_wedice",                        /* 21 edice */
            "oc_wedam",                         /* 22 edam */
            "oc_wedmgplus",                     /* 23 edmgplus */
            "oc_aflags",                        /* 24 aflags */
            "oc_aflags2",                       /* 25 aflags2 */
            "oc_critical_strike_percentage",    /* 26 critpct */
            "oc_hitbonus",                      /* 27 hitbon */
            "oc_mc_adjustment",                 /* 28 mcadj */
            "oc_fixed_damage_bonus",            /* 29 fixdmgbon */
            "oc_range",                         /* 30 range */
            "oc_oc1",                           /* 31 oc1 */
            "oc_oc2",                           /* 32 oc2 */
            "oc_oc3",                           /* 33 oc3 */
            "oc_oc4",                           /* 34 oc4 */
            "oc_oc5",                           /* 35 oc5 */
            "oc_oc6",                           /* 36 oc6 */
            "oc_oc7",                           /* 37 oc7 */
            "oc_oc8",                           /* 38 oc8 */
            "oc_nutrition",                     /* 39 nut */
            "oc_color",                         /* 40 color */
            "oc_soundset",                      /* 41 soundset */
            "oc_dir_subtype",                   /* 42 dirsubtype */
            "oc_material_components",           /* 43 materials */
            "oc_item_cooldown",                 /* 44 cooldown */
            "oc_special_quality",               /* 45 special_quality */
            "oc_power_permissions",             /* 46 powconfermask */
            "oc_target_permissions",            /* 47 permittedtargets */
            "oc_flags",                         /* 48 flags */
            "oc_flags2",                        /* 49 flags2 */
            "oc_flags3",                        /* 50 flags3 */
            "oc_flags4",                        /* 51 flags4 */
            "oc_flags5",                        /* 52 flags5 */
            "oc_flags6"                         /* 53 flags6 */
        };

        private static readonly Dictionary<string, int> _objectSlotIndex =
            _objectSlotFields
                .Select((f, i) => new KeyValuePair<string, int>(f, i))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        private static readonly Dictionary<string, int> _bitsFieldIndex =
            _bitsFields
                .Select((f, i) => new KeyValuePair<string, int>(f, i))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        private static readonly Dictionary<string, int> _objDescriptorFieldIndex =
            _objDescriptorFields
                .Select((f, i) => new KeyValuePair<string, int>(f, i))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        /* ---------------------------------------------------------------------------------
         * Table 2 -- the same eight int64_t slots oc_oc1..oc_oc8 carry six different sets of
         * values depending on oc_class (objclass.h:684-836). A null entry means the class
         * never stores anything there, so the resolver must not name it.
         * ------------------------------------------------------------------------------ */

        /// <summary>
        /// The general reading of oc_oc1..oc_oc8 (objclass.h:684-691), used by weapons and
        /// armour and by every class that does not override it. oc_oc1 is objclass.h's
        /// oc_armor_class; it holds the AC *bonus*, not the AC.
        /// </summary>
        private static readonly string?[] _generalOcSlots =
        {
            "ac_bonus",                 /* oc_armor_class */
            "magic_cancellation",       /* oc_magic_cancellation */
            "mana_bonus",               /* oc_mana_bonus */
            "hp_bonus",                 /* oc_hp_bonus */
            "bonus_attributes",         /* oc_bonus_attributes -- BONUS_TO_* mask */
            "attribute_bonus",          /* oc_attribute_bonus -- 0 means use the enchantment */
            "spell_casting_penalty",    /* oc_spell_casting_penalty */
            "multishot_style"           /* oc_multishot_style */
        };

        private static readonly Dictionary<string, string?[]> _ocSlotsByClass =
            new(StringComparer.Ordinal)
            {
                /* spellbooks -- objclass.h:782-789 */
                ["SPBOOK_CLASS"] = new string?[]
                {
                    "spell_cooldown", "spell_level", "spell_mana_cost", "spell_attribute",
                    "spell_range", "spell_radius", "spell_skill_chance", "spell_per_level_step"
                },
                /* potions -- objclass.h:803-809 */
                ["POTION_CLASS"] = new string?[]
                {
                    "potion_breathe_buc_multiplier", "potion_normal_buc_multiplier",
                    "potion_nutrition_buc_multiplier", "potion_extra_data1",
                    "potion_breathe_dice_buc_multiplier", "potion_normal_dice_buc_multiplier",
                    "potion_nutrition_dice_buc_multiplier", null
                },
                /* comestibles and reagents -- objclass.h:748-749 */
                ["FOOD_CLASS"] = new string?[]
                {
                    "edible_subtype", "edible_effect", null, null, null, null, null, null
                },
                ["REAGENT_CLASS"] = new string?[]
                {
                    "edible_subtype", "edible_effect", null, null, null, null, null, null
                },
                /* wands -- only the range and radius slots are written (objects.c:4969) */
                ["WAND_CLASS"] = new string?[]
                {
                    null, null, null, null, "spell_range", "spell_radius", null, null
                }
            };

        /* ---------------------------------------------------------------------------------
         * Table 2, continued -- the fields outside oc_oc1..oc_oc8 that objclass.h renames
         * per object class. The key is the presentation name the resolver would otherwise
         * use (the struct field name with its oc_ prefix removed).
         * ------------------------------------------------------------------------------ */
        private static readonly Dictionary<string, Dictionary<string, string>> _fieldAliasesByClass =
            new(StringComparer.Ordinal)
            {
                ["ARMOR_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["subtyp"] = "armor_category",  /* objclass.h:737 */
                    ["big"] = "bulky",              /* objclass.h:440 */
                    ["dir"] = "damage_class"
                },
                ["WEAPON_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["subtyp"] = "weapon_category",
                    ["big"] = "bimanual",           /* objclass.h:439 */
                    ["dir"] = "damage_class"
                },
                ["TOOL_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["big"] = "bimanual"
                },
                ["SPBOOK_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["dir"] = "targeting_mode",
                    ["mc_adjustment"] = "spell_saving_throw_adjustment",   /* objclass.h:790 */
                    ["aflags"] = "spell_flags",                            /* objclass.h:615 */
                    ["aflags2"] = "spell_effect_flags",                    /* objclass.h:616 */
                    ["wsdice"] = "spell_dmg_dice",                         /* objclass.h:791-793 */
                    ["wsdam"] = "spell_dmg_diesize",
                    ["wsdmgplus"] = "spell_dmg_plus",
                    ["wldice"] = "spell_dur_dice",                         /* objclass.h:797-799 */
                    ["wldam"] = "spell_dur_diesize",
                    ["wldmgplus"] = "spell_dur_plus",
                    ["extra_damagetype"] = "spell_dur_buc_plus"            /* objclass.h:800 */
                },
                ["SCROLL_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["aflags"] = "spell_flags",
                    ["aflags2"] = "scroll_effect_flags",                   /* objclass.h:618 */
                    ["wsdice"] = "scroll_dmg_dice",                        /* objclass.h:823-825 */
                    ["wsdam"] = "scroll_dmg_diesize",
                    ["wsdmgplus"] = "scroll_dmg_plus",
                    ["wldice"] = "scroll_dur_dice",                        /* objclass.h:826-828 */
                    ["wldam"] = "scroll_dur_diesize",
                    ["wldmgplus"] = "scroll_dur_plus",
                    ["extra_damagetype"] = "spell_dur_buc_plus"
                },
                ["WAND_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["dir"] = "targeting_mode",
                    ["mc_adjustment"] = "spell_saving_throw_adjustment",
                    ["aflags"] = "spell_flags",
                    ["aflags2"] = "spell_effect_flags",
                    ["wsdice"] = "wand_dmg_dice",                          /* objclass.h:831-833 */
                    ["wsdam"] = "wand_dmg_diesize",
                    ["wsdmgplus"] = "wand_dmg_plus",
                    ["wldice"] = "wand_dur_dice",                          /* objclass.h:834-836 */
                    ["wldam"] = "wand_dur_diesize",
                    ["wldmgplus"] = "wand_dur_plus",
                    ["extra_damagetype"] = "spell_dur_buc_plus"
                },
                ["POTION_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["mc_adjustment"] = "potion_saving_throw_adjustment",   /* objclass.h:810 */
                    ["aflags2"] = "potion_effect_flags",                    /* objclass.h:617 */
                    ["wsdice"] = "potion_breathe_dice",                     /* objclass.h:811-813 */
                    ["wsdam"] = "potion_breathe_diesize",
                    ["wsdmgplus"] = "potion_breathe_plus",
                    ["wldice"] = "potion_normal_dice",                      /* objclass.h:815-817 */
                    ["wldam"] = "potion_normal_diesize",
                    ["wldmgplus"] = "potion_normal_plus",
                    ["wedice"] = "potion_nutrition_dice",                   /* objclass.h:818-820 */
                    ["wedam"] = "potion_nutrition_diesize",
                    ["wedmgplus"] = "potion_nutrition_plus"
                },
                ["FOOD_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["critical_strike_percentage"] = "effect_probability"   /* objclass.h:854 */
                },
                ["REAGENT_CLASS"] = new(StringComparer.Ordinal)
                {
                    ["critical_strike_percentage"] = "effect_probability"
                }
            };

        /// <summary>The ten values OBJ expands to, named by struct objdescr field.</summary>
        public static IReadOnlyList<string> ObjDescriptorFields => _objDescriptorFields;

        /// <summary>The seventeen values pass-2 BITS emits, named by struct objclass field.</summary>
        public static IReadOnlyList<string> BitsFields => _bitsFields;

        /// <summary>
        /// The fifty-three pass-2 OBJECT arguments, named by struct objclass field. Slots
        /// <see cref="ObjBlockSlot"/> and <see cref="BitsBlockSlot"/> are the nested OBJ and
        /// BITS blocks and carry the placeholder names "@obj" and "@bits".
        /// </summary>
        public static IReadOnlyList<string> ObjectSlotFields => _objectSlotFields;

        /// <summary>Position of an OBJECT argument, or -1.</summary>
        public static int ObjectSlotOf(string objclassField) =>
            _objectSlotIndex.TryGetValue(objclassField, out int i) ? i : -1;

        /// <summary>Position of a BITS-emitted value, or -1.</summary>
        public static int BitsSlotOf(string objclassField) =>
            _bitsFieldIndex.TryGetValue(objclassField, out int i) ? i : -1;

        /// <summary>Position of an OBJ-expanded value, or -1.</summary>
        public static int ObjDescriptorSlotOf(string objdescrField) =>
            _objDescriptorFieldIndex.TryGetValue(objdescrField, out int i) ? i : -1;

        /// <summary>
        /// The eight names oc_oc1..oc_oc8 carry for <paramref name="objectClass"/>. A null
        /// entry means the class stores nothing in that slot.
        /// </summary>
        public static IReadOnlyList<string?> GetOcSlotNames(string? objectClass)
        {
            if (objectClass != null && _ocSlotsByClass.TryGetValue(objectClass, out var names))
            {
                return names;
            }
            return _generalOcSlots;
        }

        /// <summary>
        /// Whether <paramref name="objectClass"/> uses the general reading of oc_oc1..oc_oc8,
        /// in which a stored zero is still a meaningful statement about the item.
        /// </summary>
        public static bool UsesGeneralOcSlots(string? objectClass) =>
            objectClass == null || !_ocSlotsByClass.ContainsKey(objectClass);

        /// <summary>
        /// The name <paramref name="objectClass"/> gives to a field, or
        /// <paramref name="presentationName"/> when the class does not rename it.
        /// </summary>
        public static string GetFieldName(string? objectClass, string presentationName)
        {
            if (objectClass != null
                && _fieldAliasesByClass.TryGetValue(objectClass, out var aliases)
                && aliases.TryGetValue(presentationName, out string? alias))
            {
                return alias;
            }
            return presentationName;
        }
    }
}
