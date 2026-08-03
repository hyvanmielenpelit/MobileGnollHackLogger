# source_code_view

## Description
**EXPENSIVE TOOL** — Same cost considerations as source_code_search. Only use after
source_code_search has identified relevant code and you need additional context.

View a section of a GnollHack source code file by line range.
Use this after source_code_search to see more context around a match,
or when you already know which file and approximate location to examine.
Specify the file path relative to the repository root (e.g., "src/potion.c").

## Parameters
- `file` (string, required): File path relative to the repository root (e.g., 'src/potion.c')
- `start_line` (integer, required): The starting line number to view.
- `line_count` (integer, optional): The number of lines to view. Defaults to 50. Max is 1000.
