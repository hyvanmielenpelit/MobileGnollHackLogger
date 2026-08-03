Look up #define constants and enum values from the GnollHack source code.

Fast O(1) lookup. Use this instead of source_code_search when you need to
resolve a specific constant name (e.g., PM_GNOLL, WAN_DEATH, AD_FIRE).

Supports wildcard patterns: "AD_*" returns all attack damage type constants.
Use prefix_filter for broader category browsing: prefix_filter="PM_" lists
all monster indices.

Constants from generated headers (pm.h, onames.h) are included when the
server-side makedefs pipeline is configured.
