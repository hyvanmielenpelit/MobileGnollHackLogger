"""Unit tests for convert_nethackwiki_dump_md.py; synthetic wikitext only, no dump needed.

Run from the repository root:
    python -m unittest discover -s Overseer/Scripts/NetHackWiki -p "test_*.py"
"""

import os
import re
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import convert_nethackwiki_dump_md as conv  # noqa: E402


def _skill_table(first_cell):
    return (
        "<!-- GENERATED PAGE; DO NOT EDIT DIRECTLY -->\n"
        "{| class=\"prettytable\"\n"
        "! {{diagonal split header|[[Skill|Skill]]|[[Role|Role]]}} !! [[Archeologist|Arc]] !! [[Valkyrie|Val]]\n"
        "|-\n"
        f"|[[{first_cell}]]\n"
        "|class=\"basic\"| b\n"
        "|class=\"expert\"| '''E'''\n"
        "|}\n"
    )


TEMPLATES = {
    "Template:Weapon skill table": _skill_table("dagger"),
    "Template:Combat skill table": _skill_table("riding"),
    "Template:Spell skill table": _skill_table("attack spells|attack"),
    "Template:Broken skill table": "{{#if:{{{1|}}}|a|b}}\n{| class=\"prettytable\"\n! A\n|}",
    "Template:Items": "{| class=\"navbox\"\n! Items\n|-\n| [[Weapon]] · [[Armor]]\n|}",
    "Template:Version icon": "<includeonly>{{top icon|description={{{description}}}}}</includeonly>",
    "Template:Nethack-367": (
        "{{version icon\n| imagename = nh367-icon.png\n"
        "| description = This article has been updated to reflect NetHack 3.6.7.\n}}\n"
        "[[Category:Nethack-367 articles]]"
    ),
    "Template:Slashem-7E7": (
        "{{version icon\n| imagename = se7e7-icon.png\n"
        "| description = This article may require revision for the next version of SLASH'EM.\n}}"
    ),
    "Template:Variant-343": (
        "{{version icon\n| imagename = nh343var-icon.png\n"
        "| description = This article describes a 3.4.3-based variant of NetHack.\n}}"
    ),
    # Named like a NetHack banner, described like a variant one
    "Template:Nethack-999": (
        "{{version icon\n| description = This article describes a variant of NetHack.\n}}"
    ),
    "Template:Spellbook of": "<includeonly>{{of|spellbook|{{{1}}}}}</includeonly>",
    "Template:+ of": "#REDIRECT [[Template:Spellbook of]]",
}


def tmap():
    return conv.TemplateMap(TEMPLATES)


def convert(wikitext, title="Test", template_map=None, stats=None):
    blocks, content = conv.convert_wikitext_to_plaintext(
        wikitext, title, template_map if template_map is not None else tmap(), stats)
    return blocks, content


def body_of(wikitext, **kwargs):
    blocks, content = convert(wikitext, **kwargs)
    return "\n\n".join(blocks + [content])


def heading_shaped(text):
    return [line for line in text.split("\n") if re.match(r"^(#+)\s+", line)]


class TranscludedSkillTables(unittest.TestCase):
    WIKITEXT = (
        "Intro.\n\n"
        "==Skill tables==\n"
        "===Weapon skills===\n{{weapon skill table}}\n"
        "===Other combat skills===\n{{combat skill table}}\n"
        "===Spell skills===\n{{spell skill table}}\n"
    )

    def test_three_tables_render_under_their_headings(self):
        stats = conv.ConversionStats()
        text = body_of(self.WIKITEXT, stats=stats)
        for heading, first in (("### Weapon skills", "dagger"),
                               ("### Other combat skills", "riding"),
                               ("### Spell skills", "attack")):
            section = text.split(heading, 1)[1].split("###", 1)[0]
            self.assertIn("| Skill \\ Role | Arc | Val |", section)
            self.assertIn("|---|---|---|", section)
            self.assertIn(f"| {first} | b | E |", section)
        self.assertEqual(conv.count_empty_sections(text), 0)
        self.assertEqual(stats.transcluded["Spell skill table"], 1)
        self.assertEqual(stats.dropped, {})

    def test_parser_function_body_falls_back_to_dropped_and_counted(self):
        stats = conv.ConversionStats()
        text = body_of("Before {{broken skill table}} after.", stats=stats)
        self.assertNotIn("| A |", text)
        self.assertEqual(stats.dropped["Broken skill table"], 1)
        self.assertEqual(stats.transcluded, {})

    def test_navbox_is_not_transcluded(self):
        stats = conv.ConversionStats()
        text = body_of("Text.\n{{items}}\n", stats=stats)
        self.assertNotIn("Weapon", text)
        self.assertEqual(stats.dropped["Items"], 1)


class TemplateRendering(unittest.TestCase):
    def test_message(self):
        text = body_of("==Messages==\n{{message|You feel limber!|The [[petrification]] process has been stopped.}}")
        self.assertIn("\"You feel limber!\" — The petrification process has been stopped.", text)

    def test_message_without_explanation(self):
        self.assertIn("\"Who was that Maud person anyway?\"", body_of("{{message|Who was that Maud person anyway?}}"))

    def test_encyclopedia(self):
        text = body_of("==Encyclopedia entry==\n{{encyclopedia|quote=To be\nor not to be?|author=Shakespeare}}")
        self.assertIn("Encyclopedia entry:\n> To be\n> or not to be?\n> — Shakespeare", text)

    def test_guidebook(self):
        text = body_of("{{guidebook|Elves are agile, quick, and perceptive.}}")
        self.assertIn("Guidebook:\n> Elves are agile, quick, and perceptive.", text)

    def test_of_family_through_redirect(self):
        text = body_of("Spells such as {{+ of|light}} or {{+ of|knock}} help.")
        self.assertIn("Spells such as light or knock help.", text)

    def test_of_with_conjunction(self):
        self.assertIn("digging, fire, or lightning", body_of("Use {{of|wand|digging|fire|lightning|or=1}}."))

    def test_monsymlink(self):
        self.assertIn("is the cockatrice.", body_of("Their corresponding monster is the {{monsymlink|cockatrice}}."))

    def test_main_pointer(self):
        self.assertIn("Main article: Fountain dipping", body_of("==Dipping==\n{{main|Fountain dipping}}\nText."))

    def test_redirects_here(self):
        text = body_of("{{redirects-here|\"Master\"|the rank title|Monk rank titles}}\nBody text here.")
        self.assertIn("\"Master\" redirects here. For the rank title, see Monk rank titles.", text)

    def test_davg(self):
        self.assertIn("averages 5 damage", body_of("It averages {{davg|2|4}} damage."))

    def test_version_banner(self):
        text = body_of("Body.\n\n==References==\n{{nethack-367}}")
        self.assertIn("This article has been updated to reflect NetHack 3.6.7.", text)
        self.assertEqual(conv.count_empty_sections(text), 0)

    def test_nested_frac_in_table_cell(self):
        text = body_of("{|\n! Chance\n|-\n| {{frac|2|3}} of the time\n|}")
        self.assertIn("| 2/3 of the time |", text)

    def test_nested_white_in_infobox_parameter(self):
        blocks, _ = convert("{{monster|name=foo|symbol={{white|@}}}}\nText.")
        self.assertIn("Symbol: @", blocks[0])

    def test_refsrc_comment_becomes_note(self):
        self.assertIn("Text. (Note: X)", body_of("Text.{{refsrc|f.c|10|comment=X}}"))

    def test_refsrc_without_comment_is_dropped(self):
        stats = conv.ConversionStats()
        text = body_of("Text.{{refsrc|f.c|10}} More.", stats=stats)
        self.assertIn("Text. More.", text)
        self.assertNotIn("f.c", text)
        self.assertEqual(stats.dropped, {})

    def test_footnote_becomes_note(self):
        self.assertIn("Text. (Note: X)", body_of("Text.{{footnote|X}}"))

    def test_math_kept_as_inline_code(self):
        self.assertIn("is `n+1` here", body_of("The value is <math>n+1</math> here."))

    def test_randomvariable_becomes_stat_block(self):
        blocks, _ = convert("{{randomvariable|name=d(n,x)|graph=3d6.svg|mean=<math>n * (x+1)/2</math>}}\nText.")
        self.assertTrue(blocks[0].startswith("--- Random Variable ---"))
        self.assertIn("Mean: `n * (x+1)/2`", blocks[0])
        self.assertNotIn("3d6.svg", blocks[0])


class TablesAndBlocks(unittest.TestCase):
    def test_colspan_padding_and_pipe_escaping(self):
        text = body_of("{|\n! A !! colspan=2 | B\n|-\n| x || y{{!}}z || w\n|}")
        self.assertIn("| A | B |  |", text)
        self.assertIn("|---|---|---|", text)
        self.assertIn("| x | y\\|z | w |", text)

    def test_space_preformatted_map_is_fenced_aligned_and_walled(self):
        text = body_of("Map:\n\n |....|\n |.<..|\n ------\n\nAfter.")
        self.assertIn("```\n|....|\n|.<..|\n------\n```", text)

    def test_replacecharsblock_map_drops_legend(self):
        wikitext = ("<replacecharsblock>\n<=[[Stairs|{{white|<}}]]\n0={{white|0}}\n\n"
                    "|..<.|\n|  0 |\n</replacecharsblock>")
        text = body_of(wikitext)
        self.assertIn("```\n|..<.|\n|  0 |\n```", text)
        self.assertNotIn("0=0", text)
        self.assertNotIn("Stairs", text)

    def test_replacecharsblock_without_legend_keeps_whole_map(self):
        text = body_of("<replacecharsblock rules=\"ttymap\">\n ------\n |....|\n\n |.<..|\n</replacecharsblock>")
        self.assertIn("------\n |....|\n\n |.<..|", text)

    def test_map_with_backticks_gets_longer_fence(self):
        text = body_of("<pre>\n|--```-+--|\n```...\n</pre>")
        self.assertIn("````\n|--```-+--|\n```...\n````", text)

    def test_rendered_symbols_do_not_join_links(self):
        blocks, _ = convert("{{armor|top={{black|[}}{{blue|[}}[[Image:Black.png]][[Image:Blue.png]]|name=x}}\nText.")
        self.assertIn("Top: [[", blocks[0])
        self.assertNotIn("Image", blocks[0])

    def test_apostrophe_glyph_survives(self):
        self.assertIn("all monster class ''' golems", body_of("Text: all {{monclasslink|'}} golems."))

    def test_unbalanced_quote_markup_does_not_swallow_templates(self):
        text = body_of("==Charisma==\n{{message|You feel charismatic!''|You gained.}}\n"
                       "{{message|You feel repulsive!''|You lost.}}\n\n==Next==\nText.")
        self.assertIn("\"You feel charismatic!\" — You gained.", text)
        self.assertIn("\"You feel repulsive!\" — You lost.", text)

    def test_layout_table_with_map_and_caption(self):
        wikitext = ("{|class=\"prettytable\"\n|\n<div class=\"ttymap\"><replacecharsblock>\n"
                    "@={{white|@}}\n\n---\n|@|\n---\n</replacecharsblock></div>\n|-\n"
                    "|The hero in a closet.\n|}")
        text = body_of(wikitext)
        self.assertIn("```\n---\n|@|\n---\n```\n\nThe hero in a closet.", text)
        self.assertNotIn("|---|", text)
        self.assertFalse(re.search(r"(?m)^\|.*```", text))

    def test_pre_with_hash_lines_is_not_a_heading(self):
        text = body_of("==Examples==\n<pre>\n# Auto pickup only items\nOPTIONS=autopickup\n</pre>")
        self.assertEqual(heading_shaped(text), ["## Examples"])
        self.assertIn(" # Auto pickup only items", text)

    def test_lists(self):
        text = body_of("# first\n# second\n* bullet\n** nested\n#* mixed")
        self.assertEqual(heading_shaped(text), [])
        self.assertNotIn("- -", text)
        self.assertIn("1. first", text)
        self.assertIn("- bullet\n  - nested", text)
        self.assertIn("  - mixed", text)

    def test_ref_contents_and_magic_words_are_removed(self):
        text = body_of("__TOC__\nSkills.<ref>weapon.c, function slots_required</ref> If you lose.")
        self.assertNotIn("slots_required", text)
        self.assertNotIn("__TOC__", text)
        self.assertIn("Skills. If you lose.", text)

    def test_lowercase_image_link_leaves_nothing(self):
        text = body_of("A hole [[image:hole.png|thumb|A hole]] in the floor.")
        self.assertNotIn("hole.png", text)
        self.assertNotIn("image:", text)

    def test_infobox_values_are_clean(self):
        blocks, _ = convert("{{item|tile=[[File:Foo.png]]|name=x|uses=<ul><li>one</li><li>two</li></ul>|"
                            "notes=\n* alpha\n* beta}}\nText.")
        block = blocks[0]
        self.assertNotIn("Foo.png", block)
        self.assertNotIn("Tile", block)
        self.assertIn("Uses: one; two", block)
        self.assertIn("Notes: alpha; beta", block)
        self.assertNotIn("<", block)

    def test_space_preformatted_lines_starting_with_pipe_are_fenced(self):
        text = body_of("Before.\n\n |-----|\n |.....|\n |-----|\n")
        self.assertIn("```\n|-----|\n|.....|\n|-----|\n```", text)

    def test_summary_skips_tables_and_quotes(self):
        _, content = convert("{|\n! Header cell with long text\n|-\n| Cell with long text here\n|}\n"
                             "{{encyclopedia|quote=A long encyclopedia quotation line.}}\n"
                             "The first real sentence of the article. Second one.")
        self.assertEqual(conv.make_summary(content), "The first real sentence of the article.")

    def test_template_free_paragraph_is_unchanged(self):
        wikitext = ("A '''cockatrice''' is a [[monster]] that appears in ''[[NetHack]]''. "
                    "It can [[stoning|stone]] you   on touch.\n\n"
                    "==Generation==\nCockatrices are always hostile.\n")
        _, content = convert(wikitext)
        self.assertEqual(
            content,
            "A cockatrice is a monster that appears in NetHack. It can stone you on touch.\n\n"
            "## Generation\nCockatrices are always hostile.")


class PageFilters(unittest.TestCase):
    def reason(self, title, wikitext="Some English article text about the game.", template_map=None):
        return conv.classify_page(title, wikitext, template_map if template_map is not None else tmap())

    def test_translations_are_skipped(self):
        for title in ("Cockatrice/zh-CN", "Cockatrice/ko", "Cockatrice/ru"):
            self.assertEqual(self.reason(title)[0], "non_english", title)
        self.assertEqual(self.reason("鸡蛇", "鸡蛇是一种怪物。它可以把你变成石头。")[0], "non_english")

    def test_two_letter_english_subpage_is_kept(self):
        self.assertEqual(self.reason("Foo/hh")[0], None)

    def test_only_main_namespace_is_included(self):
        self.assertEqual(set(conv.INCLUDED_NAMESPACES), {0})
        for ns in (4, 12, 14, 100, 110):
            self.assertNotIn(ns, conv.INCLUDED_NAMESPACES)

    def test_variant_only_articles_are_skipped(self):
        self.assertEqual(self.reason("Boojum (EvilHack)"), ("variant_only", "title_suffix"))
        self.assertEqual(self.reason("Tourist quest/dNetHack"), ("variant_only", "subpage"))
        self.assertEqual(
            self.reason("Ruby golem", "Text.\n[[Category:dNetHack monsters]]\n[[Category:Variant items]]"),
            ("variant_only", "categories"))

    def test_vanilla_and_exception_articles_are_kept(self):
        self.assertEqual(self.reason("Orc (monster class)")[0], None)
        self.assertEqual(self.reason("Foo", "Text.\n[[Category:Monsters]]\n[[Category:dNetHack monsters]]")[0], None)
        self.assertEqual(self.reason("Overview (SLASH'EM)", "Text.\n[[Category:Variants]]\n[[Category:SLASH'EM]]"),
                         (None, "variant_overview"))
        self.assertEqual(self.reason("GnollHack", "Text.\n[[Category:GnollHack]]")[0], None)
        self.assertEqual(self.reason("Longsword", "Text.\n[[Category:Slashing weapons]]")[0], None)

    def test_banner_rule(self):
        self.assertEqual(self.reason("Duergar", "A variant monster.\n{{slashem-7E7}}"), ("variant_only", "banners"))
        self.assertEqual(self.reason("Rock", "Text.\n{{variant-343}}\n{{nethack-367}}")[0], None)
        self.assertEqual(self.reason("Gnoll", "In GnollHack, gnolls are a race.\n{{variant-343}}"),
                         (None, "gnollhack"))

    def test_banner_classifier_reads_the_description(self):
        m = tmap()
        self.assertEqual(m.banner("nethack-999")[0], "variant")
        self.assertEqual(m.banner("nethack-367")[0], "nethack")
        self.assertEqual(m.banner("slashem-7E7")[0], "variant")
        self.assertIsNone(m.banner("items"))

    def test_non_game_articles(self):
        self.assertEqual(self.reason("Junethack 2020", "Text.\n[[Category:Tournaments]]")[0], "non_game")
        self.assertEqual(self.reason("Foo", "Text.\n[[Category:Tournaments]]\n[[Category:Monsters]]")[0], None)
        self.assertEqual(self.reason("Sortloot", "Text.\n[[Category:Patches]]"), (None, "non_game_keep"))
        self.assertEqual(self.reason("Main Page")[0], "non_game")


class Filenames(unittest.TestCase):
    def test_quote_and_asterisk_titles_get_distinct_files(self):
        used = set()
        first = conv.unique_filename('"', used)
        second = conv.unique_filename("*", used)
        self.assertEqual(first, "_.md")
        self.assertEqual(second, "__2.md")
        self.assertNotEqual(first.lower(), second.lower())


if __name__ == "__main__":
    unittest.main()
