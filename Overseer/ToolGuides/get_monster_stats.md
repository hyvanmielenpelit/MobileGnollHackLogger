Look up authoritative monster statistics directly from the GnollHack game data.

Returns a unified JSON response with these fields:
- "stats": structured data parsed from src/monst.c (null if parsing failed)
- "flag_descriptions": human-readable descriptions for every flag constant
- "macro_definitions": relevant #define macros (empty if parsing succeeded)
- "struct_definitions": relevant struct definitions (empty if parsing succeeded)
- "raw_definition": raw MON() source text (null if parsing succeeded)
- "error": error message (null if no error)
- "message": informational message (null if none)

Use this tool for precise stat questions: exact AC, MR, damage dice, speed,
resistances, flags. Every monster in the game is indexed.

For strategy advice, gameplay tips, or descriptions, use monster_lookup or
wiki_search FIRST — they contain information not captured in raw struct fields.
Only fall back to this tool if the wiki lacks data for the specific monster.

For the hero's **own pets**, prefer the snapshot's `Pets` section. This tool returns the
species row from `src/monst.c`; a pet has its own level, HP, AC, equipment and
intrinsics, and those are what the snapshot reports.
