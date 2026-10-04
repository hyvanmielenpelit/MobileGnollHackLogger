Search the local NetHack Wiki database for information about NetHack game mechanics,
monsters, items, spells, dungeon features, and strategies.

This tool searches a local copy of nethackwiki.com content. Use it when:
- The user asks about general NetHack mechanics that may differ from GnollHack
- You need background information on a NetHack concept (e.g., stoning, Elbereth, wish)
- You want to compare how something works in NetHack vs GnollHack
- The user references a NetHack-specific term or mechanic

Do NOT use this tool when:
- The question is specifically about GnollHack — use wiki_search (GnollHack wiki) instead
- You need monster/item stats from GnollHack source — use get_monster_stats/get_item_stats
- You need GnollHack source code — use source_code_search/source_code_view

The corpus holds the English main-namespace articles of nethackwiki.com about NetHack itself;
variant-only articles, articles without game content (community, tournaments, websites,
development), source-code pages, and forum and wiki administration pages are not included.

Returns up to 5 articles (default 3), each capped at 3,000 characters with a truncation note
pointing at nethack_wiki_view; the whole result is never cut mid-article.
