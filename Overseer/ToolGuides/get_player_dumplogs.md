Access dumplog files stored on the player's device.

This tool has two modes:

1. LIST MODE (no filename): Returns a list of all dumplog files that actually
   exist on the device. Each entry includes the filename, linked game info
   (if an xlog entry matches), file size, and format (txt, html, or both).
   Files are deduplicated so .txt/.html pairs for the same game appear as one
   entry. Some dumplogs may be "orphaned" (no matching xlog entry) if the
   xlog file was deleted or corrupted.

2. READ MODE (filename specified): Reads the full text of a specific dumplog.
   Get the filename from list mode or from get_player_xlog's dumplog_filename
   field. HTML files are automatically stripped of tags for readability.

⚠️ DO NOT use this tool for routine spoiler checking. Assume by default that past
games have not revealed spoiler content. Only read a dumplog when the player
explicitly asks about a past game (e.g., "why did I die?", "what happened in my
last run?"). When you do read a dumplog, you may then update your understanding
of what the player has seen and adjust spoiler filtering accordingly.

This tool requires client data to be enabled.

When reading a specific dumplog, content is truncated to `max_length` characters (default 4000). If you need more complete dumplog content (e.g. for detailed post-mortem analysis), pass a higher `max_length` value such as 16000.
