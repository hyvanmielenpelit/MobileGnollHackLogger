#!/usr/bin/env python3
"""
convert_nethackwiki_dump_md.py

Converts a MediaWiki XML dump of NetHack Wiki into plain text files
suitable for Lucene.Net indexing by the Overseer NetHackWikiService.

Usage:
    python convert_nethackwiki_dump_md.py <input_xml> <output_dir> [--test-titles "Title1,Title2"]

Dependencies:
    pip install mwparserfromhell
"""

import argparse
import html
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

import mwparserfromhell

# Namespaces to include (based on analysis of the dump)
INCLUDED_NAMESPACES = {
    0: "article",
    4: "nethackwiki",
    12: "help",
    14: "category",
    100: "source",
    110: "forum",
}

# Namespace prefixes to strip from titles for cleaner filenames
NS_PREFIXES = {
    4: "NetHackWiki:",
    12: "Help:",
    14: "Category:",
    100: "Source:",
    110: "Forum:",
}

# Known infobox/stat templates and their human-readable labels
KNOWN_STAT_TEMPLATES = {
    "monster": "Monster Stats",
    "item": "Item Stats",
    "weapon": "Weapon Stats",
    "armor": "Armor Stats",
    "tool": "Tool Stats",
    "ring": "Ring Stats",
    "amulet": "Amulet Stats",
    "scroll": "Scroll Stats",
    "potion": "Potion Stats",
    "wand": "Wand Stats",
    "spellbook": "Spellbook Stats",
    "artifact": "Artifact Stats",
    "spell": "Spell Stats",
    "food": "Food Stats",
    "gem": "Gem Stats",
    "coin": "Coin Stats",
}

# Templates to skip entirely (navigation, formatting, metadata)
SKIP_TEMPLATES = {
    "languages", "lang", "stub", "merge", "cleanup", "delete",
    "disambiguation", "main", "see also", "hatnote", "for",
    "clear", "clr", "br", "nbsp", "ndash", "mdash",
    "columns", "col-begin", "col-end", "col-break",
    "reflist", "references", "refn", "efn", "notelist",
    "toc", "notoc", "compact toc",
    "ngpl",  # NetHack General Public License boilerplate
}

# Windows reserved device names that cannot be used as filenames
WINDOWS_RESERVED_NAMES = {
    "CON", "PRN", "AUX", "NUL",
    "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
}


def sanitize_filename(title):
    """Convert a wiki title to a safe filename."""
    # Replace characters not allowed in filenames
    name = title.replace("/", "__").replace("\\", "__")
    name = name.replace(":", "__").replace("*", "_")
    name = name.replace("?", "_").replace('"', "_")
    name = name.replace("<", "_").replace(">", "_")
    name = name.replace("|", "_")
    # Remove control characters
    name = re.sub(r"[\x00-\x1f]", "", name)
    # Collapse multiple underscores
    name = re.sub(r"__+", "__", name)
    # Strip leading/trailing whitespace and trailing dots (Windows filename safety)
    name = name.strip().rstrip(". ")
    # Trim length (Windows MAX_PATH considerations)
    if len(name) > 200:
        name = name[:200].rstrip(". ")
    if not name:
        name = "unnamed"
    # Guard against Windows reserved device names (e.g. CON, NUL, AUX, PRN)
    if name.upper() in WINDOWS_RESERVED_NAMES:
        name = f"{name}_"
    return name + ".md"


def format_template_params(template):
    """Extract key-value pairs from a template, returning readable text."""
    lines = []
    for param in template.params:
        name = str(param.name).strip()
        value = str(param.value).strip()
        if not name or not value:
            continue
        # Clean wikitext from param values
        try:
            # Handle common inline formatting templates before strip_code
            val_clean = value
            val_clean = re.sub(r"\{\{s?frac\|(\d+)\|(\d+)\|(\d+)\}\}", r"\1 \2/\3", val_clean, flags=re.IGNORECASE)
            val_clean = re.sub(r"\{\{s?frac\|(\d+)\|(\d+)\}\}", r"\1/\2", val_clean, flags=re.IGNORECASE)
            val_clean = re.sub(r"\{\{s?frac\|(\d+)\}\}", r"1/\1", val_clean, flags=re.IGNORECASE)
            val_clean = re.sub(r"\{\{(?:monsym|mcsl)\|([^}]+)\}\}", r"'\1'", val_clean, flags=re.IGNORECASE)
            val_clean = re.sub(r"\{\{(?:kbd|key)\|([^}]+)\}\}", r"[\1]", val_clean, flags=re.IGNORECASE)
            value_parsed = mwparserfromhell.parse(val_clean)
            # Extract text from wikilinks in the value
            for wl in value_parsed.filter_wikilinks():
                display = str(wl.text) if wl.text else str(wl.title)
                value_parsed.replace(wl, display)
            # Strip remaining markup
            value = value_parsed.strip_code().strip()
        except Exception:
            pass
        if value:
            # Capitalize first letter of param name for readability
            display_name = name.replace("_", " ").replace("-", " ")
            display_name = display_name[0].upper() + display_name[1:] if display_name else display_name
            lines.append(f"{display_name}: {value}")
    return "\n".join(lines)


def convert_wikitext_to_plaintext(wikitext, title=""):
    """Convert MediaWiki wikitext to clean plain text with preserved structure."""
    if not wikitext:
        return [], ""

    # Decode HTML entities first
    text = html.unescape(wikitext)

    try:
        parsed = mwparserfromhell.parse(text)
    except Exception as e:
        # If parsing fails, do basic regex cleanup
        return _fallback_cleanup(text)

    stat_blocks = []
    templates_to_remove = []

    # Process templates
    for template in parsed.filter_templates(recursive=False):
        tname = str(template.name).strip().lower()

        # Check if it's a known stat/infobox template
        if tname in KNOWN_STAT_TEMPLATES:
            label = KNOWN_STAT_TEMPLATES[tname]
            params_text = format_template_params(template)
            if params_text:
                stat_blocks.append(f"--- {label} ---\n{params_text}")
            templates_to_remove.append(template)

        # Skip navigation/formatting templates
        elif tname in SKIP_TEMPLATES:
            templates_to_remove.append(template)

        # Handle inline formatting templates
        elif tname in ("kbd", "key"):
            # {{kbd|E}} -> [E]
            try:
                val = str(template.params[0].value).strip() if template.params else ""
                parsed.replace(template, f"[{val}]")
            except Exception:
                templates_to_remove.append(template)

        elif tname in ("monsym", "mcsl"):
            # {{monsym|cockatrice}} -> (symbol)
            try:
                val = str(template.params[0].value).strip() if template.params else ""
                parsed.replace(template, f"'{val}'")
            except Exception:
                templates_to_remove.append(template)

        elif tname == "frac" or tname == "sfrac":
            # {{frac|3}} -> 1/3, {{frac|1|3}} -> 1/3, {{frac|1|2|3}} -> 1 2/3
            try:
                params = [str(p.value).strip() for p in template.params]
                if len(params) == 1:
                    parsed.replace(template, f"1/{params[0]}")
                elif len(params) == 2:
                    parsed.replace(template, f"{params[0]}/{params[1]}")
                elif len(params) == 3:
                    parsed.replace(template, f"{params[0]} {params[1]}/{params[2]}")
                else:
                    templates_to_remove.append(template)
            except Exception:
                templates_to_remove.append(template)

        elif tname == "upcoming":
            # {{upcoming|NetHack 3.7.0|text}} -> [Upcoming in NetHack 3.7.0: text]
            try:
                params = [str(p.value).strip() for p in template.params]
                if len(params) >= 2:
                    version = params[0]
                    content = params[1]
                    try:
                        content_parsed = mwparserfromhell.parse(content)
                        content = content_parsed.strip_code().strip()
                    except Exception:
                        pass
                    parsed.replace(template, f"[Upcoming in {version}: {content}]")
                else:
                    templates_to_remove.append(template)
            except Exception:
                templates_to_remove.append(template)

        elif tname in ("refsrc", "sourcecode", "commit"):
            # Source code references — just remove them, they add noise
            templates_to_remove.append(template)

        elif tname == "wikipedia":
            # {{Wikipedia|article}} -> (See Wikipedia: article)
            try:
                val = str(template.params[0].value).strip() if template.params else ""
                parsed.replace(template, f"(See Wikipedia: {val})")
            except Exception:
                templates_to_remove.append(template)

        elif tname == "fa":
            # Featured article template — skip
            templates_to_remove.append(template)

        elif tname in ("white", "cyan", "brown", "red", "green", "blue",
                        "yellow", "magenta", "gray", "grey", "bright",
                        "darkgray", "lightgray", "orange", "black"):
            # Color templates: {{cyan|"}} -> "
            try:
                val = str(template.params[0].value).strip() if template.params else ""
                parsed.replace(template, val)
            except Exception:
                templates_to_remove.append(template)

        elif tname == "attributes":
            # {{attributes|...}} -> format as key-value
            params_text = format_template_params(template)
            if params_text:
                stat_blocks.append(f"--- Attributes ---\n{params_text}")
            templates_to_remove.append(template)

        elif tname == "roles":
            # Template listing roles — just replace with text
            parsed.replace(template, "Archeologist, Barbarian, Caveman, Healer, Knight, Monk, Priest, Ranger, Rogue, Samurai, Tourist, Valkyrie, Wizard")

    # Remove marked templates
    for t in templates_to_remove:
        try:
            parsed.remove(t)
        except ValueError:
            pass

    # Convert wikilinks to plain text
    for wl in parsed.filter_wikilinks():
        display = str(wl.text) if wl.text else str(wl.title)
        # Clean up display text
        display = display.strip()
        # Remove Image/File links entirely
        title_str = str(wl.title).strip()
        if title_str.startswith(("Image:", "File:", "Category:")):
            try:
                parsed.remove(wl)
            except ValueError:
                pass
            continue
        try:
            parsed.replace(wl, display)
        except ValueError:
            pass

    # Convert external links [url text] -> text
    for el in parsed.filter_external_links():
        display = str(el.title) if el.title else str(el.url)
        try:
            parsed.replace(el, display.strip())
        except ValueError:
            pass

    # Convert section headings BEFORE strip_code() removes them.
    # Use unique placeholders that won't be stripped by mwparserfromhell.
    raw = str(parsed)

    # Convert headings: ====== H6 ====== -> HEADING6_MARKER Title, etc.
    # Process from deepest to shallowest to avoid partial matches
    raw = re.sub(r"^={6}\s*(.*?)\s*={6}", r"XHEADING6X \1", raw, flags=re.MULTILINE)
    raw = re.sub(r"^={5}\s*(.*?)\s*={5}", r"XHEADING5X \1", raw, flags=re.MULTILINE)
    raw = re.sub(r"^={4}\s*(.*?)\s*={4}", r"XHEADING4X \1", raw, flags=re.MULTILINE)
    raw = re.sub(r"^={3}\s*(.*?)\s*={3}", r"XHEADING3X \1", raw, flags=re.MULTILINE)
    raw = re.sub(r"^={2}\s*(.*?)\s*={2}", r"XHEADING2X \1", raw, flags=re.MULTILINE)
    raw = re.sub(r"^={1}\s*(.*?)\s*={1}", r"XHEADING1X \1", raw, flags=re.MULTILINE)

    # Re-parse after heading conversion, then strip remaining markup
    try:
        parsed2 = mwparserfromhell.parse(raw)
        # Remove any remaining wikilinks
        for wl in parsed2.filter_wikilinks():
            display = str(wl.text) if wl.text else str(wl.title)
            display = display.strip()
            title_str = str(wl.title).strip()
            if title_str.startswith(("Image:", "File:", "Category:")):
                try:
                    parsed2.remove(wl)
                except ValueError:
                    pass
                continue
            try:
                parsed2.replace(wl, display)
            except ValueError:
                pass
        for el in parsed2.filter_external_links():
            display = str(el.title) if el.title else str(el.url)
            try:
                parsed2.replace(el, display.strip())
            except ValueError:
                pass
        result = parsed2.strip_code()
    except Exception:
        result = raw

    # Convert heading placeholders to Markdown headings
    result = result.replace("XHEADING6X ", "###### ")
    result = result.replace("XHEADING5X ", "##### ")
    result = result.replace("XHEADING4X ", "#### ")
    result = result.replace("XHEADING3X ", "### ")
    result = result.replace("XHEADING2X ", "## ")
    result = result.replace("XHEADING1X ", "# ")

    # Clean up HTML remnants
    result = re.sub(r"<ref[^>]*>.*?</ref>", "", result, flags=re.DOTALL)
    result = re.sub(r"<ref[^/]*/>", "", result)
    result = re.sub(r"</?(?:div|span|ul|ol|li|table|tr|td|th|br|hr|p|pre|code|tt|nowiki|gallery|center|small|big|sup|sub|s|u|em|strong|b|i|font|blockquote|includeonly|noinclude|onlyinclude|section)[^>]*>", "", result, flags=re.IGNORECASE)
    result = re.sub(r"<!--.*?-->", "", result, flags=re.DOTALL)

    # Clean up HTML span tags with IDs (common in Source pages)
    result = re.sub(r'<span[^>]*>', "", result, flags=re.IGNORECASE)
    result = re.sub(r'</span>', "", result, flags=re.IGNORECASE)

    # Clean up wiki table markup
    result = re.sub(r"^\{\|.*$", "", result, flags=re.MULTILINE)
    result = re.sub(r"^\|\}.*$", "", result, flags=re.MULTILINE)
    result = re.sub(r"^\|-.*$", "", result, flags=re.MULTILINE)
    result = re.sub(r"\s*(?:\|\||!!)\s*", " | ", result)
    result = re.sub(r"^\|.*$", lambda m: m.group(0).lstrip("|").strip(), result, flags=re.MULTILINE)
    result = re.sub(r"^!.*$", lambda m: m.group(0).lstrip("!").strip(), result, flags=re.MULTILINE)
    result = re.sub(r'^(?:align|valign|colspan|rowspan|style|class|width|height|bgcolor)=["\'][^"\']*["\']\s*\|\s*', '', result, flags=re.MULTILINE | re.IGNORECASE)

    # Decode remaining HTML entities
    result = html.unescape(result)

    # Normalize whitespace
    result = re.sub(r"\n{3,}", "\n\n", result)
    result = re.sub(r"[ \t]+", " ", result)
    result = re.sub(r" +\n", "\n", result)

    return stat_blocks, result.strip()


def _fallback_cleanup(text):
    """Basic regex cleanup when mwparserfromhell fails."""
    # Strip templates
    text = re.sub(r"\{\{[^}]*\}\}", "", text)
    # Strip wikilinks, keep display text
    text = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", r"\1", text)
    # Strip external links
    text = re.sub(r"\[https?://\S+\s+([^\]]+)\]", r"\1", text)
    text = re.sub(r"\[https?://\S+\]", "", text)
    # Strip HTML
    text = re.sub(r"<[^>]+>", "", text)
    # Decode entities
    text = html.unescape(text)
    # Normalize whitespace
    text = re.sub(r"\n{3,}", "\n\n", text)
    return [], text.strip()


def process_dump(input_xml, output_dir, test_titles=None):
    """Process the MediaWiki XML dump and write text files."""
    os.makedirs(output_dir, exist_ok=True)

    # MediaWiki XML namespace
    ns = "{http://www.mediawiki.org/xml/export-0.11/}"

    index = {}  # filename -> {title, namespace}
    used_filenames = set()  # Track filenames (lowercased) to handle case collisions
    processed = 0
    skipped = 0
    redirects = 0
    errors = 0

    print(f"Processing: {input_xml}")
    print(f"Output to:  {output_dir}")
    if test_titles:
        print(f"Test mode:  only processing titles matching: {test_titles}")
    print()

    # Use iterparse for memory-efficient streaming
    context = ET.iterparse(input_xml, events=("end",))

    for event, elem in context:
        tag = elem.tag
        if not (tag.endswith("page") or tag == "page"):
            continue

        # Extract namespace URI if present, e.g. "{http://www.mediawiki.org/xml/export-0.11/}"
        ns = tag[:tag.rfind("}") + 1] if "}" in tag else ""

        title_elem = elem.find(f"{ns}title")
        ns_elem = elem.find(f"{ns}ns")
        revision = elem.find(f"{ns}revision")

        if title_elem is None or ns_elem is None or revision is None:
            elem.clear()
            continue

        title = title_elem.text or ""
        page_ns = int(ns_elem.text or "-1")

        # Filter namespaces
        if page_ns not in INCLUDED_NAMESPACES:
            skipped += 1
            elem.clear()
            continue

        # Filter by test titles if specified
        if test_titles:
            # Match against both full title and title without namespace prefix
            clean_title = title
            prefix = NS_PREFIXES.get(page_ns, "")
            if prefix and clean_title.startswith(prefix):
                clean_title = clean_title[len(prefix):]

            if not any(t.lower() in title.lower() or t.lower() in clean_title.lower()
                       for t in test_titles):
                elem.clear()
                continue

        text_elem = revision.find(f"{ns}text")
        wikitext = text_elem.text if text_elem is not None else ""

        if not wikitext:
            elem.clear()
            continue

        # Skip redirect pages — they contain no useful content
        is_redirect = (
            elem.find(f"{ns}redirect") is not None
            or bool(re.match(r"(?i)^\s*#\s*redirect", wikitext))
        )
        if is_redirect:
            redirects += 1
            elem.clear()
            continue

        ns_label = INCLUDED_NAMESPACES[page_ns]

        try:
            stat_blocks, content = convert_wikitext_to_plaintext(wikitext, title)

            # Auto-generate summary from first non-empty sentence of content
            summary = ""
            if content:
                # Find first sentence (up to first period followed by space or end)
                for line in content.split("\n"):
                    line = line.strip()
                    if not line or line.startswith("#") or line.startswith("---"):
                        continue
                    # Skip meta-lines that aren't real article content
                    if line.startswith("(See Wikipedia:"):
                        continue
                    if line.startswith("Parts of this"):
                        continue
                    if len(line) < 20:
                        continue
                    # Take first sentence
                    dot_pos = line.find(". ")
                    if dot_pos > 0:
                        summary = line[:dot_pos + 1]
                    else:
                        summary = line[:200]
                    break

            # Escape YAML special chars in title and summary
            clean_title_yaml = title.replace("\r", "").replace("\n", " ").replace("\\", "\\\\").replace('"', '\\"')
            clean_summary_yaml = summary.replace("\r", "").replace("\n", " ").replace("\\", "\\\\").replace('"', '\\"')

            # Build output with YAML frontmatter
            output_lines = [
                "---",
                f"title: \"{clean_title_yaml}\"",
                f"namespace: {ns_label}",
                f"summary: \"{clean_summary_yaml}\"",
                "---",
                "",
            ]

            if stat_blocks:
                for block in stat_blocks:
                    output_lines.append(block)
                    output_lines.append("")

            output_lines.append(content)

            output_text = "\n".join(output_lines)

            # Write file with collision handling
            filename = sanitize_filename(title)
            filename_lower = filename.lower()
            if filename_lower in used_filenames:
                # Append numeric suffix to avoid case-insensitive collision
                base, ext = os.path.splitext(filename)
                suffix = 2
                while f"{base}_{suffix}{ext}".lower() in used_filenames:
                    suffix += 1
                filename = f"{base}_{suffix}{ext}"
                filename_lower = filename.lower()
            used_filenames.add(filename_lower)

            filepath = os.path.join(output_dir, filename)
            with open(filepath, "w", encoding="utf-8") as f:
                f.write(output_text)

            index[filename] = {"title": title, "namespace": ns_label}
            processed += 1

            if processed % 1000 == 0:
                print(f"  Processed {processed} pages...")

        except Exception as e:
            errors += 1
            print(f"  ERROR processing '{title}': {e}", file=sys.stderr)

        # Free memory
        elem.clear()

    # Write index
    index_path = os.path.join(output_dir, "_index.json")
    with open(index_path, "w", encoding="utf-8") as f:
        json.dump(index, f, indent=2, ensure_ascii=False)

    print()
    print(f"Done! Processed: {processed}, Skipped (wrong ns): {skipped}, "
          f"Redirects: {redirects}, Errors: {errors}")
    print(f"Index written to: {index_path}")

    return processed, skipped, errors


def main():
    parser = argparse.ArgumentParser(
        description="Convert NetHack Wiki XML dump to plain text files"
    )
    parser.add_argument("input_xml", help="Path to the MediaWiki XML dump file")
    parser.add_argument("output_dir", help="Output directory for text files")
    parser.add_argument(
        "--test-titles",
        help="Comma-separated list of title substrings to process (test mode)",
        default=None,
    )

    args = parser.parse_args()

    test_titles = None
    if args.test_titles:
        test_titles = [t.strip() for t in args.test_titles.split(",")]

    process_dump(args.input_xml, args.output_dir, test_titles)


if __name__ == "__main__":
    main()
