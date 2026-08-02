Retrieve entries from the player's local game log (xlogfile).
Returns a listing of past games with rich metadata: character name, role,
race, gender, alignment, XP level, HP, game mode, turns, score, outcome,
death date, real time played, and whether a dumplog file exists.

By default, returns the 50 newest games. Use the 'limit' and 'offset'
parameters to paginate through older games if needed.

Each entry includes a dumplog_filename field if a dumplog exists for that game.
Use this filename with get_player_dumplogs to read the full dumplog.

This tool requires client data to be enabled.
