Retrieve a specific article from the local NetHack Wiki database by its exact title.
Use this when you already know the article name and want to read its full content or
a specific section.

Use the `section` parameter to retrieve only a specific section heading (e.g., "Strategy",
"Generation", "Variants") to reduce response size. A section request may be the heading's
exact text, its text without a leading symbol, or a phrase that appears in exactly one heading.
When no heading matches, the reply names every heading in the article.

If you don't know the exact article title, use nethack_wiki_search first to find relevant
articles, then use this tool to read the full content.
