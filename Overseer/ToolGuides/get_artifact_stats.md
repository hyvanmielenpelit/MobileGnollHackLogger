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

`otyp` names the artifact's base item. What the base item does when used — its effect text,
charges and weight — is in `get_item_stats` under that item's exact name (for The Holy Grail,
`GRAIL_OF_HEALING` is `grail of healing`). Issue both calls together only when you already
know that name; otherwise read `otyp` first and then look the item up. What invoking the
artifact does beyond its base item may need `wiki_search` or the source.

For descriptions, strategy tips, and usage advice, use wiki_search FIRST — it contains
gameplay context not in raw struct fields.

Scope carve-out — exact numbers: whenever your answer will state exact numeric or
enumerated artifact facts (attack damage, cost, alignment, base item type, spfx/cspfx
values, artifact flags, role/race restrictions, invoke properties, material), this tool
is authoritative and you must call it, regardless of which branch the question routed to
and regardless of whether the wiki already had a page. The wiki supplies context, not
numbers.
