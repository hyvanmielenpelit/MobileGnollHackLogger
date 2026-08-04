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
Only fall back to this tool if the wiki lacks data for the specific item.
