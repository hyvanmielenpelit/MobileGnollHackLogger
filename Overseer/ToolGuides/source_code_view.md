# source_code_view

## Description
**EXPENSIVE TOOL** — Same cost considerations as source_code_search. Only use after
source_code_search has identified relevant code and you need additional context.

View a section of a GnollHack or NetHack source code file by line range.
Use `repository` to select the codebase (default: gnollhack).
Use this after source_code_search to see more context around a match,
or when you already know which file and approximate location to examine.
Specify the file path relative to the repository root (e.g., "src/potion.c").

WARNING: Reading item appearance strings in `src/objects.c` shows pre-shuffle compile-time initializers. In any active game, appearances are reshuffled at startup by `shuffle_all()` in `src/o_init.c`. Never infer an in-game item's identity from `src/objects.c` appearance descriptions. Use the snapshot's `Discoveries` section for the current game's identities.

## Parameters
- `file` (string, required): File path relative to the repository root (e.g., 'src/potion.c')
- `start_line` (integer, optional): The starting line number to view. Defaults to 1 when neither this nor `search_term` is given.
- `line_count` (integer, optional): The number of lines to view. Defaults to 50. Max is 1000. About 150-200 lines of C fit under the result cap; a longer request is cut at a whole line and the notice names where to resume.
- `repository` (string, optional): Which codebase to view: 'gnollhack' (default) or 'nethack'.

If the output is truncated, the tool's notice names the last file line shown and the `start_line` to continue from. Prefer requests of 150 lines or fewer; a continuation is a dependent call, never part of the original batch.
