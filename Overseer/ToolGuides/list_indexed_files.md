# list_indexed_files

## Description
This tool lists all source code files currently indexed by the Overseer's search service. 
The Overseer caches a complete snapshot of the GnollHack repository. When you need to discover what files exist, where certain subsystems are located, or verify a file's existence before attempting to search it or view it, you should use this tool.

## Usage Guidelines
- You can provide an optional `path_filter` parameter to list only files whose paths contain a specific string (case-insensitive).
- Examples of `path_filter`:
  - `src` - lists all `.c` files in the core game engine
  - `include` - lists all `.h` header files
  - `potion` - lists any file with "potion" in its name or path
  - `.cs` - lists all C# source files for the frontend
- Use this tool before making assumptions about what files exist in the repository.
- Unlike `source_code_search`, this tool does not search the contents of the files, only their file paths, which makes it extremely fast.

## Schema
- `path_filter` (string, optional): A substring to filter the returned file paths. If omitted, all indexed files are returned.

## Output Format
The tool returns an alphabetically sorted list of file paths along with their total line counts, followed by a total count.
```
src/potion.c (412 lines)
src/pray.c (1200 lines)
Total: 2 files indexed
```
