Extract the complete body of a C function, macro, or struct from the source code.

Unlike search_definitions (which returns only the definition line + a few lines
of context), this tool extracts the ENTIRE body by tracking braces.

If the output is truncated, the tool's notice gives you two numbers for the next
unseen line: an output line (1-based within the result you just got) and an
absolute file line (inside the header's L-range). Call again with start_line set
to either one — a value outside both ranges returns an explicit error, not a
guess.

Use this tool when you need to understand the full logic of a function.
Use search_definitions when you only need to see the signature or a quick look.
Use source_code_view when you need to read arbitrary file regions.

## Parameters
- `name` (string, required): Function, macro, or struct name.
- `type` (string, optional): Kind of definition ('function', 'macro', 'struct', or 'any'). Default 'any'.
- `start_line` (integer, optional): Where to resume after truncation — the output line from the truncation notice, or an absolute file line inside the header's L-range. Out-of-range values return an explicit error.
- `repository` (string, optional): Which codebase to extract from: 'gnollhack' (default) or 'nethack'.
