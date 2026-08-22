Find the definition of a specific C symbol in the GnollHack or NetHack C core.
Use `repository` to select the codebase (default: gnollhack).
Much faster and more accurate than a general source_code_search when
you only want to see where a function, struct, macro, or typedef is defined.

Use the `kind` parameter to disambiguate (e.g., function, struct, macro, enum, type).
If you don't know the kind, leave it as 'any'.

## Parameters
- `symbol` (string, required): The name of the symbol to find.
- `kind` (string, optional): The kind of symbol ('function', 'struct', 'macro', 'enum', 'type', 'any'). Default 'any'.
- `repository` (string, optional): Which codebase to search: 'gnollhack' (default) or 'nethack'.
