Request a fresh game state snapshot from the client device and return it as plain text.

The snapshot contains the current map, the status lines, inventory, attributes, weapon
and spell skills, known spells, the **`Discoveries`** section (the object types the player
has identified in this game, written as `true name (appearance)`), recent in-game
messages, and the dungeon overview.

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
