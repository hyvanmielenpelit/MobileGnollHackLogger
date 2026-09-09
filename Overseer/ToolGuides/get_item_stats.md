Look up authoritative item statistics directly from the GnollHack game data.

Returns a unified JSON response with these fields:
- "stats": the named object class values, expanded from the macro chain in src/objects.c.
  Null only when the entry could not be expanded, in which case "message" says why.
- "raw_definition": the raw WEAPON()/ARMOR()/SCROLL()/etc. source text. Always present, so the
  invocation is available even when "stats" is not.
- "flag_descriptions": human-readable descriptions for every flag constant in the entry
- "macro_definitions": the #define macros needed to read "raw_definition" by hand. Empty when
  "stats" was produced, because then nothing needs reading by hand.
- "struct_definitions": relevant struct definitions, empty on the same condition
- "error": error message (null if no error) — set when src/objects.c is not indexed, or when
  no entry carries that name
- "message": informational message (null if none)

Look items up by the **bare name as src/objects.c writes it**, not by the display name:
`digging`, not `wand of digging`; `identify`, not `scroll of identify`. Spelling and
hyphenation are literal.

Some names belong to entries in more than one object class — the same word can name a scroll,
a wand and a spellbook. When that happens the result carries `ambiguous_object_classes` and a
note saying which class the values came from. Say which one you are describing, and if it is not
the one the question is about, call again with `object_class` set to one of the listed values.

## Reading the numbers

Several fields are stored in units the player never sees, and the "notes" entry in "stats"
states the conversion for each one it applies to. Read it before quoting a number.

The one that matters most is armour class. GnollHack armour stores `10 - ac`, so:
- `base_ac` is the `ac` argument written in src/objects.c
- `ac_bonus` is the stored `oc_armor_class`, which is `10 - base_ac`
- the game **negates** `ac_bonus` into the hero's AC, so a positive `ac_bonus` **lowers** AC,
  and lower AC is better

So an answer may quote either figure, but it must say which one it is quoting and which
direction the effect runs. "AC 1" and "improves your AC by 9" describe the same armour.

Magic cancellation and the spellcasting penalty work the same way: `magic_cancellation` is a
stored level that is adjusted further at run time, and `spell_casting_penalty` is a stored
value whose player-visible form is `spell_casting_penalty_percent`.

Use this tool for precise stat questions: exact damage dice, weight, cost, AC,
material type. Every item in the game is indexed.

For descriptions, strategy tips, and usage advice, use item_lookup or
wiki_search FIRST — they contain gameplay context not in raw struct fields.

Scope carve-out — exact numbers: whenever your answer will state exact numeric
stats (damage dice, weight, cost, AC, material, item flags), this tool is
authoritative and you must call it, regardless of which branch the question routed
to and regardless of whether the wiki already had a page. The wiki supplies context,
not numbers.

These are **object class** values: one particular object's numbers are modified further at run
time by its material, enchantment, exceptionality and erosion. `material` is the base material,
and unless the entry's `material_init_type` is `MATINIT_BASE_MATERIAL` a generated object's
material is randomised.

IMPORTANT: The appearance/description field in the raw definition (e.g. the second positional argument of SCROLL(), POTION(), WAND(), RING(), AMULET(), SPELL()) is an unidentified description template and is randomized per game. Report it only as "one of the possible appearances in the game pool", never as this game's assigned identity.

The player's actual identities for this game are in the snapshot's `Discoveries` section — check there before answering an "is this X?" question.
