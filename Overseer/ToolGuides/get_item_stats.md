Look up authoritative item statistics directly from the GnollHack game data.

Returns a unified JSON response with these fields:
- "stats": structured data parsed from src/objects.c (null if parsing failed)
- "flag_descriptions": human-readable descriptions for every flag constant
- "macro_definitions": relevant #define macros (empty if parsing succeeded)
- "struct_definitions": relevant struct definitions (empty if parsing succeeded)
- "raw_definition": raw WEAPON()/ARMOR()/etc. source text (null if parsing succeeded)
- "error": error message (null if no error)
- "message": informational message (null if none)

Use this tool for precise stat questions: exact damage dice, weight, cost, AC,
material type. Every item in the game is indexed.

For descriptions, strategy tips, and usage advice, use item_lookup or
wiki_search FIRST — they contain gameplay context not in raw struct fields.

Scope carve-out — exact numbers: whenever your answer will state exact numeric
stats (damage dice, weight, cost, AC, material, item flags), this tool is
authoritative and you must call it, regardless of which branch the question routed
to and regardless of whether the wiki already had a page. The wiki supplies context,
not numbers.

IMPORTANT: The appearance/description field in the raw definition (e.g. the second positional argument of SCROLL(), POTION(), WAND(), RING(), AMULET(), SPELL()) is an unidentified description template and is randomized per game. Report it only as "one of the possible appearances in the game pool", never as this game's assigned identity.

The player's actual identities for this game are in the snapshot's `Discoveries` section — check there before answering an "is this X?" question.
