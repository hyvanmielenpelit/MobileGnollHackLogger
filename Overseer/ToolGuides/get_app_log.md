Reads the GnollHack application log (ghlog.txt) from the player's device.

Contains timestamped entries for app startup, connection events, UI actions, errors, and debug information. Useful for diagnosing connection failures, UI issues, performance problems, and unexpected app behavior.

Use `last_n` to retrieve only the most recent log entries (e.g. `last_n: 100` for the last 100 lines). Use `search_term` to filter for specific events (e.g. `search_term: "Overseer"` to find Overseer-related log entries).

The log file may be large. If you only need recent events, always specify `last_n` to avoid unnecessarily large responses.
