# NetHack Wiki Dump Converter

This directory contains the Python conversion script used to convert a MediaWiki XML dump of the NetHack Wiki (from [nethackwiki.com](https://nethackwiki.com/)) into clean plain text Markdown files for Overseer's local Lucene.Net search index (`NetHackWikiService`).

---

## Overview

NetHackWiki is protected against automated HTTP scrapers by Cloudflare WAF. Overseer uses a local offline copy of the wiki instead. This script parses the MediaWiki XML dump (`*-pages-articles.xml` / `nethackwiki_current.xml`) and outputs:
- One `.md` file per article with YAML frontmatter (`title`, `namespace`, `summary`).
- Structured key-value stat blocks (e.g., `--- Monster Stats ---`, `--- Item Stats ---`).
- Markdown headings (`##`, `###`) for section extraction.
- Wikitables as Markdown pipe tables; maps, configuration examples and code as fenced code blocks.
- A generated `_index.json` mapping filenames to titles and namespaces.
- A generated `_conversion_report.json` with the run's figures (see *The Conversion Report*).

Only English main-namespace articles about NetHack itself are written; see *What Is Converted*.

---

## Prerequisites

1. **Python 3.10+**
2. **`mwparserfromhell` library**:
   ```bash
   pip install mwparserfromhell
   ```

---

## How to Regenerate NetHack Wiki Files

### Step 0: Back Up the Existing Tree

**Required, not optional.** The generated articles are not under version control — nothing else holds a copy. Before running the converter, copy the current `NetHackWikiPath` tree to a **new subdirectory of `C:\Backup\NetHack Wiki` named for the backup's date and time**:

```powershell
$dest = Join-Path 'C:\Backup\NetHack Wiki' (Get-Date -Format 'yyyy-MM-dd_HHmm')
Copy-Item -Path 'c:\hmp\nethackwiki' -Destination $dest -Recurse
```

The date-and-time naming is load-bearing rather than tidy: no benchmark run column records a NetHack wiki revision, so the backup directory's name is the only durable record of when a regeneration happened. Verify that the backup holds the same number of files and bytes as the source before going on.

Articles in this tree are **never hand-edited** — the converter is the only writer, and a manual edit is lost by the next regeneration without a trace.

### Step 1: Obtain a NetHack Wiki XML Dump

Download the latest MediaWiki XML dump (current pages export) and keep it under a dated directory:

```
C:\files\NetHackWiki\<YYYY-MM-DD>\nethackwiki_current.xml
```

The images that come with a dump are not needed: the corpus is text, and the converter removes `File:`/`Image:` links.

### Step 2: Remove the Old Generated Files

The converter writes files and never deletes them, so an article that the new dump no longer has, or that the converter no longer converts, would stay in the index as a stale page. After the backup is verified, confirm that the target directory has no subdirectories and no other file types, then remove the generated files from its top level:

```powershell
$target = 'c:\hmp\nethackwiki'
Get-ChildItem -Path $target -Directory          # must list nothing
Get-ChildItem -Path $target -File | Where-Object { $_.Extension -ne '.md' -and $_.Name -notin '_index.json', '_conversion_report.json' }   # must list nothing
Remove-Item -Path (Join-Path $target '*.md'), (Join-Path $target '_index.json'), (Join-Path $target '_conversion_report.json') -ErrorAction SilentlyContinue
```

### Step 3: Run the Conversion Script

Run the script specifying the input XML file and target output directory:

```bash
python convert_nethackwiki_dump_md.py <path_to_input_xml> <output_directory>
```

#### Example:
```bash
python convert_nethackwiki_dump_md.py C:\files\NetHackWiki\2026-10-04\nethackwiki_current.xml c:\hmp\nethackwiki
```

A full conversion takes about a minute. Convert into a scratch directory first and compare its `_conversion_report.json` with the previous one before converting into `NetHackWikiPath`.

### Step 4 (Optional): Test Mode
To test conversion on a subset of articles without processing the entire dump:

```bash
python convert_nethackwiki_dump_md.py <path_to_input_xml> <output_directory> --test-titles "Cockatrice,Elbereth,Wand of digging"
```

The template map is always read from the whole dump, so test mode renders templates exactly as a full run does.

### Unit Tests

The unit tests use synthetic wikitext and need no dump. From the repository root:

```bash
python -m unittest discover -s Overseer/Scripts/NetHackWiki -p "test_*.py"
```

---

## Output Details

- **Output Directory**: The target directory (e.g., `c:\hmp\nethackwiki`) contains about 2,500 `.md` files, `_index.json` and `_conversion_report.json`, and no subdirectories.
- **Target Configuration**: Make sure `NetHackWikiPath` in Overseer's `appsettings.json` points to this output directory:
  ```json
  "NetHackWikiPath": "c:\\hmp\\nethackwiki"
  ```
- **Indexing is startup-only**: `NetHackWikiService` scans this directory and indexes all markdown files into a Lucene.Net RAM index **once, when Overseer starts**. There is no periodic re-indexing timer — NetHackWiki is thousands of static files updated only by a manual regeneration, and repeatedly scanning them would cost CPU and disk I/O for nothing. The two JSON files are not `.md` files and are not indexed.
- **Restart Overseer after a regeneration**, or none of the new content is indexed and every NetHack wiki tool keeps answering from the previous tree.

---

## What Is Converted

Only namespace `0` (main articles) is converted; every file carries `namespace: article`. The `Source:`, `Forum:`, `Category:`, `Help:` and `NetHackWiki:` namespaces, talk and user pages, templates and files are skipped. Historical source code is covered by Overseer's own source corpora instead. Redirect pages are skipped.

Three page filters then skip articles, and every skipped title is listed in the report under its reason:

| Filter | Rule |
|---|---|
| Non-English | The title ends in `/<code>` for a code in `LANGUAGE_SUBPAGE_CODES` (`zh-CN`, `ko`, `ru`, …) — never a generic two-letter pattern, since `Source:NetHack 3.4.3/dat/hh` is English — or more than 20 % of the letters in the title and wikitext are outside Latin script. |
| Variant-only | The title ends in a parenthetical naming a variant (`Boojum (EvilHack)`); or it is a subpage whose last segment is a variant name (`Tourist quest/dNetHack`); or every category names a variant or starts with `Variant `; or every version banner it carries is a variant banner. Never skipped: articles in category *Variants* or *Defunct variants* (the variant overview pages), and articles whose title, a category or wikitext mentions GnollHack. |
| Non-game | The *Main Page*, or every category is in `NON_GAME_CATEGORIES` (*Bots*, *Community*, *Development*, *Games*, *Tournaments*, *Websites*, …). Never skipped: the titles in `NON_GAME_KEEP_TITLES` (*NetHack*, *Sortloot*, *Wizard mode*, …). *History* is a game category: release pages stay. |

Variant names are the fixed `VARIANT_NAMES` list, matched case-insensitively with no letter or digit on either side (`SLASH` matches `SLASH'EM`, not `Slashing weapons`). A **version banner** is a template whose body calls `{{version icon}}`; it is a variant banner when its `description` names a variant or contains the word "variant", and a NetHack banner otherwise. The classification is computed from the dump on every run and written to the report.

**Extending the filters.** The report's `candidates` section lists, without applying them, title suffixes and category prefixes on 5 or more kept articles that name no known variant and are not seen on NetHack-bannered articles, and kept articles without a NetHack banner whose first paragraph says they appear in a variant. Read it after each regeneration: a new variant goes into `VARIANT_NAMES`, a new non-game category into `NON_GAME_CATEGORIES`, a game-relevant exception into `NON_GAME_KEEP_TITLES`.

---

## Template Handling

The converter reads every page of the dump's Template namespace first (the *template map*: names normalized as MediaWiki does, redirects followed), then renders every template of an article, innermost first, so templates nested in table cells, infobox parameters or other templates are rendered too. Each template has exactly one outcome:

| Outcome | Examples | Result |
|---|---|---|
| Stat block | `{{monster}}`, `{{weapon}}`, `{{artifact weapon}}`, `{{level}}`, `{{randomvariable}}` | A `--- <Label> ---` block at the top of the article; image parameters (`tile`, `image`, `caption`, `graph`) are left out |
| Note | `{{refsrc}}` with `comment=`, `{{footnote}}` | ` (Note: …)` after the sentence; the citation's file and line are never written |
| Intentional drop | `{{refsrc}}` without a comment, `{{stub}}`, `{{todo}}`, `{{note}}` (a footnote anchor) | Nothing |
| Pointer | `{{main}}`, `{{redirects-here}}`, `{{distinguish}}` | `Main article: …`, `X redirects here. For Y, see Z.`, `Not to be confused with …` |
| Inline text | colors, `{{kbd}}`, `{{frac}}`, the *of* family, `{{monsymlink}}`, `{{sa}}`, `{{rn1}}`, `{{davg}}` | The displayed text |
| Block text | `{{message}}`, `{{encyclopedia}}`, `{{guidebook}}`, version banners | A quoted message with its explanation; `>` quotation lines; the banner's description |
| Transclusion | names matching `TRANSCLUDE_PATTERNS` (the `… skill table` templates) | The template body, expanded; a body that still uses a parser function falls back to *unknown* |
| Unknown | navigation boxes, parser functions, everything else | Dropped, and counted by name in the report |

Transclusion is an allowlist on purpose: the static templates nearest in shape to the skill tables are navigation boxes, whose expansion would add the same link list to every page that carries one and pollute search ranking. To transclude another template family, add a pattern to `TRANSCLUDE_PATTERNS`; to render a template some other way, add it to the matching handler in the script. Check the report's `dropped_templates` list after a regeneration for content templates worth handling.

Other conversions: `<math>` content is kept as inline code; preformatted text (`<pre>`, `<syntaxhighlight>`, `<source>`, `<replacecharsblock>` and space-indented lines) becomes a fenced code block with its spacing intact, and a `<replacecharsblock>` map loses its character-mapping legend; a table framing a map is written as the map and its caption; list items become `- ` and `1. ` lines; `<ref>` contents and magic words such as `__TOC__` are removed; and no line that is not a heading starts with `#`, because `MarkdownSectionExtractor` takes any such line for one.

---

## The Conversion Report

`_conversion_report.json` records the dump (path, size, modification time), the generation time, page counts and skipped counts by reason, every skipped title, the articles kept by an exception, the version-banner classification, the candidate lists, the number of tables and preformatted blocks, the transclusions, the dropped templates by name, and quality figures measured on the output:

- **Empty sections**: headings followed by a sibling or parent heading, or by the end of the file, with no content between.
- **Heading-shaped non-heading lines**: lines `MarkdownSectionExtractor` would take for headings beyond the article's own wikitext headings; expected to be 0.
- **Residue outside code**: template and link syntax, `''`, HTML tags, image file names, magic words and leftover placeholders, with example titles. Template and link syntax left over is usually malformed wikitext in the wiki itself, and an apostrophe pair can be the golem class glyph.
- **Largest file** and files over the 500 KB `MaxNetHackWikiFileSizeKB` limit.

Compare these figures with the previous report before converting into `NetHackWikiPath`.
