# Reduce Overseer's Over-Reliance on Source Code Searches

## Problem

The Overseer is calling `source_code_search` and `source_code_view` too aggressively for questions that are well-answered by the wiki, `monster_lookup`, or `item_lookup`. For example, "what are the stats of the barbarian" should be answerable from wiki/monster_lookup alone, without diving into C source code.

Source code searches are expensive in three ways:
1. **Token cost**: Each search returns large code snippets that consume output tokens and add latency.
2. **Cascading searches**: Source code searches rarely resolve in a single call. The LLM typically needs a discovery search, then a targeted search, then a `source_code_view` for context — easily 5–25 tool calls for one question. This compounds the cost.
3. **UX impact**: Every tool call is visible to the user. Watching 15+ source code searches scroll by before getting an answer that was already in the wiki is a poor user experience. A clean wiki lookup (1–2 tool calls) is far clearer to the player.

## Root Cause Analysis

After reviewing the codebase, I found **three reinforcing prompt instructions** that push the LLM toward source code searches even when unnecessary:

### 1. `_policy.md` (lines 11–12) — the strongest trigger

> "When answering questions about specific game mechanics, probabilities, or formulas, use source_code_search to verify the exact implementation before stating numbers."

This is **overly broad**. "Specific game mechanics" includes things like "what damage does a barbarian deal?" or "what's the AC of chain mail?" — questions where the wiki or `monster_lookup`/`item_lookup` already have authoritative data. The instruction tells the LLM to *always verify via source code*, which is wasteful.

### 2. `BuildSystemPrompt` Section 6b (lines 1431–1433)

```csharp
sb.AppendLine("You have access to the GnollHack C source code via the source_code_search and source_code_view tools.");
sb.AppendLine("Use these tools to verify undocumented mechanics, check exact formulas or probabilities, and investigate potential bugs.");
sb.AppendLine("When a player asks about a specific mechanic that is not covered in the wiki, search the source code to find the authoritative answer.");
```

This section is reasonable on its own (it mentions "undocumented mechanics" and "not covered in the wiki") but doesn't actively *discourage* code searches when wiki data is sufficient.

### 3. `BuildSystemPrompt` Section 11 (line 1497)

> "The GnollHack source code (accessible via source_code_search) is the definitive authority for exact mechanics, formulas, and probabilities. Prefer source code over wiki when they disagree."

This positions source code as "the definitive authority" which — while technically true — gives the LLM the impression that it should *always prefer* code. Combined with #1, the LLM concludes it should search source code for essentially every factual question.

### 4. No cost awareness in the tool descriptions

The [source_code_search.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/source_code_search.md) tool guide has a detailed "Search Strategy" section (discover → survey → locate → deep dive) but never mentions that code searches are expensive or that the LLM should prefer lighter tools first. The wiki tools ([wiki_search.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/wiki_search.md), [monster_lookup.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/monster_lookup.md), [item_lookup.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/item_lookup.md)) have almost no guidance text, making them seem less capable/useful than source code search.

### 5. No cost-aware behavior anywhere

There is no mode-differentiated behavior for tool preference. All modes (Gameplay, Technical, Debug) get identical tool guidance that pushes toward source code. Even in Debug Mode (mode 2), the wiki should be the first resort for factual questions — debug mode is often used for testing, not just bug investigation.

## Proposed Changes

### Tool Use Policy

#### [MODIFY] [_policy.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/_policy.md)

Rewrite the tool use policy to establish a clear **tool preference hierarchy** and make source code searches a *fallback*, not a default. The key change is replacing the line "When answering questions about specific game mechanics, probabilities, or formulas, use source_code_search to verify the exact implementation before stating numbers" with guidance that establishes a wiki-first approach.

> [!NOTE]
> ChatService.cs Section 15 (line 1606) already prepends `## Tool Usage Policy` as a header before injecting the `_policy.md` content. The current `_policy.md` also starts with `## Tool Use Policy`, creating a pre-existing duplicate heading. The replacement below preserves this existing behavior — fixing the duplicate is out of scope for this change.

New policy text:

```markdown
## Tool Use Policy
- Prefer GnollHack tools (wiki_search, monster_lookup, item_lookup) over web search
  for game-specific questions. GnollHack tools use authoritative data.
- Use web search only for general knowledge, cross-game comparisons, or topics
  not covered by GnollHack tools.
- Do NOT use tools for information already in your context (game snapshot,
  recent messages in the snapshot, wiki articles already provided).
- When spoiler-free mode is active, tools return full information but you MUST filter it according to the spoiler policy.
- Briefly tell the player what you're looking up when using a tool.
- If a tool returns no results, say so honestly — do not fabricate information.
- When citing source code, always mention the file name and approximate line number.
- Use source_code_view to get more context when a source_code_search result is incomplete.

## Tool Preference Hierarchy
Follow this order when looking up game information. Be parsimonious with tool calls — fewer, targeted calls provide a better experience for the player.

1. **Check your context first** — wiki articles already embedded in the system prompt and the game snapshot often contain the answer. If they do, respond directly without any tool calls.
2. **Wiki & lookup tools** (wiki_search, monster_lookup, item_lookup, nethack_wiki_search) — fast, cheap, and authoritative for documented game content. Use these for stats, descriptions, general mechanics, item/monster properties, class/race information, and well-documented game features. If the wiki gives a clear answer, trust it — no further verification is needed.
3. **Source code tools** (source_code_search, source_code_view) — expensive and typically require multiple follow-up calls (discovery → targeted search → context view), easily consuming 5–15+ tool calls for a single question. Reserve these for:
   - Exact formulas, probability calculations, or random number logic (e.g., "what is the exact chance of...?")
   - Mechanics that the wiki does not cover or covers ambiguously
   - Bug investigation or when you suspect wiki information is incorrect
   - When the user explicitly asks for a code-level answer or source code verification

**Do NOT** routinely use source_code_search to verify information that wiki/lookup tools already provide clearly (e.g., monster stats, item properties, class descriptions). The wiki and lookup tools draw from the same game data.

## Spoiler-Free Mode
- When spoiler-free mode is active, explaining HOW mechanics work is always safe.
- Revealing WHAT the player has not yet encountered is a spoiler.
- Use get_player_library and get_oracle_consultations to check what the player already knows.
- Do NOT scan dumplogs for spoiler checking. The full spoiler policy is provided in the system prompt when this mode is active.
```

---

### System Prompt Adjustments

#### [MODIFY] [ChatService.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs)

**Change 1: Section 6b (Source Code Access) — lines 1426–1445**

Add cost-aware guidance that applies in all modes. The LLM should always prefer wiki first, regardless of mode. Only when actively debugging a reported bug (clear from context) or when explicitly asked should source code be the first tool.

Replace the three lines at 1431–1433:
```csharp
sb.AppendLine("You have access to the GnollHack C source code via the source_code_search and source_code_view tools.");
sb.AppendLine("Use these tools to verify undocumented mechanics, check exact formulas or probabilities, and investigate potential bugs.");
sb.AppendLine("When a player asks about a specific mechanic that is not covered in the wiki, search the source code to find the authoritative answer.");
```

With:
```csharp
sb.AppendLine("You have access to the GnollHack C source code via the source_code_search and source_code_view tools.");
sb.AppendLine("IMPORTANT: Source code searches are expensive — they typically require multiple follow-up calls and produce large outputs. Always try wiki_search, monster_lookup, or item_lookup first. Only use source code tools when:");
sb.AppendLine("- The wiki/lookup tools do not have the information or the answer is ambiguous");
sb.AppendLine("- The user asks about exact formulas, probabilities, or undocumented mechanics");
sb.AppendLine("- You are actively investigating a bug or the user explicitly requests source code verification");
sb.AppendLine("When the wiki or lookup tools give a clear answer, trust it without code verification.");
```

**Change 2: Section 11 (Important Rules) — line 1497**

Soften the "source code is the definitive authority" wording so it doesn't override the preference hierarchy. Replace:

```
"- The GnollHack source code (accessible via source_code_search) is the definitive authority for exact mechanics, formulas, and probabilities. Prefer source code over wiki when they disagree."
```

With:

```
"- The GnollHack source code is the ultimate authority if the wiki and source code disagree on exact formulas or probabilities. However, for general game information (stats, properties, descriptions), the wiki is authoritative and does not require source code verification."
```

---

### Tool Guide Enrichment

#### [MODIFY] [source_code_search.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/source_code_search.md)

Add a cost/preference note at the very top, before the existing line 1:

```markdown
**EXPENSIVE TOOL** — Source code searches typically cascade into multiple follow-up calls
(discovery → targeted search → context view), easily consuming 5–15 tool calls per question.
Before using this tool, check whether wiki_search, monster_lookup, or item_lookup can 
answer the question. Only use this tool when you need exact formulas, undocumented mechanics,
or code-level details that lighter tools cannot provide.

```

*(The existing content starting with "Search the GnollHack C source code..." remains unchanged after this addition.)*

#### [MODIFY] [wiki_search.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/wiki_search.md)

Expand the minimal description:

```markdown
Search the GnollHack specific wiki for information. Use this before nethack_wiki_search.

**PREFERRED TOOL** — Fast and cheap. Use this as your first tool for general game information,
mechanics, class/race descriptions, and any well-documented feature. Only fall back to
source_code_search if the wiki does not have the answer or you need exact code-level details.
```

#### [MODIFY] [monster_lookup.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/monster_lookup.md)

```markdown
Look up exact base stats and flags for a monster in the game data.

**PREFERRED TOOL** — Returns authoritative monster data directly from the game database.
Use this for all monster stat questions. No source_code_search verification is needed
for stats returned by this tool. If a monster is not found, you may check monst.c via
source_code_search to confirm the monster does not exist in the game.
```

#### [MODIFY] [item_lookup.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/item_lookup.md)

```markdown
Look up exact base stats and flags for an item in the game data.

**PREFERRED TOOL** — Returns authoritative item data directly from the game database.
Use this for all item stat questions. No source_code_search verification is needed
for stats returned by this tool. If an item is not found, you may check objects.c via
source_code_search to confirm the item does not exist in the game.
```

#### [MODIFY] [source_code_view.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/source_code_view.md)

View the current file to get its exact content first:

Currently reads:
```markdown
# source_code_view

## Description
View a section of a GnollHack source code file by line range.
Use this after source_code_search to see more context around a match,
or when you already know which file and approximate location to examine.
Specify the file path relative to the repository root (e.g., "src/potion.c").
```

Prepend a cost note under the Description header. New content:

```markdown
# source_code_view

## Description
**EXPENSIVE TOOL** — Same cost considerations as source_code_search. Only use after
source_code_search has identified relevant code and you need additional context.

View a section of a GnollHack source code file by line range.
Use this after source_code_search to see more context around a match,
or when you already know which file and approximate location to examine.
Specify the file path relative to the repository root (e.g., "src/potion.c").
```

*(The existing parameter/schema section below remains unchanged.)*

## Verification Plan

### Manual Verification
After deploying the changes, test the Overseer with questions like:
- "What are the stats of the barbarian?" → should use `monster_lookup` or `wiki_search`, **not** `source_code_search`
- "What damage does Excalibur do?" → should use `wiki_search` or `item_lookup`
- "What is the exact formula for to-hit calculation?" → should use `source_code_search` (this is an exact formula question)
- "How does prayer work?" → should use `wiki_search` first
- "Can you check the source code for how prayer works?" → should use `source_code_search` (explicit user request)
- In Debug Mode: "What are the stats of the gnoll?" → should still use `monster_lookup` first
- In Debug Mode while discussing a crash: code searches should be used freely

### What to watch for
- Tool call counts per question should drop significantly for simple factual queries
- The LLM should state which tool it's using and why (per existing policy: "Briefly tell the player what you're looking up")
- Source code searches should only appear for formula/probability questions, bug investigation, or explicit user requests

## Subagent Use

No subagents are needed. This is a prompt/content-only change across 7 small files (no code logic changes), easily handled sequentially by a single agent.
