# Verifier Caution Ledger

Every recorded instance of the claim verifier returning a wrong verdict, and the shapes they fall
into. Open this file when an analysis meets a verifier verdict it doubts, when it numbers a new
instance, or when it considers a new claim-verification instruction. It is linked from
[`../SKILL.md`](../SKILL.md) § 11 (Standing Cautions) and § 12.

**New instances are appended here**, one row each in the Instances table, in number order, and the
instance number is added to its row in the Shapes table (or a new shape row is added).

Reading the tables:

- Where the source names several instances together ("twelfth and thirteenth …"), the numbers are
  assigned in the order the source lists the items.
- A file:line marked † is given with the shape; the source does not say whether the verdict cited
  it or it is the code that settles the claim.
- **Caught today?** is *Yes — note* where a deterministic harness check adds a note and counts the
  verdict as Indeterminate, *Yes — structured* where a structured verdict field decides it, *Prompt
  only* where a claim-verification instruction targets the shape and no check is described, *No* where
  the source says the shape is not caught, stays a caution, or the instruction did not hold, and
  *Not stated* where the source records no response.

## Instances

| # | Run, question | Verifier model @ level | Shape | Citation the verdict used | The code that settles it | Harness response (instruction 3a–3p, deterministic note, or none) | Caught today? |
|---|---|---|---|---|---|---|---|
| 1 | run 24, Q1 (presumed: the source does not number the run-24 pair) | Not stated | Refuted *"Gnolls can be played by the following six roles"*: the wrong array element (`races[]` instead of `roles[]`); recorded and flagged as `BenchmarkAnswerFlags.RefutedClaim` | `src/role.c:1228`, a line inside the **`races[]`** array, the Gnoll *race* entry's alignment mask | `MH_GNOLL` in exactly six `roles[]` entries: Barbarian (`:141`), Caveman/Cavewoman (`:220`), Healer (`:299`), Priest/Priestess (`:540`), Rogue (`:621`), Ranger (`:713`) | None: "No code fix applies" | No — caution |
| 2 | run 24, Q1 (presumed, as 1) | Not stated | Refuted the claimed Yeenaghu peace and wish: a default flag cited, the overriding special case missed; recorded and flagged as `BenchmarkAnswerFlags.RefutedClaim` | `M2_HOSTILE` on `src/monst.c:5669` | `peace_minded()` (`src/makemon.c:4451`) returns `TRUE` for Yeenaghu and for `PM_HYENA` against a gnoll player unconditionally, ahead of `always_peaceful()` and every alignment test; `src/minion.c:799-830` grants a real wish through `mongrantswish()` (chaotic with alignment record ≥ 14, or a luck roll for a chaotic or neutral gnoll, or carrying the Howling Flail; `context.yeenaghu_wishes` makes repeats rarer) | None: "No code fix applies" | No — caution |
| 3 | Not identified in the source | — | — | — | — | — | — |
| 4 | run 30, Q11 (trident range) | Not stated | Not described | `src/apply.c:5233` † | — | None stated | Not stated |
| 5 | run 33, Q1 (wish-repeat odds) | Not stated | Not described | — | — | None stated | Not stated |
| 6 | run 35, Q16 (group size) | Not stated | An `#if 0` citation | Not given | — | None stated | Not stated |
| 7 | run 35, Q7 | Not stated | *"d20-style save"* wrongly marked Supported | — | — | None stated | Not stated |
| 8 | run 36, Q7 (fear-skill save table) | Opus @ `low` (claim-verifier `low`) | Refuted against a wiki table that omits the spell's extra per-skill-level term | The wiki table | `src/zap.c:949` | Instruction 3a (harness 20): a claim about how a spell, attack or effect is computed is checked in the code that implements it | Prompt only |
| 9 | run 36, Q14 (−4 magic-cancellation penalty) | Opus @ `low` (claim-verifier `low`) | Refuted against a data field instead of the code that applies it | Master Kaen's `mcadj` data field | `src/mcastu.c:793` | Instruction 3a (harness 20) | Prompt only |
| 10 | run 42, Q15 | Gemini 3.7 Flash @ `high` | **Substituted subject**: the claim "after the glyph callback, it separately sends object records … engraving data" refuted on the basis that *layer* information is not sent separately, a claim the candidate did not make | `include/winprocs.h:58` | `lib_print_glyph` in `win/win32/xpl/libshare/libproc.c` calls `callback_print_glyph` at `:494`, then `callback_send_object_data` at `:580` and `:599` and `callback_send_engraving_data` at `:613` | None; reading rule: read what the verifier says it refuted before reading its citation | Not stated |
| 11 | run 48, Q13 (fire resistance halves fire damage) | Claude 5 Sonnet @ `high` | Refuted against the code that *grants* the intrinsic, not the code that applies it | `src/cmd.c:4579`, the enlightenment string for the half-damage tier | The resistance rolls in `src/zap.c` | Instruction 3b (harness 28): a magnitude or tier of a resistance or property is checked where the property is applied | Prompt only |
| 12 | run 52, Q6 | GPT-5.6 Luna @ `xhigh` | Data or description instead of the applying code: *"blessing supplies only +1 max HP"*; not a contest, so not in the harness-30 evidence-informed re-grade | Not given | The healing roll `d(12 + 6*bcsign, 6)`, 18d6 blessed (`src/potion.c:966-969`) | 3a and 3b already targeted it | Prompt only (3a, 3b in place) |
| 13 | run 52, Q3 | GPT-5.6 Luna @ `xhigh` | Data or description instead of the applying code: *"the camera only frightens"*; not a contest, so not in the harness-30 re-grade | Not given | Blinds adjacent monsters and scares within distance (`src/uhitm.c:4951-4977`) | 3a and 3b already targeted it | Prompt only (3a, 3b in place) |
| 14 | run 54, Q12 | GPT-5.6 Luna @ `xhigh` | Refuted from **unreachable code** | `priest_talk`, whose only callers are commented out at `src/sounds.c:1123` and `:7609` | — | Instruction 3d (harness 32): confirm a cited function has a live call site; from harness 33 the deterministic liveness note | Yes — note |
| 15 | run 54, Q11 | GPT-5.6 Luna @ `xhigh` | **Supported** from a wiki page alone | A wiki page | The timer code that applies the number (`src/timeout.c:303-349`) | Instruction 3e (harness 32): a number is settled in the code that applies it; a wiki page alone does not settle it while the source is searchable | Prompt only |
| 16 | run 55, Q12 | GPT-5.6 Luna @ `xhigh` | Refuted again from `priest_talk`: 3d did not hold | `priest_talk` | — | `BenchmarkCitationLivenessCheck` (harness 33): *"cited function <name> has no live call site"*, Indeterminate | Yes — note |
| 17 | run 55, Q12 (second refutation) | GPT-5.6 Luna @ `xhigh` | As 16 | `priest_talk` | — | As 16 | Yes — note |
| 18 | run 55, Q5 | GPT-5.6 Luna @ `xhigh` | Refuted *"lizard corpses for confusion"* while citing the very lines that implement it | `src/eat.c:1670-1674` | The cited lines themselves | None: the liveness check does not catch a wrong verdict that cites live code | No |
| 19 | run 56, Q3 | GPT-5.6 Luna @ `high` | Camera blinding refuted from `use_camera` without reading the function it hands the effect to | `use_camera` | `flash_hits_mon` (`src/uhitm.c:4936-4977`) | Instructions 3f–3h (harness 34), named together; 3h: read the function the cited one hands the effect to before concluding that an effect is absent | Prompt only |
| 20 | run 56, Q13 | GPT-5.6 Luna @ `high` | A zero-based skill index read as a different formula | — | `src/spell.c:5267-5281` † | 3f–3h (harness 34); 3g: before refuting a formula, a table or a number, check for the same quantity in another notation and recompute the claim's own worked example | Prompt only |
| 21 | run 56, Q13 (second refutation) | GPT-5.6 Luna @ `high` | As 20 | — | As 20 | As 20 | Prompt only |
| 22 | run 56, Q14 | GPT-5.6 Luna @ `high` | Accused sentence refuted from the general `can_advance` code | The general `can_advance` code | The Monk's Martial arts has no experience-level route (`src/role.c:501`) | 3f–3h (harness 34); 3f: facts, not advice: a recommendation is refuted only through a false mechanic it states | Prompt only |
| 23 | run 57, Q7 | Gemini 3.8 Flash @ `medium` | A function header cited as if it settled the body | Not given | — | None stated | Not stated |
| 24 | run 57, Q11 | Gemini 3.8 Flash @ `medium` | One fact refuted and supported in the same run | — | — | None stated | Not stated |
| 25 | run 57, Q14 | Gemini 3.8 Flash @ `medium` | A sequence read as a present-tense claim | — | — | None stated | Not stated |
| 26 | run 59, Q18 | Gemini 3.8 Flash @ `medium` | The verifier's own cited table misread | Not given | — | None stated | Not stated |
| 27 | run 59, Q8 | Gemini 3.8 Flash @ `medium` | A substituted subject | — | — | None stated | Not stated |
| 28 | run 57, Q1 | Gemini 3.8 Flash @ `medium` | A cited file that does not exist in the indexed source | Not given | — | Harness 35 note *"cited file <path> is not in the indexed source"*, Indeterminate | Yes — note |
| 29 | run 61, Q11 | Gemini 3.8 Flash @ `medium` | A lizard corpse's 13 turns of resistance refuted from the post-effect (siding with a NetHack assumption) | `src/eat.c:1641` | The grant at `:1141-1151` | Instruction 3i (harness 36): absence needs more than one place; rule out the pre-effect, the post-effect, the callers and the shared checks | No — a line inside the wrong live function is not caught |
| 30 | run 61, Q11 | Gemini 3.8 Flash @ `medium` | Magic cancellation against a cockatrice's touch refuted from one function of the attack path | `src/mhitu.c:1340` | True at `:1869-1870`, `:2429-2437` | Instruction 3i (harness 36) | No — as 29 |
| 31 | run 61, Q11 | Gemini 3.8 Flash @ `medium` | Accused *"a failed read does not paralyze"* refuted from a function's definition line, a function about casting | `src/spell.c:5186`, the definition line of `percent_success` | — | Harness 36 note *"cited line <path>:<line> is only the definition line of <function>"*, Indeterminate | Yes — note |
| 32 | run 61, Q16 | Gemini 3.8 Flash @ `medium` | A success claim refuted from the failure path | `src/spell.c:484` | The success at `:640-646` | None named (3i was named for the shapes of 29 and 30) | No — as 29 |
| 33 | run 60, Q7 | Gemini 3.8 Flash @ `medium` | A refutation citing a bare file with no line | A bare `src/shk.c` or `src/spell.c` (the source does not say which) | — | Harness 36 note *"cited file <path> without a line"*, Indeterminate; instruction 5 says it is not a citation | Yes — note |
| 34 | run 60, Q7 | Gemini 3.8 Flash @ `medium` | As 33 | As 33 | — | As 33 | Yes — note |
| 35 | run 60, Q7 | Gemini 3.8 Flash @ `medium` | A **wrong Supported** for *"can fail or paralyze you"*, whose paralysis exists only inside `#if 0`; it made a validated evidence-informed re-grade withdraw a right charge | As 33 | — | As 33 | Yes — note |
| 36 | run 63, Q6 | Gemini 3.8 Flash @ `medium` | A dispatch block read as the settling code: *"healing potions restore 12d6 HP; the blessed one also cures sickness and can increase maximum HP by 1"* refuted | `src/potion.c:1784-1793`, the `case POT_HEALING:` block, which only passes values computed at the top of the same function | The dice from `src/objects.c:3313-3317` and `src/potion.c:966-971`, the blessed cure from `:1001-1003`, the extra maximum HP at `:1789` | Instruction 3j (harness 37): values passed are settled where they are assigned; read where the variables a cited call passes are computed, and the object's data entry | Prompt only |
| 37 | run 64, Q11 | Gemini 3.8 Flash @ `medium` | A Supported verdict covering a true clause beside the charged one: the critical-error quote Supported on its read-ability clause | `src/spell.c:889-894` | The charged part, "keep a casting ready", the same verifier refuted on another item | Charged part line, preambles and instruction 3k (harness 38); structured `chargedPartVerdict` (harness 39) | Yes — structured (accused items with a charged part) |
| 38 | run 65, Q1 | Gemini 3.8 Flash @ `medium` | As 37: *"dwarves are among the creatures your lawful alignment is expected to respect"* Supported; the validated evidence-informed withdrawal it fed withdrew a true charge | `src/mon.c:4935-4953`, the general penalty for angering any peaceful, which has no dwarf rule | — | As 37 | Yes — structured |
| 39 | run 65, Q8 | Gemini 3.8 Flash @ `medium` | As 37: *"you currently lack a reliable … escape option"* Supported; its validated evidence-informed withdrawal withdrew a true charge | The board's *Controlled teleport* line | The hero carries a wand of digging | As 37 | Yes — structured |
| 40 | run 66, Q13 | Gemini 3.8 Flash @ `medium` | A macro's definition line cited as evidence: *"these four schools are non-somatic"* refuted | `src/objects.c:3549`, a parameter line of the `#define SPELL(…)` header | The somatic flag is uniform per school (Clerical 18/18, Healing 10/10, Divination 19/19, Abjuration 24/24, Movement 18/18) | Harness 39 note *"cited line <path>:<line> is only the definition of macro <NAME>"*, Indeterminate | Yes — note |
| 41 | run 67, Q6 | Gemini 3.8 Flash @ `medium` | A dispatcher read as the settling code: *"healing casts do not consume a turn"* refuted | `docast` (`src/spell.c:1538-1548`), which only returns `spelleffects`' result | Minor healing carries `S1_DOES_NOT_TAKE_A_TURN` (`src/objects.c:4523-4526`); the wiki page the candidate read says "Casting time: 0 rounds" | Instruction 3l (harness 39): a verdict that contradicts a GnollHack wiki statement names that statement and the code that overrides it | Prompt only |
| 42 | run 67, Q7 | Gemini 3.8 Flash @ `medium` | A Supported verdict on the uncharged clause, although harness 38 printed the charged part (the harness-38 prose did not hold); one of run 67's validated evidence-informed withdrawals rests on it | `src/read.c:2786`, which settles only the uncharged clause | — | Structured `chargedPartVerdict` (harness 39); omitted means Indeterminate, with the note *"the charged part was not judged separately"* | Yes — structured |
| 43 | run 67, Q15 | Gemini 3.8 Flash @ `medium` | A constant's name read as the claim's meaning: *"The Grail recharges itself over 1,500 turns"* Supported; one of run 67's validated evidence-informed withdrawals rests on it | `include/artilist.h:331-336`, the constant `ARTINVOKE_RECHARGE_ITSELF` | — | None: caught by none of the harness-39 changes | No — caution |
| 44 | run 68, Q1 | Gemini 3.8 Flash @ `medium` | Substituted subject | — | — | Not stated per instance (the harness-41 transfer lists "citation notes") | Not stated |
| 45 | run 68, Q7 | Gemini 3.8 Flash @ `medium` | Substituted subject | — | — | As 44 | Not stated |
| 46 | run 72, Q14 | Gemini 3.8 Flash @ `medium` | Substituted subject | — | — | As 44 | Not stated |
| 47 | run 68, Q7 | Gemini 3.8 Flash @ `medium` | A citation into an unrelated function | `poly_obj` | — | As 44 | Not stated |
| 48 | run 68, Q7 (second) | Gemini 3.8 Flash @ `medium` | As 47 | `poly_obj` | — | As 44 | Not stated |
| 49 | run 68, Q17 | Gemini 3.8 Flash @ `medium` | Over-literal board arithmetic | — | — | As 44 | Not stated |
| 50 | run 68, Q11 | Gemini 3.8 Flash @ `medium` | Scope | — | — | As 44 | Not stated |
| 51 | run 69, Q4 | Gemini 3.8 Flash @ `medium` | A board disclaimer read as a refutation | — | — | As 44 | Not stated |
| 52 | run 70, Q12 | Gemini 3.8 Flash @ `medium` | A governing `else if` missed | — | — | As 44 | Not stated |
| 53 | run 70, Q18 | Gemini 3.8 Flash @ `medium` | A truncated menu-builder range | — | — | As 44 | Not stated |
| 54 | run 70, Q7 | Gemini 3.8 Flash @ `medium` | A true clause beside a false one | — | — | As 44 | Not stated |
| 55 | run 70, Q12 | Gemini 3.8 Flash @ `medium` | A substituted implication | — | — | As 44 | Not stated |
| 56 | run 74, Q9 | Gemini 3.8 Flash @ `medium` | A function read without its callers, the shape 3i targets, which did not hold: *"the wand of digging may work by zapping down on the grave"* refuted | `src/dig.c:1110-1116` (only applying a digging tool exhumes a grave) | A downward zap calls `dighole(FALSE, TRUE, …)` (`src/dig.c:2085`), whose grave branch digs a pit and calls `dig_up_grave` (`:1003-1005`) | None: the shape stays a caution | No — caution |
| 57 | run 74, Q12 | Gemini 3.8 Flash @ `medium` | A substituted subject behind a pronoun: *"It's cheapest before you level up again."* reached the verifier without the sentence *"It"* refers back to | The divination price (`src/sounds.c:7740`) | The protection donation, whose price `2500 + 150 × level` (`src/sounds.c:7613`) is lower before a level-up | Harness 43: a pronoun-led claim item reaches the verifier with the preceding answer sentence as context | Not stated |
| 58 | run 74, Q14 | Gemini 3.8 Flash @ `medium` | An attribution claim judged as a claim about the mechanic: *"the wiki's Skills overview page says Bare-handed combat must be Grand Master before you can advance Martial arts"* refuted (that the page is wrong is a separate finding, the run-74 round's wiki handoff W1) | `can_advance` | The page says exactly that (`Skills.md:28`) | Instruction 3m (harness 43): a sentence that reports what a source says is checked against that source, not against the mechanic the source describes | Prompt only |
| 59 | run 79, Q6 (battery run 2) | Gemini 3.8 Flash @ `medium` | A branch inside a `case` whose labels exclude the item | `src/zap.c:7371-7378` † | — | None stated | Not stated |
| 60 | run 79, Q7 (battery run 2) | Gemini 3.8 Flash @ `medium` | A branch read without its guard | `src/apply.c:4505` † | — | None stated | Not stated |
| 61 | run 78, Q9 (battery run 2) | Gemini 3.8 Flash @ `medium` | A wiki hedge read as a code exception | — | — | None stated | Not stated |
| 62 | run 80, Q13 (battery run 3) | Gemini 3.8 Flash @ `medium` | A descriptive adjective read as a claimed species name ("adult red dragon") | — | — | None: "No new verifier instruction" | No — caution |
| 63 | run 80, Q16 (battery run 3) | Gemini 3.8 Flash @ `medium` | A function's variable names read as its semantics: `minmlev`/`maxmlev` refuted as "difficulty ratings" without reading where the bounds are compared | `src/makemon.c:35-36` † | — | None: "No new verifier instruction" | No — caution |
| 64 | run 83, Q6 (battery run 4) | Not stated | The dead `GRAIL_OF_HEALING` lines | The dead `GRAIL_OF_HEALING` lines | — | None stated | Not stated |
| 65 | run 86, Q18 (battery runs 6–7) | Not stated | `onscary` read without its caller `mfndpos` | `onscary` | `mfndpos` | None stated | Not stated |
| 66 | run 97, Q14 (battery run 11) | Gemini 3.8 Flash @ `medium` | A substituted threshold (Master's 320 for Grand Master's 500) | `src/weapon.c:2127-2130` † | — | None stated | Not stated |
| 67 | run 97, Q17 (battery run 11) | Gemini 3.8 Flash @ `medium` | A recomputation from the wrong baseline despite instruction 3g | `src/spell.c:5281-5282` † | — | None stated; 3g did not hold | No |
| 68 | run 97, Q18 (battery run 11) | Gemini 3.8 Flash @ `medium` | The first component of a `matcomps.c` entry read as the whole entry ("protection needs no wood") | `src/matcomps.c:2607-2621` † | — | None stated | Not stated |
| 69 | run 97, Q15 (battery run 11) | Gemini 3.8 Flash @ `medium` | A hedge ("about 1500 turns") refuted as false | — | — | None stated | No — run 99 Q15 (battery run 12) again refuted the same hedge |
| 70 | run 125, Q11 | Gemini 3.8 Flash @ `medium` | A sentence refuted from one branch of an `if … else if` chain, missing the sibling branch that makes it partly true | `src/eat.c:3806-3828` | `src/eat.c:3829-4002`, the corpse confirmation (`ParanoidCorpse`, on by default through `flags.paranoia_bits` at `src/options.c:871`), which warns a character who has neither edibility appraisal nor active blessed food detection about a corpse's or glob's known dangers and uncertain freshness | None; instruction 3n names a `switch` or `if` condition but not its sibling branches | No |

## Shapes

| Shape | Instances | Count | Current guard |
|---|---|---|---|
| Not identified | 3 | 1 | — |
| A default flag or the wrong array element cited, the special-case code that overrides it missed | 1, 2 | 2 | **No** (caution only): "No code fix applies"; a refutation about GnollHack-specific racial behavior is unverified until a human reads the overriding code path |
| Not described beyond the item (7 is a wrong Supported) | 4, 5, 7 | 3 | Not stated |
| Compiled-out or dead lines cited (`#if 0`, dead `GRAIL_OF_HEALING` lines) | 6, 64 | 2 | Not stated |
| A data table, data field, description or granting code instead of the applying code | 8, 9, 11, 12, 13 | 5 | Prompt only: 3a (harness 20), 3b (harness 28); 12 and 13 occurred with both in place |
| Substituted subject: the verdict answers a claim the candidate did not make | 10, 27, 44, 45, 46, 57 | 6 | Reading rule from 10: treat a refutation whose basis names a different thing than the claim as unverified. For a pronoun-led item (57), harness 43 sends the preceding answer sentence as context |
| A cited function with no live call site | 14, 16, 17 | 3 | **Yes — note**: 3d (harness 32) and `BenchmarkCitationLivenessCheck` (harness 33). The stored verdict text is kept and both are printed. It cannot see a function reached only through a function pointer or a macro, so it demotes to Indeterminate and never to the opposite verdict. From harness 34 it needs a definition followed by a body (a macro row in a data table is no function) and counts a value reference such as `set_occupation(learn, …)` as live |
| Supported from a wiki page alone against the applying code | 15 | 1 | Prompt only: 3e (harness 32) |
| Refuted while citing the lines that implement the claim | 18 | 1 | **No**: the liveness check does not catch a wrong verdict that cites live code |
| An effect judged absent without reading the function the cited one hands it to | 19 | 1 | Prompt only: 3h (harness 34) |
| The same quantity in another notation read as a different formula | 20, 21 | 2 | Prompt only: 3g (harness 34) |
| An accused sentence refuted from general code where the specific rule has no such route | 22 | 1 | Prompt only: 3f–3h (harness 34), named together |
| A function header cited as if it settled the body | 23 | 1 | Not stated |
| One fact refuted and supported in the same run | 24 | 1 | Not stated |
| A sequence read as a present-tense claim | 25 | 1 | Not stated |
| The verifier's own cited table misread | 26 | 1 | Not stated |
| A cited file that is not in the indexed source | 28 | 1 | **Yes — note** (harness 35) |
| Absence concluded from one place: the post-effect, one function of the attack path, the failure path | 29, 30, 32 | 3 | Prompt only: 3i (harness 36), named for 29 and 30. **No** deterministic guard: a wrong verdict that cites a line inside the wrong live function is not caught |
| A function's definition line cited | 31 | 1 | **Yes — note** (harness 36); a range is never noted as a definition line; instruction 5 says it is not a citation |
| A bare file cited without a line | 33, 34, 35 | 3 | **Yes — note** (harness 36); instruction 5 says it is not a citation; a wiki or board citation beside a bare file keeps the verdict |
| A dispatch block or dispatcher read as the settling code | 36, 41 | 2 | Prompt only: 3j (harness 37); 41 occurred after 3j, and 3l (harness 39) was among that round's responses |
| A Supported verdict covering a true clause beside the charged one (54 is described as "a true clause beside a false one"). It differs from the substituted subject: here the verifier answers part of the claim made and ignores the part that was charged | 37, 38, 39, 42, 54 | 5 | **Yes — structured** for an accused item with a charged part: `chargedPartVerdict` (harness 39). The harness-38 *Charged part* line, preambles and 3k did not hold on 42 |
| A macro's definition line cited as evidence | 40 | 1 | **Yes — note** (harness 39) |
| A constant's name read as the claim's meaning | 43 | 1 | **No** (caution only): caught by none of the harness-39 changes |
| A citation into an unrelated function | 47, 48 | 2 | Not stated |
| Over-literal board arithmetic | 49 | 1 | Not stated |
| Scope | 50 | 1 | Not stated |
| A board disclaimer read as a refutation | 51 | 1 | Not stated |
| A governing `else if` missed | 52 | 1 | Not stated |
| A truncated menu-builder range | 53 | 1 | Not stated |
| A substituted implication | 55 | 1 | Not stated |
| A function read without its callers (65: `onscary` read without its caller `mfndpos`) | 56, 65 | 2 | **No** (caution only): 3i targets it and did not hold; the shape "stays a caution" |
| An attribution claim judged as a claim about the mechanic | 58 | 1 | Prompt only: 3m (harness 43) |
| A branch inside a `case` whose labels exclude the item | 59 | 1 | Not stated |
| A branch read without its guard | 60 | 1 | Not stated |
| A wiki hedge read as a code exception | 61 | 1 | Not stated |
| A descriptive adjective read as a claimed species name | 62 | 1 | **No** (caution only): no new verifier instruction; "one more instance of either shape would justify a 3p" |
| A function's variable names read as its semantics | 63 | 1 | **No** (caution only): as 62 |
| A substituted threshold | 66 | 1 | Not stated |
| A recomputation from the wrong baseline | 67 | 1 | **No**: occurred despite 3g |
| The first component of an entry read as the whole entry | 68 | 1 | Not stated |
| A hedge refuted as false | 69 | 1 | **No**: recurred on run 99 Q15 (battery run 12), not numbered |
| One branch of a conditional chain read as the whole behavior | 70 | 1 | Not stated |

Instruction 3c (harness 29) sends the board to the claim verifier and allows `board:` citations; it
answers no instance. Instruction 3p, as § 11's battery run 4 row names it, is the claim-verifier
batching instruction (harness 49). Instructions 3n and 3o are in
`Overseer/Services/Benchmarking/BenchmarkClaimVerificationPrompt.cs`. 3n says that a line inside a
`switch` is reached only for that `case`'s labels and a line inside an `if` only when its condition
holds, so the `case` labels and the `if`/`else` conditions above a line are read before it is cited
as what an item does; the shapes of 59 and 60 are the ones it describes. 3o says that a wiki page's
hedge (usually, often, typically, in most cases) is not evidence that exceptions exist, and that a
claim is judged by the code when the code shows the rule without an exception. Neither covers a
sibling branch of an `if … else if` chain that makes a refuted sentence partly true (70), and 3o
speaks of a wiki page's hedge, not of a hedge in the candidate's answer (69).

## The Rule That Binds

> **A refutation is advisory evidence, and a human reads the cited line before any change rests on it.**

> **No code fix applies** — the verifier is a model, its output is advisory and folded into no index, and that containment is what limited the damage — but a refutation about GnollHack-specific racial behaviour should be treated as **unverified until a human reads the overriding code path**, not as evidence.

> The failure mode here is not a wrong citation but a **substituted subject**: read what the verifier says it refuted before reading its citation, and treat a refutation whose basis names a different thing than the claim as unverified.

On the harness-30 evidence-informed re-grade:

> Its human-read check on each `withdrawn` list is still the guard against a wrong verdict flowing the other way.
