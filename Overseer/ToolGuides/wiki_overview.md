Count and list the articles of the GnollHack wiki or the NetHack wiki. Use it for how many
articles a wiki has, which directories it has, or a list of its articles. It answers from
Overseer's index, so a count describes Overseer's local copy of the wiki, which can differ
slightly from the live site; say so when you report one.

`wiki` selects `gnollhack` (default) or `nethack`.

`list_titles: true` also lists article paths (GnollHack) or titles (NetHack), one per line, with
`offset` and `limit` for paging (default 100 articles, maximum 200). A GnollHack path can be passed
to `wiki_view`, and a NetHack title to `nethack_wiki_view`.

`category` works on the GnollHack wiki only. It is a case-insensitive substring of the article's
path, file name included, as in `wiki_search`, and the result adds how many articles match. The
NetHack wiki has no directories and refuses `category`.

To find what an article says, use `wiki_search` or `nethack_wiki_search` instead; this tool reads no
article text.
