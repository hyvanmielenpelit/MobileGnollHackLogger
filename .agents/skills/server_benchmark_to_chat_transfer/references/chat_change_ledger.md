# Chat Change Ledger

Every change that reached the Overseer chat from a GnollBench round, and every recorded decision
not to make one. Open this file **before proposing any change** (`../SKILL.md` § 9 rule 4: the
same change is not re-proposed), and add one row per change when a round's full entry in § 11 is
written or retired. It is linked from [`../SKILL.md`](../SKILL.md) § 11 and § 12.

It lists every change at rungs 1 and 3–7 recorded as made or rolled back, one row per round for
rung-2 wiki edits, and every recorded decision not to change. *Round* is the motivating run, then
the run that checked it after `→`; *not recorded* means no later round settled the outcome. A task
directory is under `hyvanmielenpelit/MobileGnollHackLogger/` in the plans repository.

## Changes

| Change | Rung | Round (run) | Criterion, in short | Outcome | Task directory |
|---|---|---|---|---|---|
| `verboseMode: true` as the chat default (T7) | option | 12 | Completeness 83.0 → 83.8 (noise), Accuracy −4.2, model time +28 % | decided against | not named |
| Heading-scoped wiki snippets (T8) | 3 | 12 → 14 | six pre-declared measures; 4 of 6 met | kept | not named |
| `get_monster_stats.md` aligned with `_policy.md` exact-stats routing (T15) | 3 | 14 | — | not recorded | not named |
| Gnoll-race knowledge-base article (T19, T-B, T6) | 1 → 2 | 16–24 | — | superseded: game mechanics go to rung 2 (§ 7) | not named |
| Google stable byte prefix (`BuildChatRequestBody`), implicit caching (T23) | 5 | 22 → 39, 40 | cache read 54.3 % / 51.9 % | kept | not named |
| Explicit Gemini `cachedContents` | 5 | 22; closed at 40 (T3), 2026-09-12 | storage costs more than the discounted reads save; provider parity (§ 3) | decided against | not named |
| `RecommendedModels:Google` → `gemini-3.8-flash` @ `high` (T25) | 6 | 22 | confirming run; the price advantage doubles on 2027-01-01 | outstanding | not named |
| `gemini-3.1-pro-preview` as `RecommendedModels:Google` | 6 | 24 | — | decided against | not named |
| `get_item_stats` Level 1 units; monster `ac`/`mc`/`mr` scales; `source_code_search` matcher contract and near-miss payload; the `[Truncated:` suffix | 3 | 28 → 29 | bare misses 12 → 0, calls 114 → 85, critical errors 2 → 0 | kept | `benchmark_run_28_analysis/` |
| `_policy.md` answer-opening and miss-recovery text | 3 | 28 → 29 | the answer-opening criterion missed | not recorded | `benchmark_run_28_analysis/` |
| Wiki-family near-miss payloads; `wiki_search` 13,000 result-cap floor (rung 4) | 3, 4 | 29 | — | not recorded | `benchmark_run_29_analysis/` |
| `nethack_wiki_search` per-article cap | 4 | 29 | — | superseded by run 36 T2 | `benchmark_run_29_analysis/` |
| `wiki_view` title-collision disambiguation; the wiki indexer skips dot-directories | 3 | 30 | — | not recorded | `benchmark_run_30_analysis/` |
| `FindDefinition` literal pre-filter; the `wiki_view` `.md`-suffix fix | 3 | 30 → 31 | 8.8 s → 79 ms; 8 of 12 `.md` calls resolved | kept | `benchmark_run_30_analysis/` |
| Segmented candidate prompt (H2) | 5 | 30–31 → 32 | cache read 83.3 → 91.5 %, candidate cost $1.35 → $1.03, TTFT 4,120 → 3,185 ms | kept | `benchmark_run_30_analysis/`, `benchmark_run_31_analysis/` |
| `wiki_view` section normalization and heading list | 3 | 31 → 32 | — | kept | `benchmark_run_31_analysis/` |
| Wiki: `Praying.md` | 2 | 32 | — | kept | `benchmark_run_32_analysis/` |
| Answer-opener sentence (T1) | 3 | 33 → 34 | rollback fired: hand count 4 vs. detector 2 | reverted | `benchmark_run_33_analysis/` |
| Shared section extractor (T2) | — | 33 | — | not recorded | `benchmark_run_33_analysis/` |
| `get_function_definition` explicit out-of-range contract (T2) | 3 | 34 | — | superseded by run 36 T1 | `benchmark_run_34_analysis/` |
| `get_monster_stats` `mattk[].dice` string (T3); `_policy.md` truncated-results marker (H6) | 3 | 34 | — | not recorded | `benchmark_run_34_analysis/` |
| `source_code_view` `start_line` default 1; `get_function_definition` miss names where the identifier occurs | 3 | 35 | — | not recorded | `benchmark_run_35_analysis/` |
| Wiki: the saving-throw article linked from the spell pages | 2 | 35 | — | not recorded | `benchmark_run_35_analysis/` |
| `get_function_definition` `start_line: 0` from the beginning (T1); `nethack_wiki_search` cap 16,140 (T2) | 3 | 36 → 37 | 42/42; the ceiling in use | kept | `benchmark_run_36_analysis/` |
| `nethack_wiki_view` resolution line (T3) | 3 | 36 | — | not recorded | `benchmark_run_36_analysis/` |
| Wiki: `Saving Throws.md` slow/hold/fear table (T4) | 2 | 36 → 38 | in use | kept | `benchmark_run_36_analysis/` |
| `wiki_search` `max_results` clamp (H5); same-line return types and closing-brace typedefs in the definition tools (H6) | 3 | 37 re-run | — | not recorded | `benchmark_run_37_rerun_analysis/` |
| `source_code_view` whole-line budget (T1); `get_function_definition` kind fallback (T2) | 3 | 38 | — | not recorded | `benchmark_run_38_analysis/` |
| Porter stemming in `WikiService` and `NetHackWikiService` (T1) | 3 | 39 → 40 | in use | kept | not named |
| `[Showing N of M matching articles …]` line (T1b) | 3 | 39 | — | not recorded | not named |
| Wiki: four edits on three pages | 2 | 39 | — | not recorded | not named |
| `wiki_search.md` stop-when-answered sentence (T1) | 3 | 40 → 41 | rounds 5.2 → 2.2, source calls 17 → 1 | kept | not named |
| Wiki: W1–W2 | 2 | 40 → 41 | verified | kept | not named |
| Exact-title narrowing in `monster_lookup` / `item_lookup` (T1); `ChatService.NoGreetInstruction` (H5) | 3; — | 41 | — | not recorded | not named |
| Case-folded `pathlower` in `WikiService` (T1); exact-title preference in `NetHackWikiService` (T2) | 3 | 42 | — | not recorded | not named |
| Wiki: W1–W2 | 2 | 42 | — | not recorded | not named |
| `AD_SAMU` flag description names the quest artifact (T1) | — | 47–48 | — | not recorded | `benchmark_runs_47_48_analysis/` |
| Tool-contract miss payloads (T1–T3) | — | 50–51 → 52 | T1 vacuous, T2 met, T3 not exercised | not recorded | the harness-29 round's (2026-09-17) |
| `wiki_search` lead block | 3 | 52 | — | not recorded | `benchmark_run_52_analysis/` |
| Wiki: T3 | 2 | 52 | — | not recorded | `benchmark_run_52_analysis/` |
| Tool changes T1–T4 (not named) | 3 | 53 | — | not recorded | the run-53 round's (2026-09-18) |
| Definition pointer in `source_code_search` and `source_code_view` (T1) | 3 | 56 | — | not recorded | `benchmark_run_56_analysis/` |
| Wiki: W1–W2 | 2 | 56 | — | not recorded | `benchmark_run_56_analysis/` |
| `wiki_search.md` categories (T1); compiled-out note in the source readers (T2) | 3 | 57, 59 → 60, 61 | verified | kept | `benchmark_runs_57_59_analysis/` |
| One `_policy.md` sentence (T3) | 3 | 57, 59 → 60, 61 | policy text does not move this model | kept | `benchmark_runs_57_59_analysis/` |
| Wiki: W1 | 2 | 57, 59 | — | not recorded | `benchmark_runs_57_59_analysis/` |
| GPT-5.6 Luna as second reader and verifier (Gemini 3.8 Flash @ `medium` instead) | grader | 57, 59; 60, 61 | do not ask for it again | decided against | `benchmark_runs_57_59_analysis/` |
| *Without category* line in `wiki_search` (T1) | 3 | 60, 61 → 62, 63 | followed 1 time in 7; rollback tripped | reverted (T5) | not named |
| Wiki: W1 | 2 | 60, 61 | — | not recorded | not named |
| Short articles returned whole by `wiki_search` (T4); the *Without category* removal (T5) | 3 | 62, 63 → 64, 65 | held | kept | `benchmark_runs_62_63_analysis/` |
| `get_artifact_stats` in Structured Lookup | — | 62, 63 | — | not recorded | `benchmark_runs_62_63_analysis/` |
| Wiki: W1–W3 | 2 | 62, 63 → 64, 65 | landed | kept | `benchmark_runs_62_63_analysis/` |
| Prompt text on locations, controls and Elbereth (prompt `1b512e27…`) | — | before 64, 65 | — | not recorded | not named |
| `item_lookup` covers `Artifacts/` (T5); `get_item_stats` minification keeps the failure reason (T6) | 3 | 64, 65 | not exercised at 66, 67 | not recorded | `benchmark_runs_64_65_analysis/` |
| Wiki: W1–W4 | 2 | 64, 65 | not run by 66, 67 | not recorded | `benchmark_runs_64_65_analysis/` |
| Tool output T1, T2 | 3 | 66, 67 | — | not recorded | the runs 66 and 67 round's (2026-09-24) |
| Wiki: W5 Holy Grail cures | 2 | 66, 67 | — | not recorded | the runs 66 and 67 round's (2026-09-24) |
| The controlled `verboseMode` pair | run | declined 2026-09-27 | — | decided against | not named |
| `wiki_view` section miss on an over-cap article (T1) | 3 | 68–72 → 73 | `benchmark_runs_68_72_analysis_v2.md` § 7.3; not exercised | outstanding | `benchmark_runs_68_72_analysis/` |
| `[Not reachable: …]` note (T2) | 3 | 68–72 → 73 | rollback trigger in § 7.3 there: a note on a function something does call is a false positive (`server_benchmark_tool_diagnostics`); not exercised | outstanding | `benchmark_runs_68_72_analysis/` |
| Wiki: W1–W3 | 2 | 68–72 → 73 | landed | kept | `benchmark_runs_68_72_analysis/` |
| Wiki: W1–W3 (3 + 552 + 23 pages) | 2 | 73 | — | not recorded | not named |
| `get_item_stats.md` and tool-side name resolution (T3) | 3 | 74 → 75 | deployed | not recorded | the run-74 round's (2026-09-29) |
| Wiki: W1–W3 (W1 `Skills.md`) | 2 | 74 → 75 | landed | kept | the run-74 round's (2026-09-29) |
| Stats tools trim `name`/`object_class`; `get_knowledge_article` trims `topic` (T1) | 3 | 75 | — | not recorded | `benchmark_run_75_analysis/` |
| Wiki: `Rooms/Smithy.md` | 2 | 75 | — | not recorded | `benchmark_run_75_analysis/` |
| Knowledge-base pointer fix (K1) | 1 | battery 2 | — | not recorded | not named |
| Wiki: W1–W3 | 2 | battery 2 | — | not recorded | not named |
| Wiki: W3 | 2 | battery 3 | — | not recorded | `battery_run_3_analysis/` |
| Knowledge-base scope sentence (T0) | 3 | battery 4 | — | not recorded | `battery_run_4_analysis/` |
| Retrieved-figures sentence (T1, then D1; C1) | 3 | battery 4–5 → 6–7 | R2 criteria 1, 3, 7 failed; R3 no effect | reverted | `battery_run_4_analysis/`, `battery_run_5_analysis/`, `battery_runs_6_7_analysis/` |
| Wiki: W1 | 2 | battery 4 | — | not recorded | `battery_run_4_analysis/` |
| `get_item_stats` trailing-word resolution (T1) | — | battery 5 → 6–7 | — | kept | `battery_run_5_analysis/` |
| `get_item_stats` glyph and long-tail resolution (T1b) | — | battery 6–7 | landed by battery 8 | not recorded | `battery_runs_6_7_analysis/` |
| `nethack_wiki_view` over-cap notice (T1); forced-final instruction in `AgentLoopRunner` (T2) | — | battery 8 | — | not recorded | not named |
| Stall watchdog (T3) | — | battery 8 | deferred to a second occurrence | outstanding | not named |
| Article `item_identification_and_appearances` corrected (it taught NetHack identify, altar and engraving rules) | 1 | battery 9–10 → 11 | read by no answer; no answer stated the old rules | kept | not named |
| GPT-6.1 Sol for GPT-5.6 Luna (M1) | 6 | battery 9–10 | motivated, not justified | not recorded | not named |
| `get_constants` fixes (B1–B2) | — | battery 11 | — | not recorded | `battery_run_11_analysis/` |
| Wiki: W1–W4 | 2 | battery 11 → 12 | landed | kept | `battery_run_11_analysis/` |
| Wiki: wrong-alignment wield (W1), peaceful-kill Luck (W2), two pages each | 2 | battery 12 | — | not recorded | `battery_run_12_analysis/` |
| *Steals amulet* on 14 monster pages and in `src/do.c:5523` (T6) | 2 | battery 12 | deferred 2026-10-08 | outstanding | `battery_run_12_analysis/` |
| Routing: Claude 5.5 Sonnet (`medium`) answered 9 of 36 without a tool call (T1) | 6, then 3 | Comparison #4 | pending the replicate R1 | outstanding | `comparison_4_rca_review/` |
| Wiki: W1 (`Bones Files`, `Quest Artifacts`) | 2 | bones trap → 122 | R1: ≥ max(3, R0 + 2) of A1–A4 correct, reaching those pages on ≥ 3 | kept, closed on run 123: criterion 1 met on the floor of 3, criterion 2 4 of 4 (run 122 had met criterion 2 only) | `bones_quest_artifact_trap/` |
| `_policy.md` sentence (T1): a role-relative helper's definition is read before saying what it covers | 3 | bones trap → 122, 123 → R1 | R1: ≥ 2 of 3 held-out questions correct with the helper's definition read on ≥ 2; Q8 correct; no drop on Q1–Q7, no critical error; Q1–Q7 model time and input tokens within +25 % of run 123 | reverted: rollback fired on run 124 (a 1 of 3, c Q7 critical error, side effect +39 % time, +58 % tokens); (a) confounded by wiki-misled held-out questions; removed in harness 58 | `bones_quest_artifact_trap/`, `benchmark_run_123_analysis/`, `benchmark_run_124_analysis/` |
| Model selection (M1) | — | bones trap | deferred to R2 | outstanding | `bones_quest_artifact_trap/` |
| Wiki: W2 (`Quest Artifacts`, `Wishable Artifacts`, `Wishing Guide`: another role's quest artifact is wishable, your own is not) | 2 | 122 | R2: Q6 carries no critical error and states that another role's quest artifact can be wished for; Q1 and Q3 do not attribute the bones rule to the dead character's role | kept, closed on run 123: Q6 no critical error and states it; Q1, Q3 role correct | `benchmark_run_122_analysis/` |
| `WikiService.TitleEquals` ignores one leading "The" in `wiki_view`, `item_lookup`, `monster_lookup` (T2) | 3 | 123 → R1 | unit tests; on R1 no `item_lookup` or `wiki_view` result on an existing article carries `[Truncated:` | kept, closed on run 125: no truncated exact-title result | `benchmark_run_123_analysis/` |
| Wiki: a Pets article (the pet pickup rule for quest artifacts) | 2 | 123 | deferred: it would teach Q8, the only question left on which T1 was observed | superseded by W3 (run 124) | `benchmark_run_123_analysis/` |
| Wiki: W3 (race attitudes, corpse recognition, quest-artifact pet, vanish and touch rules; ten pages) | 2 | 124 → R1 | R1: Q8, Q9, Q11 correct (Accuracy 5–6 from both members, no critical error); Q7 no critical error; Q3 Completeness ≥ 4 from both; on Q8 and Q9 a wiki call returns an edited page | partially kept, run 125: Q3, Q6–Q8 held; the Orc line's "as they may be to any chaotic character" caused Q9's critical error (repaired by W4); Q11 failed, Healer page not reached | `benchmark_run_124_analysis/` |
| Wiki: W4 (Orc line repaired; Chaotic race exception; quest-leader and always-peaceful exception on `Races/Orc.md`, `Races/Gnoll.md`, `Chaotic.md`; Eating Guide warning and corpse recognition; Master Key blessed-key note) | 2 | 125 → R1 | R1: the W4 pages reached, and Q9, Q10, Q11 answered without the repaired falsehoods; full criteria in the round's developer runbook, R1 | outstanding | `benchmark_run_125_analysis/` |
| Routing: the asker's race or role left out of wiki searches (T1) | 3 candidate | 125 | — | logged; measured by personal-context reach from harness 59 | `benchmark_run_125_analysis/` |
| Routing: `category` excludes root mechanics pages (T2) | 3 candidate | 125 | — | logged; the harness-36 pointer-line attempt was reverted | `benchmark_run_125_analysis/` |
| Variant source-tools-v1: `get_character_info` and stats `referenced_by` (experimental tools) | 3 candidate | 125 | `get_character_info` called on ≥ 2 of Q9–Q11; Q10's `get_artifact_stats` names `is_magic_key` in `referenced_by`; ≥ 2 of Q9–Q11 correct and more than R2; no new critical error on Q1–Q8; input tokens and median model time ≤ +25 % vs. R2 | experiment, outstanding (runbook R3, R4); annotated 2026-10-24: the variant is now the experimental changes *Race and role lookup tool* + *Where the game code uses it* (`race-role-tool-v1+stats-referenced-by-v1`) | `benchmark_run_125_analysis/` |
| Variant asker-profile-v1: asker race/role retrieval procedure | 3 / 7 candidate | 125 | ≥ 4 of 6 race/role questions search with the asker's race or role; no new critical error vs. R3; cost ≤ +25 % vs. R3; Q1, Q5–Q8 drop ≤ 15 points each | experiment, outstanding (runbook R3, R4); annotated 2026-10-24: the variant is now all four built-in experimental changes, *Race and role lookup tool*, *Where the game code uses it*, *Search with the player's race and role* and *Answer from the player's own character* | `benchmark_run_125_analysis/` |
