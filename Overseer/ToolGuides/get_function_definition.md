Extract the complete body of a C function, macro, or struct from the source code.

Unlike search_definitions (which returns only the definition line + a few lines
of context), this tool extracts the ENTIRE body by tracking braces.

If the output is truncated, the tool's notice gives you two numbers for the next
unseen line: an output line (1-based within the result you just got) and an
absolute file line (inside the header's L-range). Call again with start_line set
to either one — a value outside both ranges returns an explicit error, not a
guess. Omit start_line, or pass 0, to start at the beginning.

A miss says where the name occurs in the indexed source — naming up to three
files and how many lines mention it — or states that it does not occur there at
all. A struct member, a function pointer field, or a macro alias has no
extractable body under that name: read those with source_code_search (with
context_lines) on the file the miss names, or with search_definitions for the
symbol the alias resolves to. If you name a kind and nothing of that kind
exists but a definition of another kind does (a macro under a function's name
is the common case), the tool returns that definition behind a one-line note
saying so.

Use this tool when you need to understand the full logic of a function.
Use search_definitions when you only need to see the signature or a quick look.
Use source_code_view when you need to read arbitrary file regions.

## Parameters
- `name` (string, required): Function, macro, or struct name.
- `type` (string, optional): Kind of definition ('function', 'macro', 'struct', or 'any'). Default 'any'.
- `start_line` (integer, optional): 1-based. Omit, or pass 0, to start at the beginning. To resume after a truncated result, pass the output line the notice names, or an absolute file line inside the header's L-range. Any other value returns an explicit error.
- `repository` (string, optional): Which codebase to extract from: 'gnollhack' (default) or 'nethack'.
