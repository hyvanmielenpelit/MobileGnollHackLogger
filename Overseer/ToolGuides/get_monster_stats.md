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

## Reading ac, mc and mr

Three of these fields are on scales that run in different directions, and none of them is a
plain "higher is stronger" number. State the direction whenever you quote one.

- `ac` — armour class. **Lower is better for the defender**: it makes the monster harder to
  hit, and it goes negative for well-armoured monsters.
- `mc` — magic cancellation. A small **level**, not a percentage, and **higher is better** for
  the monster. The game converts the level into a chance to negate a magical touch attack, so
  the number in this field is not itself a percentage.
- `mr` — magic resistance. A **percentage from 0 to 100**, and **higher is better** for the
  monster: it is the chance to resist other magic outright. A small value therefore means
  *little* resistance, not a little resistance that is hard to overcome.

`mc` and `mr` are base values for the species. What a particular monster gets is adjusted at
run time by its own intrinsics and equipment.

For strategy advice, gameplay tips, or descriptions, use monster_lookup or
wiki_search FIRST — they contain information not captured in raw struct fields.

Scope carve-out — exact numbers: whenever your answer will state exact numeric
stats (level, hit dice, AC, MC, MR, speed, damage dice, resistances, flags), this
tool is authoritative and you must call it, regardless of which branch the question
routed to and regardless of whether the wiki already had a page. A "what am I up
against" or strategy question that you then answer with exact numbers is an exact-stat
question for those numbers. The wiki supplies context, not numbers.

For the hero's **own pets**, prefer the snapshot's `Pets` section. This tool returns the
species row from `src/monst.c`; a pet has its own level, HP, AC, equipment and
intrinsics, and those are what the snapshot reports.
