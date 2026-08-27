**EXPENSIVE TOOL** — Source code searches typically cascade into multiple follow-up calls
(discovery → targeted search → context view), easily consuming 5–15 tool calls per question.
Before using this tool, check whether wiki_search, monster_lookup, or item_lookup can 
answer the question. Only use this tool when you need exact formulas, undocumented mechanics,
or code-level details that lighter tools cannot provide.

Search the GnollHack or NetHack C source code for functions, macros, constants, or game mechanic implementations.
Use the `repository` parameter to select the codebase (default: gnollhack).
Use this tool to verify undocumented game mechanics, check exact formulas or probabilities,
investigate potential bugs, or find how specific features are implemented in the codebase.

The codebase is organized as:
- src/*.c — C source files (game logic, combat, spells, items, monsters, dungeon generation, etc.)
- include/*.h — Header files (data structures, macros, constants, monster/object definitions)
- dat/*.des — Level description files (special level layouts)
- dat/*.txt — Text databases (quest dialogues, rumors, encyclopedia entries)
- win/win32/xpl/ — .NET MAUI frontend (C#/XAML) — GnollHack only, Debug Mode only

Key files for common mechanic lookups:
- In GnollHack: monster definitions are in src/monst.c and object definitions are in src/objects.c.
- In NetHack 5.0: monster definitions are in include/monsters.h and object definitions are in include/objects.h (src/monst.c and src/objects.c are stubs).
- src/potion.c, src/read.c, src/zap.c — Item usage (potions, scrolls, wands)
- src/uhitm.c, src/mhitu.c, src/mhitm.c — Combat (player-vs-monster, monster-vs-player, monster-vs-monster)
- src/mon.c, src/mondata.c, src/makemon.c — Monster behavior, data, creation
- src/spell.c — Spellcasting mechanics
- src/artifact.c — Artifact properties and effects
- src/trap.c — Trap mechanics
- src/pray.c — Prayer mechanics
- src/eat.c — Eating/nutrition
- src/weapon.c — Weapon skills and damage
- src/shk.c — Shopkeeper interactions
- src/fountain.c — Fountain effects
- include/monst.h, include/obj.h — Core data structures
- include/mondata.h — Monster property macros (resistances, flags)
- include/youprop.h — Player property macros
- src/objects.c — Object definitions (all items with stats; GnollHack). WARNING: Appearance/description strings in src/objects.c are pre-shuffle templates; appearances are randomized each game by shuffle_all() in src/o_init.c.
- src/monst.c — Monster definitions (all monsters with stats; GnollHack)

Search tips:
- Search for function names (e.g., "potionhit", "hitmu", "rn2")
- Search for constants (e.g., "PM_GNOLL", "SPE_FIREBALL", "EXPL_FIERY")
- Search for game messages to find the code that produces them (e.g., "You feel a numbness")
- Use file_filter to narrow results to a specific file when you know where to look

## Parameters
- `query` (string, required): The search terms to look up in the source code.
- `file_filter` (string, optional): A substring to filter the returned file paths.
- `max_results` (integer, optional): The maximum number of files to return matches from. Defaults to 10. Max is 100.
- `is_regex` (boolean, optional): If true, the query is treated as a regular expression. This is extremely useful for pattern matching, e.g., finding all random number calls like `rn[12]\(\d+\)`.
- `filenames_only` (boolean, optional): If true, the tool will only return file paths and match counts, without any code snippets. This is very useful for getting a broad overview of where a term is used across the codebase without exceeding output limits.
- `context_lines` (integer, optional): The number of context lines to include before and after each match. Defaults to 5. Max is 25. Increase this if you need to see surrounding logic or function signatures.
- `repository` (string, optional): Which codebase to search: 'gnollhack' (default) or 'nethack'. Use 'nethack' when investigating NetHack-specific mechanics or comparing with GnollHack.

## Search Strategy
1. **Discover**: Start by using the `list_indexed_files` tool to get a sense of the repository structure or find specific files.
2. **Survey**: Use `source_code_search` with `filenames_only: true` to quickly find out which files contain a term or function.
3. **Locate**: Use `source_code_search` with `is_regex` or regular queries to find specific lines. Adjust `context_lines` up to 25 if you need more context around the match.
4. **Deep Dive**: If the context lines aren't enough or the output gets truncated, use `source_code_view` to read the entire function or file.

After finding relevant code, use source_code_view to see more context around the match.
