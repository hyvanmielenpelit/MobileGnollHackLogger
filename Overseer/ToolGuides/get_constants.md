Look up #define constants and enum values from the GnollHack or NetHack source code.
Use `repository` to select the codebase (default: gnollhack).

Fast O(1) lookup. Use this instead of source_code_search when you need to
resolve a specific constant name (e.g., PM_GNOLL, WAN_DEATH, AD_FIRE).

Supports wildcard patterns: "AD_*" returns all attack damage type constants.
Use prefix_filter for broader category browsing: prefix_filter="PM_" lists
all monster indices.

Constants from generated headers (pm.h, onames.h) are included when the
server-side makedefs pipeline is configured.

## Parameters
- `name` (string, required): Constant name or wildcard pattern (e.g., 'PM_GNOLL', 'AD_*', 'WAN_*').
- `prefix_filter` (string, optional): Filter by prefix (e.g., 'PM_', 'AD_', 'WAN_', 'ART_').
- `repository` (string, optional): Which codebase to search: 'gnollhack' (default) or 'nethack'.
