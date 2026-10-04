#!/usr/bin/env python3
"""
convert_nethackwiki_dump_md.py

Converts a MediaWiki XML dump of NetHack Wiki into plain text files
suitable for Lucene.Net indexing by the Overseer NetHackWikiService.

Only English main-namespace articles about NetHack itself are written:
non-English translations, variant-only articles and articles without game
content are skipped and listed in _conversion_report.json.

Usage:
    python convert_nethackwiki_dump_md.py <input_xml> <output_dir> [--test-titles "Title1,Title2"]

Dependencies:
    pip install mwparserfromhell
"""

import argparse
import ast
import datetime
import html
import json
import operator
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

import mwparserfromhell
from mwparserfromhell.nodes import (
    Argument, ExternalLink, Heading, Tag, Template, Text, Wikilink,
)

# Namespaces to include
INCLUDED_NAMESPACES = {
    0: "article",
}

TEMPLATE_NAMESPACE = 10

# Namespaces the converter wrote before it was limited to articles; counted in the report
FORMERLY_CONVERTED_NAMESPACES = {
    4: "nethackwiki",
    12: "help",
    14: "category",
    100: "source",
    110: "forum",
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

# Further infobox templates rendered as stat blocks
EXTRA_STAT_TEMPLATES = {
    "attributes": "Attributes",
    "artifact weapon": "Artifact Weapon Stats",
    "comestible": "Comestible Stats",
    "level": "Level Stats",
    "trap": "Trap Stats",
    "dnethack weapon": "dNetHack Weapon Stats",
    "dnethack armor": "dNetHack Armor Stats",
    "randomvariable": "Random Variable",
}

STAT_TEMPLATES = {**KNOWN_STAT_TEMPLATES, **EXTRA_STAT_TEMPLATES}

# Infobox parameters that only name images
STAT_IMAGE_PARAMS = {"tile", "image", "caption", "graph"}

# Templates to skip entirely (navigation, formatting, metadata)
SKIP_TEMPLATES = {
    "languages", "lang", "stub", "merge", "cleanup", "delete",
    "disambiguation", "hatnote", "for",
    "clear", "clr", "br", "nbsp", "ndash", "mdash",
    "columns", "col-begin", "col-end", "col-break",
    "reflist", "references", "notelist",
    "toc", "notoc", "compact toc",
    "ngpl",  # NetHack General Public License boilerplate
}

# Templates dropped on purpose; not counted as losses
INTENTIONAL_DROP_TEMPLATES = SKIP_TEMPLATES | {
    "reffunc", "sourcecode", "commit", "fa", "featured", "todo", "tocright",
    "disambig", "forumheader", "unsigned", "anchor",
    "dod", "cwi", "verbatim spoiler",  # license banners
    "hl2",  # table cell style
    "note",  # footnote anchor; the note text is outside the template
}

# Templates whose text becomes a "(Note: ...)" after the sentence
NOTE_TEMPLATES = {"refsrc", "footnote", "efn", "refn"}

# Templates whose body is spliced into the article and expanded
TRANSCLUDE_PATTERNS = [
    re.compile(r"^.+ skill table( \(.+\))?$"),
]

COLOR_TEMPLATES = {
    "white", "cyan", "brown", "red", "green", "blue", "yellow", "magenta",
    "gray", "grey", "bright", "darkgray", "lightgray", "orange", "black",
    "purple", "brightblue", "brightcyan", "brightgreen", "brightmagenta",
    "brightred", "metal",
}

# Item-class link templates: name -> item class (None: class is the first parameter)
OF_TEMPLATES = {
    "of": None,
    "spellbook of": "spellbook",
    "potion of": "potion",
    "scroll of": "scroll",
    "wand of": "wand",
    "ring of": "ring",
    "amulet of": "amulet",
    "+ of": "spellbook",
    "spell of": "spellbook",
    "! of": "potion",
    "? of": "scroll",
    "= of": "ring",
    '" of': "amulet",
    "/ of": "wand",
}

ALL_ROLES = ("Archeologist, Barbarian, Caveman, Healer, Knight, Monk, Priest, "
             "Ranger, Rogue, Samurai, Tourist, Valkyrie, Wizard")

# Translation subpage suffixes; never a generic two-letter pattern
LANGUAGE_SUBPAGE_CODES = {
    "zh-CN", "zh", "zh-TW", "ko", "ja", "ru", "de", "fr", "es", "pt", "it",
    "pl", "nl", "fi", "sv",
}
NON_LATIN_LETTER_SHARE = 0.20

VARIANT_NAMES = [
    "AceHack", "CrecelleHack", "dNetHack", "DynaHack", "EvilHack", "FIQHack",
    "GruntHack", "Hack'EM", "NetHack 4", "NetHack Fourk", "NetHack Plus",
    "NetHack brass", "NetHack--", "NetHack: The Next Generation",
    "Nethack Extended", "NitroHack", "notdNetHack", "notnotdNetHack", "Pathos",
    "SLASH", "SLASH 6", "SLASH'EM", "Slash'EM Extended", "SlashTHEM",
    "SliceHack", "SpliceHack", "SpliceHack-Rewrite", "SporkHack", "UnNetHack",
    "UnNetHackPlus", "Wizard Patch", "xNetHack", "Lethe patch", "Convict patch",
    "Pirate patch", "ZAPM",
]
VARIANT_RE = re.compile(
    r"(?<![A-Za-z0-9])(?:"
    + "|".join(re.escape(n) for n in sorted(VARIANT_NAMES, key=len, reverse=True))
    + r")(?![A-Za-z0-9])",
    re.IGNORECASE,
)
VARIANT_NAMES_LOWER = {n.lower() for n in VARIANT_NAMES}

# Categories of the variant overview pages, which are always kept
VARIANT_OVERVIEW_CATEGORIES = {"variants", "defunct variants"}

NON_GAME_CATEGORIES = {
    "bots", "community", "development", "games", "junethack", "literature",
    "nethackwiki", "notable people", "operating systems", "patches", "ports",
    "public servers", "roguelikes", "the november nethack tournament",
    "tournaments", "url list", "utilities", "websites",
}
NON_GAME_KEEP_TITLES = {
    "NetHack", "Acronyms", "Abbreviations", "Sortloot", "Statuscolors",
    "Persistent level", "Wizard mode",
}
MAIN_PAGE_TITLE = "Main Page"

# Title-suffix and category qualifiers known to describe vanilla concepts;
# they are left out of the variant candidate lists in the report
VANILLA_QUALIFIERS = {
    "monster class", "starting race", "disambiguation", "monster", "monster attribute",
    "skill", "trap", "spell", "role", "race", "item", "object", "tool", "weapon",
    "armor", "food", "corpse", "artifact", "property", "command", "option", "level",
    "dungeon feature", "terrain", "function", "status", "intrinsic", "quest",
    "special level", "room", "branch", "gem", "rock", "potion", "scroll", "wand",
    "ring", "amulet", "spellbook", "attack", "damage", "god", "nethack",
}

MIN_CANDIDATE_ARTICLES = 5

MAX_EXPANSION_DEPTH = 6
MAX_REDIRECT_HOPS = 5
MAX_COLSPAN = 40
MAX_REPORT_EXAMPLES = 10
MAX_FILE_BYTES = 500 * 1024

# Characters standing in for entity-encoded wikitext syntax until the end of conversion
PROTECTED_ENTITIES = {
    "&#124;": "", "&vert;": "", "&verbar;": "", "&#x7c;": "", "&#X7C;": "",
    "&#61;": "", "&equals;": "",
    "&#123;": "", "&lbrace;": "",
    "&#125;": "", "&rbrace;": "",
}
PROTECTED_RESTORE = {"": "|", "": "=", "": "{", "": "}", "": "[", "": "]",
                     "": "'"}
# Symbols a template renders as plain characters, kept from joining surrounding markup
SYMBOL_PROTECT = {"|": "", "=": "", "{": "", "}": "", "[": "", "]": "",
                  "'": ""}

# Marks a removed template, so the line it started does not look preformatted
REMOVED_MARK = "\x03"
# Marks a heading-shaped line that is not a heading
GUARD_MARK = "\x01"

PRE_TAG_RE = re.compile(
    r"<(pre|syntaxhighlight|source|replacecharsblock)(\s[^>]*)?>(.*?)</\1\s*>",
    re.IGNORECASE | re.DOTALL,
)
MATH_RE = re.compile(r"<math(\s[^>]*)?>(.*?)</math\s*>", re.IGNORECASE | re.DOTALL)
MAGIC_WORD_RE = re.compile(r"__(?!FILE__|LINE__|DATE__|TIME__|STDC__|FUNCTION__|GNUC__)[A-Z]+__")
HTML_TAG_NAMES = (
    "div|span|ul|ol|li|table|tr|td|th|br|hr|p|pre|code|tt|nowiki|gallery|center|small|big|"
    "sup|sub|s|u|em|strong|b|i|font|blockquote|includeonly|noinclude|onlyinclude|section|"
    "abbr|cite|var|del|ins|kbd|samp|dl|dt|dd|caption|ref|references|math|poem|source|"
    "syntaxhighlight|replacecharsblock|strike|q"
)
HTML_TAG_RE = re.compile(r"</?(?:" + HTML_TAG_NAMES + r")\b[^<>]*>", re.IGNORECASE)
FILE_LINK_TITLE_RE = re.compile(r"^\s*(?:image|file|category)\s*:", re.IGNORECASE)
PLACEHOLDER_RE = re.compile(r"X(?:TABLE|PRE|MATH|HEADING|LISTINDENT)\d+X|[\x01\x02\x03]")
HEADING_LINE_RE = re.compile(r"^(#+)\s+")
CATEGORY_LINK_RE = re.compile(r"\[\[\s*category\s*:\s*([^|\]]+)", re.IGNORECASE)
TEMPLATE_NAME_RE = re.compile(r"\{\{\s*([^|{}<>\[\]\n]+?)\s*(?=\||\}\})")
PARENTHETICAL_SUFFIX_RE = re.compile(r"^(.*\S)\s*\(([^()]+)\)\s*$")

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


def unique_filename(title, used_filenames):
    """Sanitized filename for a title, suffixed to avoid case-insensitive collisions."""
    filename = sanitize_filename(title)
    if filename.lower() in used_filenames:
        base, ext = os.path.splitext(filename)
        suffix = 2
        while f"{base}_{suffix}{ext}".lower() in used_filenames:
            suffix += 1
        filename = f"{base}_{suffix}{ext}"
    used_filenames.add(filename.lower())
    return filename


# ---------------------------------------------------------------------------
# Template map
# ---------------------------------------------------------------------------

def normalize_template_name(name):
    """Normalize a template name as MediaWiki does: no prefix, '_' = space, first letter uppercase."""
    name = str(name).replace("_", " ")
    name = re.sub(r"\s+", " ", name).strip()
    if name.lower().startswith("template:"):
        name = name[len("template:"):].strip()
    if name.startswith(":"):
        name = name[1:].strip()
    return name[:1].upper() + name[1:]


def transclusion_text(raw):
    """The part of a template page that is transcluded."""
    text = re.sub(r"<!--.*?(?:-->|$)", "", raw or "", flags=re.DOTALL)
    if re.search(r"<onlyinclude>", text, re.IGNORECASE):
        text = "".join(re.findall(r"<onlyinclude>(.*?)(?:</onlyinclude>|$)", text,
                                  flags=re.IGNORECASE | re.DOTALL))
    text = re.sub(r"<noinclude>.*?(?:</noinclude>|$)", "", text, flags=re.IGNORECASE | re.DOTALL)
    text = re.sub(r"</?includeonly\s*>", "", text, flags=re.IGNORECASE)
    return text


class TemplateMap:
    """Template namespace pages of the dump, by normalized name."""

    def __init__(self, texts=None, redirects=None):
        self._texts = {}
        self._redirects = {}
        self._bodies = {}
        self._banners = {}
        for name, text in (texts or {}).items():
            key = normalize_template_name(name)
            self._texts[key] = text or ""
            m = re.match(r"(?i)^\s*#\s*redirect\s*:?\s*\[\[([^\]|]+)", text or "")
            if m:
                self._redirects[key] = normalize_template_name(m.group(1))
        for name, target in (redirects or {}).items():
            self._redirects[normalize_template_name(name)] = normalize_template_name(target)

    def __len__(self):
        return len(self._texts)

    def names(self):
        return list(self._texts)

    def resolve(self, name):
        key = normalize_template_name(name)
        for _ in range(MAX_REDIRECT_HOPS):
            target = self._redirects.get(key)
            if not target or target == key:
                break
            key = target
        return key

    def exists(self, name):
        return self.resolve(name) in self._texts

    def body(self, name):
        key = self.resolve(name)
        if key not in self._texts:
            return None
        if key not in self._bodies:
            self._bodies[key] = transclusion_text(self._texts[key])
        return self._bodies[key]

    def banner(self, name):
        """(kind, description) for a version-banner template, kind 'variant' or 'nethack'; else None."""
        key = self.resolve(name)
        if key in self._banners:
            return self._banners[key]
        result = None
        body = self.body(key)
        if body and re.search(r"\{\{\s*[Vv]ersion[ _]icon", body):
            try:
                for t in mwparserfromhell.parse(body).filter_templates(recursive=False):
                    if normalize_template_name(t.name).lower() != "version icon":
                        continue
                    description = str(t.get("description").value).strip() if t.has("description") else ""
                    is_variant = bool(VARIANT_RE.search(description)
                                      or re.search(r"(?i)\bvariants?\b", description))
                    result = ("variant" if is_variant else "nethack", description)
                    break
            except Exception:
                result = None
        self._banners[key] = result
        return result

    def banner_classification(self):
        """Every version-banner template, by kind."""
        out = {"variant": [], "nethack": []}
        for key in sorted(self._texts):
            if key in self._redirects:
                continue
            b = self.banner(key)
            if b:
                out[b[0]].append(key)
        return out


# ---------------------------------------------------------------------------
# Page filters
# ---------------------------------------------------------------------------

def page_categories(wikitext):
    """Categories the page's wikitext assigns directly."""
    cats = []
    for m in CATEGORY_LINK_RE.finditer(wikitext or ""):
        name = re.sub(r"\s+", " ", m.group(1).replace("_", " ")).strip()
        if name:
            name = name[:1].upper() + name[1:]
            if name not in cats:
                cats.append(name)
    return cats


def page_template_names(wikitext):
    """Names of the templates a page's wikitext calls (approximate, without parsing)."""
    return {m.group(1) for m in TEMPLATE_NAME_RE.finditer(wikitext or "") if not m.group(1).startswith("#")}


def is_non_english(title, wikitext):
    if "/" in title and title.rsplit("/", 1)[1] in LANGUAGE_SUBPAGE_CODES:
        return True
    letters = [c for c in (title + (wikitext or "")) if c.isalpha()]
    if not letters:
        return False
    non_latin = sum(1 for c in letters if ord(c) > 0x024F)
    return non_latin / len(letters) > NON_LATIN_LETTER_SHARE


def page_banners(wikitext, template_map):
    """Version-banner kinds ('variant'/'nethack') the page calls."""
    kinds = set()
    for name in page_template_names(wikitext):
        b = template_map.banner(name) if template_map else None
        if b:
            kinds.add(b[0])
    return kinds


def variant_rule(title, categories, banner_kinds):
    """Name of the variant-only rule an article matches, or None."""
    m = PARENTHETICAL_SUFFIX_RE.match(title)
    if m and VARIANT_RE.search(m.group(2)):
        return "title_suffix"
    if "/" in title and title.rsplit("/", 1)[1].strip().lower() in VARIANT_NAMES_LOWER:
        return "subpage"
    if categories and all(VARIANT_RE.search(c) or c.startswith("Variant ") for c in categories):
        return "categories"
    if banner_kinds and banner_kinds == {"variant"}:
        return "banners"
    return None


def classify_page(title, wikitext, template_map=None):
    """(reason, detail) for a skipped article, or (None, exception) for a kept one.

    reason is 'non_english', 'variant_only' (detail: the rule) or 'non_game';
    for a kept article, detail names the exception that kept it, if any.
    """
    if is_non_english(title, wikitext):
        return "non_english", None
    categories = page_categories(wikitext)
    cats_lower = {c.lower() for c in categories}
    rule = variant_rule(title, categories, page_banners(wikitext, template_map))
    if rule:
        if cats_lower & VARIANT_OVERVIEW_CATEGORIES:
            return None, "variant_overview"
        if "gnollhack" in title.lower() or any("gnollhack" in c for c in cats_lower) \
                or "gnollhack" in (wikitext or "").lower():
            return None, "gnollhack"
        return "variant_only", rule
    if title in NON_GAME_KEEP_TITLES:
        return None, ("non_game_keep" if cats_lower and cats_lower <= NON_GAME_CATEGORIES else None)
    if title == MAIN_PAGE_TITLE or (cats_lower and cats_lower <= NON_GAME_CATEGORIES):
        return "non_game", None
    return None, None


# ---------------------------------------------------------------------------
# Conversion
# ---------------------------------------------------------------------------

class ConversionStats:
    """Per-page counters filled during conversion."""

    def __init__(self):
        self.dropped = Counter()
        self.transcluded = Counter()
        self.tables = 0
        self.layout_tables = 0
        self.preformatted = 0
        self.handler_errors = Counter()


class _Context:
    def __init__(self, title, template_map, stats):
        self.title = title
        self.tmap = template_map if template_map is not None else TemplateMap()
        self.stats = stats if stats is not None else ConversionStats()
        self.pre_blocks = []
        self.math = []
        self.tables = []


def protect_entities(text):
    for entity, char in PROTECTED_ENTITIES.items():
        text = text.replace(entity, char)
    return text


def restore_protected(text):
    for char, value in PROTECTED_RESTORE.items():
        text = text.replace(char, value)
    return text


def protect_symbols(text):
    for char, value in SYMBOL_PROTECT.items():
        text = text.replace(char, value)
    return text


def remove_quote_markup(text):
    """Bold and italic apostrophe markup removed line by line, as MediaWiki never carries it past a line end."""
    def repl(m):
        n = len(m.group(0))
        if n == 4:
            return "'"
        return "'" * (n - 5) if n > 5 else ""
    return re.sub(r"'{2,}", repl, text)


def _links_to_text(code):
    """Wikilinks to their display text (file and category links removed), external links to their title."""
    for wl in code.filter_wikilinks():
        title_str = str(wl.title)
        try:
            if FILE_LINK_TITLE_RE.match(title_str):
                code.remove(wl)
            else:
                code.replace(wl, str(wl.text).strip() if wl.text else title_str.strip().lstrip(":"))
        except ValueError:
            pass
    for el in code.filter_external_links():
        display = str(el.title) if el.title else str(el.url)
        try:
            code.replace(el, display.strip())
        except ValueError:
            pass


def clean_inline(text, escape_pipes=False):
    """Wikitext to one line of plain text: links, markup, HTML tags and list markers removed."""
    if not text:
        return ""
    text = text.replace(REMOVED_MARK, "")
    text = re.sub(r"(?i)<br\s*/?>", " ", text)
    text = re.sub(r"(?i)</li\s*>", "", text)
    text = re.sub(r"(?i)<li\b[^>]*>", "; ", text)
    text = re.sub(r"\n[*#:;]+\s*", "; ", text)
    text = re.sub(r"^[*#]+\s+", "", text)
    try:
        parsed = mwparserfromhell.parse(text)
        _links_to_text(parsed)
        text = parsed.strip_code()
    except Exception:
        pass
    text = HTML_TAG_RE.sub("", text)
    text = re.sub(r"'{2,}", "", text)
    text = restore_protected(text)
    text = re.sub(r"\s+", " ", text).strip()
    text = re.sub(r"\s*;\s*(?:;\s*)*", "; ", text)
    text = re.sub(r"^(?:;\s*)+|(?:\s*;)+$", "", text).strip()
    if escape_pipes:
        text = text.replace("|", "\\|")
    return text


def format_template_params(template, one_line=False):
    """Extract key-value pairs from a template, returning readable text."""
    lines = []
    for param in template.params:
        name = str(param.name).strip()
        value = str(param.value).strip()
        if not name or not value or name.lower() in STAT_IMAGE_PARAMS:
            continue
        value = clean_inline(value)
        if value and one_line and name.isdigit():
            lines.append(value)
        elif value:
            # Capitalize first letter of param name for readability
            display_name = name.replace("_", " ").replace("-", " ")
            display_name = display_name[0].upper() + display_name[1:] if display_name else display_name
            lines.append(f"{display_name}: {value}")
    return ("; " if one_line else "\n").join(lines)


def _params(template):
    """(positional values, named values) of a template, stripped strings."""
    named = {}
    for p in template.params:
        named[str(p.name).strip()] = str(p.value).strip()
    positional = []
    numbers = sorted(int(k) for k in named if k.isdigit() and int(k) > 0)
    if numbers:
        positional = [named.get(str(i), "") for i in range(1, numbers[-1] + 1)]
    return positional, named


def _arg(positional, index, default=""):
    return positional[index] if len(positional) > index and positional[index] else default


def _safe_arith(expr):
    """Evaluate integer arithmetic (+ - * / parentheses); None when it is anything else."""
    ops = {ast.Add: operator.add, ast.Sub: operator.sub, ast.Mult: operator.mul,
           ast.Div: operator.truediv, ast.USub: operator.neg, ast.UAdd: operator.pos}

    def ev(node):
        if isinstance(node, ast.Expression):
            return ev(node.body)
        if isinstance(node, ast.Constant) and isinstance(node.value, (int, float)):
            return node.value
        if isinstance(node, ast.BinOp) and type(node.op) in ops:
            return ops[type(node.op)](ev(node.left), ev(node.right))
        if isinstance(node, ast.UnaryOp) and type(node.op) in ops:
            return ops[type(node.op)](ev(node.operand))
        raise ValueError(expr)

    try:
        return ev(ast.parse(expr.strip().replace("−", "-"), mode="eval"))
    except Exception:
        return None


def _format_number(value):
    if isinstance(value, float) and value.is_integer():
        value = int(value)
    return str(value)


def _join_names(names, conj):
    if not names:
        return ""
    if len(names) == 1:
        return names[0]
    if len(names) == 2:
        return f"{names[0]} {conj} {names[1]}" if conj else f"{names[0]}, {names[1]}"
    return ", ".join(names[:-1]) + (f", {conj} " if conj else ", ") + names[-1]


def _own_line(text):
    return f"\n{text}\n"


def _quote_lines(text):
    lines = [line.strip() for line in text.strip("\n").split("\n")]
    return "\n".join(f"> {line}" if line else ">" for line in lines)


def _note(text):
    text = clean_inline(text)
    return f"{REMOVED_MARK} (Note: {text})" if text else REMOVED_MARK


def _render_inline(key, template, ctx):
    """Rendering of an inline-text template, or None when the key has no inline handler."""
    pos, named = _params(template)
    if key == "!":
        return PROTECTED_ENTITIES["&#124;"]
    if key == "=":
        return "="
    if key in ("kbd", "key"):
        keys = [p for p in pos if p]
        return "[" + "+".join(keys) + "]" if keys else REMOVED_MARK
    if key in ("monsym", "mcsl"):
        return f"'{protect_symbols(_arg(pos, 0))}'"
    if key in ("frac", "sfrac"):
        if len(pos) == 1:
            return f"1/{pos[0]}"
        if len(pos) == 2:
            return f"{pos[0]}/{pos[1]}"
        if len(pos) == 3:
            return f"{pos[0]} {pos[1]}/{pos[2]}"
        return REMOVED_MARK
    if key == "upcoming":
        if len(pos) >= 2:
            content = pos[1]
            try:
                content = mwparserfromhell.parse(content).strip_code().strip()
            except Exception:
                pass
            return f"[Upcoming in {pos[0]}: {content}]"
        return REMOVED_MARK
    if key == "wikipedia":
        return f"(See Wikipedia: {_arg(pos, 0, ctx.title)})"
    if key == "roles":
        return ALL_ROLES
    if key in COLOR_TEMPLATES or key.startswith("bright"):
        return protect_symbols(_arg(pos, 0))
    if key in ("monsymlink", "msl", "monlink"):
        return _arg(pos, 0)
    if key == "monclasssym":
        return protect_symbols(_arg(pos, 0))
    if key in ("monclasslink", "monclass"):
        return f"monster class '{protect_symbols(_arg(pos, 0))}'"
    if key in OF_TEMPLATES:
        item_class = OF_TEMPLATES[key]
        names = [p for p in (pos[1:] if item_class is None else pos) if p]
        conj = "or" if named.get("or") else ("and" if named.get("and") else "")
        return _join_names(names, conj)
    if key in ("sa", "va", "aa"):
        return _arg(pos, 1, _arg(pos, 0))
    if key in ("yes", "no", "partial"):
        return _arg(pos, 0, key.capitalize())
    if key == "alignment":
        value = _arg(pos, 0).lower()
        if value in ("l", "law", "lawful"):
            return "Lawful"
        if value in ("n", "neu", "neutral"):
            return "Neutral"
        if value in ("c", "cha", "chaotic"):
            return "Chaotic"
        if value in ("white", "gray", "grey", "black"):
            return value.capitalize()
        return "Unknown alignment"
    if key == "rn1":
        x, y = _safe_arith(_arg(pos, 0)), _safe_arith(_arg(pos, 1))
        if x is None or y is None:
            return None
        sep = pos[2] if len(pos) > 2 and pos[2] else "–"
        return f"{_format_number(y)}{sep}{_format_number(x + y - 1)}"
    if key == "davg":
        values = [_arg(pos, i, "0") for i in range(6)] + [named.get("bonus", "0") or "0"]
        if not all(re.fullmatch(r"-?\d+", v) for v in values):
            return None
        n = [int(v) for v in values]
        avg = sum((n[i] * n[i + 1] + n[i]) / 2 for i in (0, 2, 4)) + n[6]
        return _format_number(avg)
    if key == "right-align":
        return _arg(pos, 0)
    if key == "diagonal split header":
        return f"{_arg(pos, 0)} \\ {_arg(pos, 1)}"
    if key == "random appearance":
        return "random"
    if key == "caption":
        return " ".join(p for p in (_arg(pos, 0), _arg(pos, 1)) if p)
    if key == "function":
        name = _arg(pos, 1)
        return name if _arg(pos, 2) else f"{name} in {_arg(pos, 0)}"
    if key == "source-link-500":
        return _arg(pos, 0)
    if key == "questmon":
        kind = _arg(pos, 2).lower()
        role = _arg(pos, 1)
        role = role[:1].upper() + role[1:]
        article = "a" if kind == "guardian" else "the"
        return f"{_arg(pos, 0)} is {article} {role} quest {kind}."
    if key == "cnlink":
        return _arg(pos, 1, _arg(pos, 0))
    if key == "bugstatus":
        return _arg(pos, 0, "Unknown")
    return None


def _render_pointer(key, template, ctx):
    pos, named = _params(template)
    if key == "main":
        names = [named.get(f"l{i + 1}") or p for i, p in enumerate(pos) if p]
        if not names:
            return REMOVED_MARK
        label = "Main articles" if len(names) > 1 else "Main article"
        return _own_line(f"{label}: {', '.join(names)}")
    if key == "see also":
        names = [named.get(f"l{i + 1}") or p for i, p in enumerate(pos) if p]
        return _own_line(f"See also: {', '.join(names)}") if names else REMOVED_MARK
    if key == "encyclopedia-redirect":
        return _own_line(f"See the encyclopedia entry for {_arg(pos, 1, _arg(pos, 0))}.")
    if key == "otheruses":
        about = f"This article is about {pos[0]}. " if _arg(pos, 0) else ""
        other = _arg(pos, 1, "other uses")
        target = _arg(pos, 2, f"{ctx.title} (disambiguation)")
        return _own_line(f"{about}For {other}, see {target}.")
    if key == "redirects-here":
        return _own_line(f"{_arg(pos, 0)} redirects here. For {_arg(pos, 1)}, see {_arg(pos, 2)}.")
    if key == "distinguish":
        names = [p for p in pos if p]
        return _own_line(f"Not to be confused with {', '.join(names)}.") if names else REMOVED_MARK
    return None


POINTER_TEMPLATES = {"main", "see also", "encyclopedia-redirect", "otheruses", "redirects-here", "distinguish"}


def _render_block(key, template, ctx):
    pos, named = _params(template)
    if key == "message":
        message = _arg(pos, 0)
        explanation = _arg(pos, 1)
        line = f"\"{message}\" — {explanation}" if explanation else f"\"{message}\""
        return _own_line(line)
    if key == "encyclopedia":
        quote = named.get("quote") or _arg(pos, 0)
        author = named.get("author") or _arg(pos, 1)
        lines = ["Encyclopedia entry:", _quote_lines(quote)]
        if author:
            lines.append(f"> — {author}")
        return "\n\n" + "\n".join(lines) + "\n\n"
    if key == "guidebook":
        return "\n\nGuidebook:\n" + _quote_lines(_arg(pos, 0)) + "\n\n"
    return None


BLOCK_TEMPLATES = {"message", "encyclopedia", "guidebook"}


def _substitute_arguments(code, named):
    """Replace {{{name|default}}} arguments in a parsed template body."""
    for i, node in enumerate(list(code.nodes)):
        if isinstance(node, Argument):
            name = str(node.name).strip()
            if name in named:
                code.nodes[i] = Text(named[name])
            elif node.default is not None:
                _substitute_arguments(node.default, named)
                code.nodes[i] = Text(str(node.default))
            else:
                code.nodes[i] = Text("")
        else:
            for sub in _child_codes(node):
                _substitute_arguments(sub, named)


def _child_codes(node):
    """The Wikicode containers inside a node."""
    if isinstance(node, Template):
        return [node.name] + [p.value for p in node.params]
    if isinstance(node, Tag):
        return [node.contents] if node.contents is not None else []
    if isinstance(node, Wikilink):
        return [c for c in (node.title, node.text) if c is not None]
    if isinstance(node, ExternalLink):
        return [node.title] if node.title is not None else []
    if isinstance(node, Heading):
        return [node.title]
    if isinstance(node, Argument):
        return [node.default] if node.default is not None else []
    return []


def _count_dropped(ctx, name):
    ctx.stats.dropped[name] += 1


def _render_template(template, ctx, stat_blocks, top_level, depth):
    """Replacement wikitext for one template whose parameters are already expanded."""
    raw_name = str(template.name).strip()
    if raw_name.startswith("#") or ":" in raw_name and not raw_name.lower().startswith("template:"):
        # Parser function or magic word with an argument
        _count_dropped(ctx, raw_name.split(":", 1)[0].strip().lower() + ":")
        return REMOVED_MARK
    name = normalize_template_name(raw_name)
    canonical = ctx.tmap.resolve(name)
    keys = [name.lower(), canonical.lower()]

    def first(table):
        for k in keys:
            if k in table:
                return k
        return None

    key = first(STAT_TEMPLATES)
    if key:
        label = STAT_TEMPLATES[key]
        if top_level:
            params_text = format_template_params(template)
            if params_text:
                stat_blocks.append(f"--- {label} ---\n{params_text}")
            return REMOVED_MARK
        params_text = format_template_params(template, one_line=True)
        return params_text or REMOVED_MARK

    key = first(NOTE_TEMPLATES)
    if key:
        pos, named = _params(template)
        if key == "refsrc":
            return _note(named.get("comment", ""))
        return _note(_arg(pos, 0))

    if first(INTENTIONAL_DROP_TEMPLATES):
        return REMOVED_MARK

    key = first(POINTER_TEMPLATES)
    if key:
        return _render_pointer(key, template, ctx)

    for k in keys:
        rendered = _render_inline(k, template, ctx)
        if rendered is not None:
            return rendered

    key = first(BLOCK_TEMPLATES)
    if key:
        return _render_block(key, template, ctx)

    banner = ctx.tmap.banner(canonical)
    if banner:
        return _own_line(banner[1])

    if any(p.match(canonical.lower()) for p in TRANSCLUDE_PATTERNS):
        body = ctx.tmap.body(canonical)
        if body is not None and depth < MAX_EXPANSION_DEPTH:
            _, named = _params(template)
            sub = mwparserfromhell.parse(body)
            _substitute_arguments(sub, named)
            if "{{#" not in str(sub):
                ctx.stats.transcluded[canonical] += 1
                expand_templates(sub, ctx, stat_blocks, top_level=False, depth=depth + 1)
                return "\n" + str(sub).strip("\n") + "\n"

    _count_dropped(ctx, canonical)
    return REMOVED_MARK


def expand_templates(code, ctx, stat_blocks, top_level=True, depth=0):
    """Render every template in code, innermost first, in place."""
    for i, node in enumerate(list(code.nodes)):
        if isinstance(node, Template):
            for p in node.params:
                expand_templates(p.value, ctx, stat_blocks, top_level=False, depth=depth)
            try:
                replacement = _render_template(node, ctx, stat_blocks, top_level, depth)
            except Exception:
                ctx.stats.handler_errors[normalize_template_name(node.name)] += 1
                replacement = REMOVED_MARK
            code.nodes[i] = Text(replacement)
        else:
            for sub in _child_codes(node):
                expand_templates(sub, ctx, stat_blocks, top_level=False, depth=depth)


def _remove_tags(code, names):
    for i, node in enumerate(list(code.nodes)):
        if isinstance(node, Tag) and str(node.tag).strip().lower() in names:
            code.nodes[i] = Text("")
        else:
            for sub in _child_codes(node):
                _remove_tags(sub, names)


def _protect_math(text, ctx):
    def repl(m):
        ctx.math.append(re.sub(r"\s+", " ", m.group(2)).strip())
        return f"XMATH{len(ctx.math) - 1}X"
    return MATH_RE.sub(repl, text)


def _guard_heading_lines(text):
    return re.sub(r"(?m)^(#+\s)", " \\1", text)


def _fence(content):
    content = restore_protected(content)
    content = re.sub(r"</?nowiki\s*/?>", "", content, flags=re.IGNORECASE)
    content = content.replace(REMOVED_MARK, "")
    lines = content.split("\n")
    while lines and not lines[0].strip():
        lines.pop(0)
    while lines and not lines[-1].strip():
        lines.pop()
    lines = [line.rstrip() for line in lines]
    if not lines:
        return ""
    text = _guard_heading_lines("\n".join(lines))
    fence = "````" if "```" in text else "```"
    return f"{fence}\n{text}\n{fence}"


def _add_pre_block(ctx, fenced):
    ctx.pre_blocks.append(fenced)
    ctx.stats.preformatted += 1
    return f"\n\nXPRE{len(ctx.pre_blocks) - 1}X\n\n"


def _extract_preformatted_tags(text, ctx):
    """<pre>, <syntaxhighlight>, <source> and <replacecharsblock> blocks to fenced-block placeholders."""
    def repl(m):
        tag = m.group(1).lower()
        content = m.group(3)
        if content.startswith("\n"):
            content = content[1:]
        if tag == "replacecharsblock":
            lines = content.split("\n")
            # A character-mapping legend ("x=...", "default=...") runs up to the first blank line
            if lines and re.match(r"^(?:.|[A-Za-z]+)=", lines[0]) \
                    and any(not line.strip() for line in lines):
                blank = next(i for i, line in enumerate(lines) if not line.strip())
                lines = lines[blank + 1:]
            content = "\n".join(lines)
            content = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", r"\1", content)
        fenced = _fence(content)
        return _add_pre_block(ctx, fenced) if fenced else ""
    return PRE_TAG_RE.sub(repl, text)


def _pre_line_text(line):
    line = re.sub(r"'{2,}", "", HTML_TAG_RE.sub("", line[1:]))
    return re.sub(r"\[\[([^|\]]*)(?:\|([^\]]*))?\]\]",
                  lambda m: "" if FILE_LINK_TITLE_RE.match(m.group(1)) else (m.group(2) if m.group(2) is not None else m.group(1)),
                  line)


def _extract_space_preformatted(parsed, ctx):
    """Runs of lines starting with a space, outside tables, to fenced-block placeholders."""
    slots = []
    parts = []
    for node in parsed.nodes:
        if isinstance(node, Tag) and str(node.tag).strip().lower() in ("table", "gallery", "poem"):
            slots.append(str(node))
            parts.append(f"\x02SLOT{len(slots) - 1}\x02")
        else:
            parts.append(str(node))
    text = "".join(parts)

    out = []
    run = []

    def flush():
        while run and not run[-1].strip():
            run.pop()
        if run:
            fenced = _fence("\n".join(_pre_line_text(line) for line in run))
            if fenced:
                out.append(_add_pre_block(ctx, fenced).strip("\n"))
        run.clear()

    for line in text.split("\n"):
        if line.startswith(" ") and (run or line.strip()):
            run.append(line)
        else:
            flush()
            out.append(line)
    flush()
    text = "\n".join(out)
    text = re.sub(r"\x02SLOT(\d+)\x02", lambda m: slots[int(m.group(1))], text)
    return text


def _flatten_table(tag):
    """A nested table as one line of text."""
    rows = []
    for row in _table_rows(tag)[1]:
        cells = [clean_inline(str(c.contents) if c.contents is not None else "") for c in row]
        rows.append(" ".join(c for c in cells if c))
    return "; ".join(r for r in rows if r)


def _table_rows(tag):
    """(caption, rows of cell tags) of a parsed table."""
    caption = None
    rows = []
    current = None

    def add_cell(cell):
        nonlocal current
        if current is None:
            current = []
            rows.append(current)
        current.append(cell)

    for node in (tag.contents.nodes if tag.contents is not None else []):
        if not isinstance(node, Tag):
            continue
        t = str(node.tag).strip().lower()
        if t == "tr":
            current = []
            rows.append(current)
            for cell in (node.contents.nodes if node.contents is not None else []):
                if isinstance(cell, Tag) and str(cell.tag).strip().lower() in ("td", "th"):
                    current.append(cell)
        elif t in ("td", "th"):
            text = str(node.contents) if node.contents is not None else ""
            if t == "td" and node.wiki_markup == "|" and text.lstrip().startswith("+") and not rows:
                caption = text.lstrip()[1:]
                continue
            add_cell(node)
    return caption, [r for r in rows if r]


def _cell_raw(cell, ctx):
    if cell.contents is None:
        return ""
    for i, node in enumerate(list(cell.contents.nodes)):
        if isinstance(node, Tag) and str(node.tag).strip().lower() == "table":
            cell.contents.nodes[i] = Text(" " + _flatten_table(node) + " ")
    return str(cell.contents)


def _colspan(cell):
    for attr in cell.attributes:
        if str(attr.name).strip().lower() == "colspan":
            m = re.search(r"\d+", str(attr.value or ""))
            if m:
                return max(1, min(int(m.group(0)), MAX_COLSPAN))
    return 1


def _table_markdown(tag, ctx):
    caption, rows = _table_rows(tag)
    raw_rows = [[(_cell_raw(c, ctx), _colspan(c)) for c in row] for row in rows]
    pre_re = re.compile(r"XPRE\d+X")

    if any(pre_re.search(raw) for row in raw_rows for raw, _ in row):
        ctx.stats.layout_tables += 1
        parts = []
        for row in raw_rows:
            for raw, _ in row:
                pos = 0
                for m in pre_re.finditer(raw):
                    before = clean_inline(raw[pos:m.start()])
                    if before:
                        parts.append(before)
                    parts.append(m.group(0))
                    pos = m.end()
                rest = clean_inline(raw[pos:])
                if rest:
                    parts.append(rest)
        if caption:
            parts.append(clean_inline(caption))
        return "\n\n".join(parts)

    ctx.stats.tables += 1
    md_rows = []
    for row in raw_rows:
        cells = []
        for raw, span in row:
            cells.append(clean_inline(raw, escape_pipes=True))
            cells.extend([""] * (span - 1))
        md_rows.append(cells)
    if not md_rows:
        return clean_inline(caption) if caption else ""
    width = max(len(r) for r in md_rows)
    lines = []
    if caption:
        lines.append(clean_inline(caption))
        lines.append("")
    for i, row in enumerate(md_rows):
        row = row + [""] * (width - len(row))
        lines.append("| " + " | ".join(row) + " |")
        if i == 0:
            lines.append("|" + "---|" * width)
    text = "\n".join(lines)
    return re.sub(r"XMATH(\d+)X",
                  lambda m: "`" + restore_protected(ctx.math[int(m.group(1))]).replace("|", "\\|") + "`", text)


def _convert_tables(code, ctx):
    """Replace each outermost table with a placeholder line."""
    for i, node in enumerate(list(code.nodes)):
        if isinstance(node, Tag) and str(node.tag).strip().lower() == "table":
            ctx.tables.append(_table_markdown(node, ctx))
            code.nodes[i] = Text(f"\n\nXTABLE{len(ctx.tables) - 1}X\n\n")
        else:
            for sub in _child_codes(node):
                _convert_tables(sub, ctx)


LIST_TAGS = {"li", "dt", "dd"}


def _convert_lists(code):
    """Wikitext list markers to Markdown list markers ('- ', '1. '), nested by indentation."""
    nodes = code.nodes
    i = 0
    while i < len(nodes):
        node = nodes[i]
        if isinstance(node, Tag) and str(node.tag).strip().lower() in LIST_TAGS and node.wiki_markup:
            j = i
            markers = ""
            while j < len(nodes) and isinstance(nodes[j], Tag) \
                    and str(nodes[j].tag).strip().lower() in LIST_TAGS and nodes[j].wiki_markup:
                markers += str(nodes[j].wiki_markup)
                j += 1
            if markers[-1] in "*#":
                marker = "- " if markers[-1] == "*" else "1. "
                indent = f"XLISTINDENT{len(markers) - 1}X" if len(markers) > 1 else ""
                nodes[i] = Text(indent + marker)
                for k in range(i + 1, j):
                    nodes[k] = Text("")
            i = j
            continue
        for sub in _child_codes(node):
            _convert_lists(sub)
        i += 1


def convert_wikitext_to_plaintext(wikitext, title="", template_map=None, stats=None):
    """Convert MediaWiki wikitext to clean plain text with preserved structure."""
    if not wikitext:
        return [], ""

    ctx = _Context(title, template_map, stats)

    # Decode HTML entities first, keeping entity-encoded wikitext syntax inert
    text = html.unescape(protect_entities(wikitext))
    text = _protect_math(text, ctx)
    text = _extract_preformatted_tags(text, ctx)
    text = MAGIC_WORD_RE.sub("", text)
    text = remove_quote_markup(text)

    try:
        parsed = mwparserfromhell.parse(text)
    except Exception as e:
        # If parsing fails, do basic regex cleanup
        return _fallback_cleanup(text)

    stat_blocks = []
    _remove_tags(parsed, {"ref", "references"})
    expand_templates(parsed, ctx, stat_blocks)

    # Preformatted text from transcluded bodies, then space-indented preformatted lines
    text = _extract_preformatted_tags(str(parsed), ctx)
    text = _extract_space_preformatted(mwparserfromhell.parse(text), ctx)
    text = text.replace(REMOVED_MARK, "")

    parsed = mwparserfromhell.parse(text)
    _convert_tables(parsed, ctx)
    _convert_lists(parsed)

    # Convert wikilinks to plain text (file and category links removed)
    _links_to_text(parsed)

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
        _links_to_text(parsed2)
        result = parsed2.strip_code()
    except Exception:
        result = raw

    # Heading guard: no line that is not a heading may look like one
    result = re.sub(r"(?m)^(#+\s)", GUARD_MARK + r"\1", result)

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
    result = result.strip()

    # Splice back the blocks kept out of whitespace normalization
    result = re.sub(r"XTABLE(\d+)X", lambda m: ctx.tables[int(m.group(1))], result)
    result = re.sub(r"XPRE(\d+)X", lambda m: ctx.pre_blocks[int(m.group(1))], result)
    result = _finish(result, ctx)
    result = re.sub(r"\n{3,}", "\n\n", result).strip("\n")

    stat_blocks = [_finish(block, ctx) for block in stat_blocks]
    return stat_blocks, result


def _finish(text, ctx):
    text = re.sub(r"XMATH(\d+)X", lambda m: f"`{ctx.math[int(m.group(1))]}`", text)
    text = re.sub(r"XLISTINDENT(\d+)X", lambda m: "  " * int(m.group(1)), text)
    text = text.replace(GUARD_MARK, " ").replace(REMOVED_MARK, "").replace("\x02", "")
    return restore_protected(text)


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


# ---------------------------------------------------------------------------
# Summary and quality measures
# ---------------------------------------------------------------------------

META_LINE_RE = re.compile(
    r"^(?:Main articles?:|See also:|See the encyclopedia entry|This article is about|"
    r"Not to be confused with|\(Note:|\[Upcoming in|Encyclopedia entry:|Guidebook:)"
    r"|redirects here\. For "
    r"|^For [^.]+, see [^.]+\.$"
)


def make_summary(content, skip_lines=frozenset()):
    """First sentence of the article's prose."""
    in_fence = False
    for line in content.split("\n"):
        line = line.strip()
        if line.startswith("```"):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        if not line or line.startswith("#") or line.startswith("---"):
            continue
        if line.startswith("|") or line.startswith(">"):
            continue
        # Skip meta-lines that aren't real article content
        if line.startswith("(See Wikipedia:"):
            continue
        if line.startswith("Parts of this"):
            continue
        if META_LINE_RE.search(line) or line in skip_lines:
            continue
        line = re.sub(r"^(?:- |1\. )", "", line)
        if len(line) < 20:
            continue
        # Take first sentence
        dot_pos = line.find(". ")
        if dot_pos > 0:
            return line[:dot_pos + 1]
        return line[:200]
    return ""


def _strip_fences(text):
    return re.sub(r"(?ms)^```.*?^```", "", text)


def count_empty_sections(body):
    """Headings followed by a sibling or parent heading, or the end of the file, with no content between."""
    lines = body.split("\n")
    heads = [(i, len(m.group(1))) for i, line in enumerate(lines) if (m := HEADING_LINE_RE.match(line))]
    empty = 0
    for k, (i, level) in enumerate(heads):
        nxt = heads[k + 1] if k + 1 < len(heads) else None
        end = nxt[0] if nxt else len(lines)
        if any(lines[j].strip() for j in range(i + 1, end)):
            continue
        if nxt and nxt[1] > level:
            continue
        empty += 1
    return empty


def count_source_headings(wikitext):
    try:
        return len(mwparserfromhell.parse(wikitext).filter_headings())
    except Exception:
        return len(re.findall(r"(?m)^=+[^=\n].*=+\s*$", wikitext))


def count_heading_lines(body):
    return sum(1 for line in body.split("\n") if HEADING_LINE_RE.match(line))


RESIDUE_CHECKS = {
    # Template or link syntax, not runs of glyphs such as "[[[[" or a map's "}}}}"
    "braces": re.compile(r"(?<!\{)\{\{(?=[^{}\s])|(?<=[^{}\s])\}\}(?!\})"),
    "brackets": re.compile(r"(?<!\[)\[\[(?=[^\[\]\s])|(?<=[^\[\]\s])\]\](?!\])"),
    "italic_bold_quotes": re.compile(r"''"),
    "html_tags": HTML_TAG_RE,
    "image_file_names": re.compile(
        r"\b(?:file|image)\s*:\s*[^\n|\]]*?\.(?:png|gif|jpe?g|svg|webp|bmp)\b", re.IGNORECASE),
    "magic_words": re.compile(r"__[A-Z]+__"),
    "placeholders": PLACEHOLDER_RE,
}

TABLE_ROW_WITH_FENCE_RE = re.compile(r"(?m)^\|.*```")


def residue_counts(body):
    """Markup left in the output outside code fences and inline code."""
    outside_fences = _strip_fences(body)
    text = re.sub(r"`[^`\n]*`", "", outside_fences)
    counts = {name: len(rx.findall(text)) for name, rx in RESIDUE_CHECKS.items()}
    counts["table_rows_with_fence"] = len(TABLE_ROW_WITH_FENCE_RE.findall(outside_fences))
    return counts


class Report:
    """Collects the figures written to _conversion_report.json."""

    def __init__(self):
        self.skipped_titles = {"non_english": [], "non_game": [],
                               "variant_only": defaultdict(list)}
        self.kept_by_exception = defaultdict(list)
        self.dropped_occurrences = Counter()
        self.dropped_pages = Counter()
        self.transcluded = Counter()
        self.handler_errors = Counter()
        self.tables = 0
        self.layout_tables = 0
        self.preformatted = 0
        self.empty_sections = 0
        self.empty_section_pages = []
        self.false_heading_lines = 0
        self.false_heading_pages = []
        self.residue = defaultdict(lambda: {"occurrences": 0, "pages": []})
        self.file_sizes = {}
        self.kept_articles = []

    def add_page(self, title, filename, stats, body, wikitext, output_bytes):
        self.tables += stats.tables
        self.layout_tables += stats.layout_tables
        self.preformatted += stats.preformatted
        self.transcluded.update(stats.transcluded)
        self.handler_errors.update(stats.handler_errors)
        for name, n in stats.dropped.items():
            self.dropped_occurrences[name] += n
            self.dropped_pages[name] += 1
        empty = count_empty_sections(body)
        if empty:
            self.empty_sections += empty
            self.empty_section_pages.append(title)
        false_headings = max(0, count_heading_lines(body) - count_source_headings(wikitext))
        if false_headings:
            self.false_heading_lines += false_headings
            self.false_heading_pages.append(title)
        for name, n in residue_counts(body).items():
            if n:
                self.residue[name]["occurrences"] += n
                self.residue[name]["pages"].append({"title": title, "count": n})
        self.file_sizes[filename] = output_bytes


def _candidates(report):
    """Variant-like title suffixes and category prefixes, and variant-looking lead sentences, of kept articles."""
    def title_suffix(title):
        m = PARENTHETICAL_SUFFIX_RE.match(title)
        return m.group(2).strip() if m else None

    def category_prefixes(categories):
        out = set()
        for c in categories:
            words = c.split(" ")
            if len(words) >= 2 and not VARIANT_RE.search(c):
                out.add(" ".join(words[:-1]))
        return out

    # A qualifier already seen on an article with a NetHack version banner describes vanilla
    vanilla_seen = {q.lower() for q in VANILLA_QUALIFIERS}
    for title, categories, banner_kinds, _ in report.kept_articles:
        if "nethack" in banner_kinds:
            if title_suffix(title):
                vanilla_seen.add(title_suffix(title).lower())
            vanilla_seen.update(p.lower() for p in category_prefixes(categories))

    suffixes = defaultdict(list)
    prefixes = defaultdict(list)
    lead = []
    for title, categories, banner_kinds, content in report.kept_articles:
        q = title_suffix(title)
        if q and not VARIANT_RE.search(q) and q.lower() not in vanilla_seen:
            suffixes[q].append(title)
        for p in category_prefixes(categories):
            if p.lower() not in vanilla_seen:
                prefixes[p].append(title)
        if "nethack" not in banner_kinds:
            para = _first_paragraph(content)
            for sentence in re.split(r"(?<=[.!?])\s+", para):
                if VARIANT_RE.search(sentence) and re.search(
                        r"(?i)\b(appears?|appearing|introduced|exists?|only (?:in|appears|exists)|"
                        r"exclusive to|unique to|added (?:in|by))\b", sentence):
                    lead.append(title)
                    break

    def listing(d):
        rows = [{"name": k, "articles": len(v), "examples": sorted(v)[:MAX_REPORT_EXAMPLES]}
                for k, v in d.items() if len(v) >= MIN_CANDIDATE_ARTICLES]
        return sorted(rows, key=lambda r: (-r["articles"], r["name"]))

    return {
        "variant_like_title_suffixes": listing(suffixes),
        "variant_like_category_prefixes": listing(prefixes),
        "variant_looking_lead_sentences": sorted(lead),
    }


def _first_paragraph(content):
    para = []
    for line in content.split("\n"):
        s = line.strip()
        if not s:
            if para:
                break
            continue
        if s.startswith(("#", "---", "|", ">", "```")) or META_LINE_RE.search(s):
            if para:
                break
            continue
        para.append(s)
    return " ".join(para)


def write_report(path, report, template_map, input_xml, counts, started):
    try:
        st = os.stat(input_xml)
        dump = {"path": os.path.abspath(input_xml), "bytes": st.st_size,
                "modified": datetime.datetime.fromtimestamp(st.st_mtime).isoformat(timespec="seconds")}
    except OSError:
        dump = {"path": input_xml}

    def pages_listing(entry, full):
        pages = sorted(entry["pages"], key=lambda p: (-p["count"], p["title"]))
        return {"occurrences": entry["occurrences"], "pages": len(pages),
                "examples": pages if full else pages[:MAX_REPORT_EXAMPLES]}

    residue = {}
    for name in list(RESIDUE_CHECKS) + ["table_rows_with_fence"]:
        entry = report.residue.get(name, {"occurrences": 0, "pages": []})
        residue[name] = pages_listing(entry, name in ("braces", "brackets", "italic_bold_quotes"))

    largest = max(report.file_sizes.items(), key=lambda kv: kv[1], default=(None, 0))
    dropped = sorted(
        ({"name": n, "occurrences": c, "pages": report.dropped_pages[n]}
         for n, c in report.dropped_occurrences.items()),
        key=lambda r: (-r["occurrences"], r["name"]))

    data = {
        "dump": dump,
        "generated": datetime.datetime.now().isoformat(timespec="seconds"),
        "duration_seconds": round((datetime.datetime.now() - started).total_seconds()),
        "pages": counts,
        "skipped_titles": {
            "non_english": sorted(report.skipped_titles["non_english"]),
            "variant_only": {k: sorted(v) for k, v in sorted(report.skipped_titles["variant_only"].items())},
            "non_game": sorted(report.skipped_titles["non_game"]),
        },
        "kept_by_exception": {k: sorted(v) for k, v in sorted(report.kept_by_exception.items())},
        "version_banners": template_map.banner_classification(),
        "candidates": _candidates(report),
        "tables_converted": report.tables,
        "layout_tables": report.layout_tables,
        "preformatted_blocks": report.preformatted,
        "transclusions": dict(report.transcluded.most_common()),
        "handler_errors": dict(report.handler_errors.most_common()),
        "dropped_templates": dropped,
        "quality": {
            "empty_sections": {"count": report.empty_sections, "pages": len(report.empty_section_pages),
                               "examples": sorted(report.empty_section_pages)[:MAX_REPORT_EXAMPLES]},
            "heading_shaped_non_heading_lines": {
                "count": report.false_heading_lines, "pages": len(report.false_heading_pages),
                "examples": sorted(report.false_heading_pages)[:MAX_REPORT_EXAMPLES]},
            "residue_outside_code_fences": residue,
            "largest_file": {"name": largest[0], "bytes": largest[1]},
            "files_over_500_kb": sorted(n for n, b in report.file_sizes.items() if b > MAX_FILE_BYTES),
        },
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    return data


# ---------------------------------------------------------------------------
# Dump processing
# ---------------------------------------------------------------------------

def _iter_pages(input_xml):
    """(title, namespace, wikitext, is_redirect_element, redirect_target) for every page of the dump."""
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

        text_elem = revision.find(f"{ns}text")
        redirect_elem = elem.find(f"{ns}redirect")
        yield (title_elem.text or "", int(ns_elem.text or "-1"),
               text_elem.text if text_elem is not None else "",
               redirect_elem is not None,
               redirect_elem.get("title") if redirect_elem is not None else None)

        # Free memory
        elem.clear()


def load_template_map(input_xml):
    """First pass: every Template namespace page of the dump."""
    texts = {}
    redirects = {}
    for title, page_ns, wikitext, _, redirect_target in _iter_pages(input_xml):
        if page_ns != TEMPLATE_NAMESPACE:
            continue
        texts[title] = wikitext or ""
        if redirect_target:
            redirects[title] = redirect_target
    return TemplateMap(texts, redirects)


def process_dump(input_xml, output_dir, test_titles=None):
    """Process the MediaWiki XML dump and write text files."""
    started = datetime.datetime.now()
    os.makedirs(output_dir, exist_ok=True)

    index = {}  # filename -> {title, namespace}
    used_filenames = set()  # Track filenames (lowercased) to handle case collisions
    processed = 0
    skipped = 0
    redirects = 0
    errors = 0
    skip_counts = Counter()
    report = Report()

    print(f"Processing: {input_xml}")
    print(f"Output to:  {output_dir}")
    if test_titles:
        print(f"Test mode:  only processing titles matching: {test_titles}")
    print()

    template_map = load_template_map(input_xml)
    print(f"Template map: {len(template_map)} templates")
    banner_lines = set()
    for kind in ("variant", "nethack"):
        for name in template_map.banner_classification()[kind]:
            banner_lines.add(clean_inline(template_map.banner(name)[1]))

    for title, page_ns, wikitext, redirect_element, _ in _iter_pages(input_xml):
        # Filter namespaces
        if page_ns not in INCLUDED_NAMESPACES:
            skipped += 1
            if page_ns in FORMERLY_CONVERTED_NAMESPACES and wikitext and not redirect_element \
                    and not re.match(r"(?i)^\s*#\s*redirect", wikitext):
                skip_counts[f"namespace_{FORMERLY_CONVERTED_NAMESPACES[page_ns]}"] += 1
            continue

        # Filter by test titles if specified
        if test_titles and not any(t.lower() in title.lower() for t in test_titles):
            continue

        if not wikitext:
            continue

        # Skip redirect pages — they contain no useful content
        if redirect_element or re.match(r"(?i)^\s*#\s*redirect", wikitext):
            redirects += 1
            continue

        reason, detail = classify_page(title, wikitext, template_map)
        if reason:
            skip_counts[reason] += 1
            if reason == "variant_only":
                skip_counts[f"variant_only_{detail}"] += 1
                report.skipped_titles["variant_only"][detail].append(title)
            else:
                report.skipped_titles[reason].append(title)
            continue
        if detail:
            report.kept_by_exception[detail].append(title)

        ns_label = INCLUDED_NAMESPACES[page_ns]

        try:
            stats = ConversionStats()
            stat_blocks, content = convert_wikitext_to_plaintext(wikitext, title, template_map, stats)

            summary = make_summary(content, banner_lines)

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
            filename = unique_filename(title, used_filenames)

            filepath = os.path.join(output_dir, filename)
            with open(filepath, "w", encoding="utf-8") as f:
                f.write(output_text)

            index[filename] = {"title": title, "namespace": ns_label}
            processed += 1

            body = "\n\n".join(stat_blocks + [content])
            report.add_page(title, filename, stats, body, wikitext, len(output_text.encode("utf-8")))
            report.kept_articles.append(
                (title, page_categories(wikitext), page_banners(wikitext, template_map), content))

            if processed % 500 == 0:
                print(f"  Processed {processed} pages...")

        except Exception as e:
            errors += 1
            print(f"  ERROR processing '{title}': {e}", file=sys.stderr)

    # Write index
    index_path = os.path.join(output_dir, "_index.json")
    with open(index_path, "w", encoding="utf-8") as f:
        json.dump(index, f, indent=2, ensure_ascii=False)

    counts = {
        "written": processed,
        "redirects": redirects,
        "errors": errors,
        "skipped": {
            "namespace": skipped,
            "formerly_converted_namespaces": {
                label: skip_counts[f"namespace_{label}"] for label in FORMERLY_CONVERTED_NAMESPACES.values()},
            "non_english": skip_counts["non_english"],
            "variant_only": skip_counts["variant_only"],
            "variant_only_by_rule": {r: skip_counts[f"variant_only_{r}"]
                                     for r in ("title_suffix", "subpage", "categories", "banners")},
            "non_game": skip_counts["non_game"],
        },
    }
    report_path = os.path.join(output_dir, "_conversion_report.json")
    data = write_report(report_path, report, template_map, input_xml, counts, started)

    q = data["quality"]
    print()
    print(f"Done! Written: {processed}, Skipped (wrong ns): {skipped}, Redirects: {redirects}, "
          f"Errors: {errors}")
    print(f"Formerly converted namespaces skipped: {counts['skipped']['formerly_converted_namespaces']}")
    print(f"Skipped: non-English {skip_counts['non_english']}, variant-only {skip_counts['variant_only']} "
          f"{counts['skipped']['variant_only_by_rule']}, non-game {skip_counts['non_game']}")
    print(f"Tables: {report.tables} (layout {report.layout_tables}), preformatted blocks: {report.preformatted}, "
          f"transclusions: {sum(report.transcluded.values())}")
    print(f"Dropped templates: {sum(report.dropped_occurrences.values())} occurrences of "
          f"{len(report.dropped_occurrences)} names")
    print(f"Empty sections: {q['empty_sections']['count']} on {q['empty_sections']['pages']} pages; "
          f"heading-shaped non-heading lines: {q['heading_shaped_non_heading_lines']['count']}")
    print("Residue outside code fences: " + ", ".join(
        f"{k} {v['occurrences']}" for k, v in q["residue_outside_code_fences"].items()))
    print(f"Largest file: {q['largest_file']['name']} ({q['largest_file']['bytes']} bytes); "
          f"over 500 KB: {len(q['files_over_500_kb'])}")
    print(f"Index written to: {index_path}")
    print(f"Report written to: {report_path}")

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
