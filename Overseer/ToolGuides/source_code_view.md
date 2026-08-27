# source_code_view

## Description
**EXPENSIVE TOOL** — Same cost considerations as source_code_search. Only use after
source_code_search has identified relevant code and you need additional context.

View a section of a GnollHack or NetHack source code file by line range.
Use `repository` to select the codebase (default: gnollhack).
Use this after source_code_search to see more context around a match,
or when you already know which file and approximate location to examine.
Specify the file path relative to the repository root (e.g., "src/potion.c").

WARNING: Reading item appearance strings in `src/objects.c` shows pre-shuffle compile-time initializers. In any active game, appearances are reshuffled at startup by `shuffle_all()` in `src/o_init.c`. Never infer an in-game item's identity from `src/objects.c` appearance descriptions.

## Parameters
- `file` (string, required): File path relative to the repository root (e.g., 'src/potion.c')
- `start_line` (integer, required): The starting line number to view.
- `line_count` (integer, optional): The number of lines to view. Defaults to 50. Max is 1000.
- `repository` (string, optional): Which codebase to view: 'gnollhack' (default) or 'nethack'.
