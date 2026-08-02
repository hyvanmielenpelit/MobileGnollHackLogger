Search the GnollHack C source code for functions, macros, constants, or game mechanic implementations.
Use this tool to verify undocumented game mechanics, check exact formulas or probabilities,
investigate potential bugs, or find how specific features are implemented in the codebase.

The codebase is organized as:
- src/*.c — C source files (game logic, combat, spells, items, monsters, dungeon generation, etc.)
- include/*.h — Header files (data structures, macros, constants, monster/object definitions)
- dat/*.des — Level description files (special level layouts)
- dat/*.txt — Text databases (quest dialogues, rumors, encyclopedia entries)
- win/win32/xpl/ — .NET MAUI frontend (C#/XAML) — only available in Debug Mode

Key files for common mechanic lookups:
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
- src/objects.c — Object definitions (all items with stats)
- src/monst.c — Monster definitions (all monsters with stats)

Search tips:
- Search for function names (e.g., "potionhit", "hitmu", "rn2")
- Search for constants (e.g., "PM_GNOLL", "SPE_FIREBALL", "EXPL_FIERY")
- Search for game messages to find the code that produces them (e.g., "You feel a numbness")
- Use file_filter to narrow results to a specific file when you know where to look

After finding relevant code, use source_code_view to see more context around the match.
