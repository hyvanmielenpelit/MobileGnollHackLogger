# NetHack Wiki Dump Converter

This directory contains the Python conversion script used to convert a MediaWiki XML dump of the NetHack Wiki (from [nethackwiki.com](https://nethackwiki.com/)) into clean plain text Markdown files for Overseer's local Lucene.Net search index (`NetHackWikiService`).

---

## Overview

NetHackWiki is protected against automated HTTP scrapers by Cloudflare WAF. Overseer uses a local offline copy of the wiki instead. This script parses the MediaWiki XML dump (`*-pages-articles.xml` / `nethackwiki_current.xml`) and outputs:
- One `.md` file per article/page with YAML frontmatter (`title`, `namespace`, `summary`).
- Structured key-value stat blocks (e.g., `--- Monster Stats ---`, `--- Item Stats ---`).
- Markdown headings (`##`, `###`) for section extraction.
- A generated `_index.json` mapping filenames to titles and namespaces.
- Skips redirect pages and unneeded namespaces (talk pages, users, templates, images).

---

## Prerequisites

1. **Python 3.10+**
2. **`mwparserfromhell` library**:
   ```bash
   pip install mwparserfromhell
   ```

---

## How to Regenerate NetHack Wiki Files

### Step 1: Obtain a NetHack Wiki XML Dump
Download the latest MediaWiki XML dump (current pages export, e.g. `nethackwiki_current.xml` or `nethackwiki-latest-pages-articles.xml`).

### Step 2: Run the Conversion Script

Run the script specifying the input XML file and target output directory:

```bash
python convert_nethackwiki_dump_md.py <path_to_input_xml> <output_directory>
```

#### Example:
```bash
python convert_nethackwiki_dump_md.py c:\hmp\plans\nethackwiki_current.xml c:\hmp\nethackwiki
```

### Step 3 (Optional): Test Mode
To test conversion on a subset of articles without processing the entire dump:

```bash
python convert_nethackwiki_dump_md.py <path_to_input_xml> <output_directory> --test-titles "Cockatrice,Elbereth,Wand of digging"
```

---

## Output Details

- **Output Directory**: The target directory (e.g., `c:\hmp\nethackwiki`) will contain ~9,300+ `.md` files and `_index.json`.
- **Target Configuration**: Make sure `NetHackWikiPath` in Overseer's `appsettings.json` points to this output directory:
  ```json
  "NetHackWikiPath": "c:\\hmp\\nethackwiki"
  ```
- **Live Re-indexing**: When Overseer starts (or on its 10-minute periodic timer), `NetHackWikiService` scans this directory and indexes all markdown files into Lucene.Net RAM index.

---

## Namespaces Processed

| Namespace ID | Label | Description |
|---|---|---|
| `0` | `article` | Main game articles (monsters, items, mechanics, strategy) |
| `4` | `nethackwiki` | NetHackWiki project pages |
| `12` | `help` | Wiki help pages |
| `14` | `category` | Category description pages |
| `100` | `source` | Annotated NetHack C source code pages |
| `110` | `forum` | Wiki discussion topics |
