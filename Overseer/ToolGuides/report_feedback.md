# report_feedback

## Description
Sends a short report to the Overseer development team when something in this conversation went wrong in a way they can fix: information missing from the GnollHack wiki, the knowledge base or the source; a tool that misbehaved; or a mistake you made earlier in this conversation. The report is stored for the developers and does not change your answer.

## Usage Guidelines
- `missing_information`: you searched for a fact that the GnollHack wiki, the knowledge base or the GnollHack source should contain, made at least one reasonable search, and did not find it. Say what you searched for, with which tools and queries, and where you expected it.
- `tool_problem`: a tool returned an error, malformed output, or a result that contradicts its own description. Name that tool first in `related_tools`.
- `own_mistake`: you realized that something you told the user earlier in this conversation was wrong, because the user corrected you, a tool result contradicted it, or you noticed it on review. Correct the mistake in your answer first. The report never replaces the correction.
- `subject` is the grouping key. Name the thing the report is about in two to six words, the way its wiki page or source file names it: `Dilithium crystal`, `wiki page Elbereth`, `src/mhitu.c`, `monster_lookup`, `cockatrice corpse weight`. Reports with the same category and subject are one issue for the developers, so use the same subject for the same problem.
- `suspected_cause` is your best guess and is read as a hypothesis. Prefer causes that can be checked, such as `did_not_verify` (you answered without calling the tool that would have shown the answer), over guesses about your own reasoning.
- Do not report: something that does not exist in the game, a question outside NetHack and GnollHack, the user's preferences, or the same issue twice.
- At most one report per issue and three per turn. When you are making other tool calls, include this one in the same batch.
- Describe the issue in your own words. Never include the user's personal information, credentials or long quotes from the conversation.
- You may mention in one sentence that you reported the issue. Do not dwell on it.

## Schema
- `category` (required): `missing_information`, `tool_problem` or `own_mistake`.
- `subject` (required): two to six words naming what the report is about, at most 120 characters.
- `summary` (required): one sentence, at most 300 characters.
- `details`, `searched_for`, `expected_location`, `related_tools`, `noticed_via`, `suspected_cause`, `suspected_cause_note`: optional.

## Output Format
A one-line acknowledgment. Continue with the user's request.
