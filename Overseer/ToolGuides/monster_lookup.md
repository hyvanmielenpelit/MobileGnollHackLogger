Search the GnollHack wiki for monster information.

Uses keyword search across wiki articles in the "monster" category.
Results are wiki articles, not structured game data — they may be
incomplete or not available for all monsters. If you need exact stats
(HP, AC, attacks, MR, flags), verify against src/monst.c using
source_code_search, or call get_monster_stats.

Monster pages open with a header line of the form `## Level N <description>` and then list
`Hit dice: M`. **`Level N` in that header is the monster's difficulty rating, not its level.** Its
level (the `mlevel` field of `src/monst.c`) is the `Hit dice` line. Report `Hit dice` as the level,
never the header number; if you need the level or hit dice and only the header is present, call
`get_monster_stats`.

For the hero's own pets, the snapshot's `Pets` section reports that individual's actual
condition; the wiki describes the species.
