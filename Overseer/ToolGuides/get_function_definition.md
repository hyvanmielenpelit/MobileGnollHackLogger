Extract the complete body of a C function, macro, or struct from the source code.

Unlike search_definitions (which returns only the definition line + a few lines
of context), this tool extracts the ENTIRE body by tracking braces.

If the output is truncated, the tool tells you the line number where it stopped.
Call again with start_line to continue reading from that point.

Use this tool when you need to understand the full logic of a function.
Use search_definitions when you only need to see the signature or a quick look.
Use source_code_view when you need to read arbitrary file regions.
