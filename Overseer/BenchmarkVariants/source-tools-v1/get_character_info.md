Look up a race's or a role's own rules directly from the GnollHack game tables (src/role.c, src/attrib.c and src/prop.c).

Parameters: `race`, `role`, or both; at least one is required. Names match case-insensitively on the noun, the adjective, the plural, or the male or female form.

Returns a JSON response:
- for a race: "name", "allowed_roles", "allowed_alignments", "always_peaceful_races", "always_hostile_races", "abilities_by_level" (the intrinsics gained at each experience level) and "traits";
- for a role: "name", "allowed_races", "allowed_alignments", "quest_artifact" and "abilities_by_level";
- "note": how a monster's own flags take precedence over race attitudes;
- "raw_definition" and "message" when a table row could not be parsed.

Use this tool when the answer depends on the player's own race or role: which monster races are always peaceful or always hostile to that race, which intrinsics a race or role gains and at which experience level, which roles, races and alignments combine, and a role's quest artifact. The game tables are authoritative for these facts; a wiki page may only summarize them. Whether one particular monster is peaceful also depends on that monster's own flags; read get_monster_stats for those.