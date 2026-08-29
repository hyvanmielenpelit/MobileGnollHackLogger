Request a fresh game state snapshot from the client device and return it as plain text.

The snapshot contains, in this order: the map with its reading notes and legend, the
status lines, recent in-game messages, the **`Pets`** section (every tame companion on
this level, with position, HP and statistics), inventory, attributes, weapon and spell
skills, known spells, the **`Discoveries`** section (the object types the player has
identified in this game, written as `true name (appearance)`), the game log, and the
dungeon overview.

## The `Pets` section
`Pets` is the authoritative, complete roster of every tame monster on the hero's
**current level**. It opens with a roll call — one line per pet with name, `<x,y>`
position, distance from the hero, and current/maximum HP — followed by full statistics
blocks (level, AC, magic cancellation, magic resistance, attacks, attribute scores,
intrinsics, worn and wielded equipment, status marks, conditions and buffs) for the
first few pets.

These are the same animals the map legend's `Notable locations` already describes: a
pet shown on the map appears there too, as a `Creature <x,y>` line reading
`level N tame <species>, HP:n(m) AC:n <alignment>` plus hunger and status. Coordinates
are identical in both, so cross-reference by `<x,y>` and never count a pet twice.
Where they disagree, prefer `Pets`: the legend caps its coordinate lines and describes
the hero's *memory* of the map, so it can omit a pet or place it where it used to be.

The roll call is always complete; only the detail blocks are capped (at 6), and the
section says so when it caps them. `(None)` means the hero has no pets — not that the
data is missing; a snapshot with no `Pets` section at all came from an older client.
Pets on other dungeon levels are not listed, and the player cannot see them either.
Under hallucination expect pet names to be distorted, exactly as they are for the
player.

## When to call this
Only when the snapshot already in your context is **stale or missing**:
- The session was resumed from the player's session list and may describe an older game
  or a different character.
- The "Available Context in This Session" section of your instructions does not list a
  game snapshot.
- The player says the situation has changed since the conversation started.

## When NOT to call this
- **Do not call it to re-read something already in your context.** GnollHack is
  turn-based and the game is paused while the Overseer is open, so within a normally
  opened session a refresh returns state identical to the snapshot you already have.
- Do not call it speculatively at the start of a conversation.

## Failure modes
The tool returns an error rather than content when there is no active game, or when the
player has turned *Send Game Context* off in the Overseer settings. Both are legitimate
answers. Report them to the player plainly and do not retry — in particular, an opt-out
is the player's explicit choice and must not be worked around by other means.

## Truncation
The client caps a snapshot at 60,000 characters and appends
`[SNAPSHOT TRUNCATED at 60000 characters.]` when it has to. If you see that marker, the
tail sections are missing — say so rather than treating the `Discoveries` list as complete.
