Look up authoritative artifact statistics directly from the GnollHack game data.

Returns a unified JSON response with these fields:
- "stats": structured data parsed from include/artilist.h (null if parsing failed)
- "flag_descriptions": human-readable descriptions for every flag constant
- "macro_definitions": relevant #define macros (empty if parsing succeeded)
- "struct_definitions": relevant struct definitions (empty if parsing succeeded)
- "raw_definition": raw A()/GENERAL_ARTIFACT() source text (null if parsing succeeded)
- "error": error message (null if no error)
- "message": informational message (null if none)

Use this tool for precise artifact questions: base item type, special effects (spfx/cspfx),
artifact flags, attack damage, alignment, role/race restrictions, invoke properties,
cost, and material. Every artifact in the game is indexed.

For descriptions, strategy tips, and usage advice, use wiki_search FIRST — it contains
gameplay context not in raw struct fields. Only fall back to this tool if the wiki
lacks data for the specific artifact.
