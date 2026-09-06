Search the GnollHack wiki for monster information.

Uses keyword search across wiki articles in the "monster" category.
Results are wiki articles, not structured game data — they may be
incomplete or not available for all monsters. If you need exact stats
(HP, AC, attacks, MR, flags), verify against src/monst.c using
source_code_search, or call get_monster_stats.

A wiki monster page's difficulty number is not the monster's level and not its
hit dice. Difficulty is a derived encounter rating; level/hit dice come from the
LVL() entry in src/monst.c. Never report one as the other, and never present a
difficulty value as a level. If a page gives difficulty but you need level or hit
dice, call get_monster_stats.

For the hero's own pets, the snapshot's `Pets` section reports that individual's actual
condition; the wiki describes the species.
