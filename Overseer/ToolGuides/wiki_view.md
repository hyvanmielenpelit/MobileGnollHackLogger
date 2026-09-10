View a specific wiki article by name. Use when you already know which
article you want. Faster and more reliable than wiki_search for known articles.

Use section parameter to extract a specific section (e.g., section: "Strategy").

`article` is matched against title and filename only, and the single best Lucene hit is
returned with **no relevance floor** — a misspelled or invented name therefore returns whatever
article scored highest rather than a miss. Check that the returned article is the one you asked
for before quoting it, and use `wiki_search` when you are not certain of the title.

A `section` that matches no heading in the article is **not** an error and does not cost you the
result: the tool returns `[Section '...' not found in article. Returning full text.]` followed by the
whole article. Read the headings from that text rather than guessing another spelling.
