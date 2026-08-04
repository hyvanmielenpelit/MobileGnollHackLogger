using System.Text;
using System.Collections.Generic;
using System;

namespace Overseer.Services
{
    public class CLexer
    {
        private enum LexerState
        {
            Normal,
            InString,
            InChar,
            InLineComment,
            InBlockComment
        }

        public class ExtractionResult
        {
            public string Content { get; set; } = string.Empty;
            public int StartLine { get; set; }
            public int EndLine { get; set; }
        }

        public static ExtractionResult? ExtractBracedBlock(string[] lines, int startLine)
        {
            return SkipToMatchingClose(lines, startLine, '{', '}');
        }

        public static ExtractionResult? ExtractParenBlock(string[] lines, int startLine)
        {
            return SkipToMatchingClose(lines, startLine, '(', ')');
        }

        public static ExtractionResult? SkipToMatchingClose(string[] lines, int startLine, char openChar, char closeChar)
        {
            var state = LexerState.Normal;
            int depth = 0;
            bool foundStart = false;
            
            int actualStartLine = -1;
            int actualEndLine = -1;
            
            var contentBuilder = new StringBuilder();

            for (int i = startLine; i < lines.Length; i++)
            {
                string line = lines[i];
                var lineBuilder = new StringBuilder();

                for (int j = 0; j < line.Length; j++)
                {
                    char c = line[j];
                    char nextC = j + 1 < line.Length ? line[j + 1] : '\0';

                    switch (state)
                    {
                        case LexerState.Normal:
                            if (c == '/' && nextC == '/')
                            {
                                state = LexerState.InLineComment;
                                if (foundStart) lineBuilder.Append(c).Append(nextC);
                                j++; // skip nextC
                            }
                            else if (c == '/' && nextC == '*')
                            {
                                state = LexerState.InBlockComment;
                                if (foundStart) lineBuilder.Append(c).Append(nextC);
                                j++; // skip nextC
                            }
                            else if (c == '"')
                            {
                                state = LexerState.InString;
                                if (foundStart) lineBuilder.Append(c);
                            }
                            else if (c == '\'')
                            {
                                state = LexerState.InChar;
                                if (foundStart) lineBuilder.Append(c);
                            }
                            else
                            {
                                if (c == openChar)
                                {
                                    if (!foundStart)
                                    {
                                        foundStart = true;
                                        actualStartLine = i;
                                    }
                                    depth++;
                                }
                                else if (c == closeChar && foundStart)
                                {
                                    depth--;
                                }
                                
                                if (foundStart)
                                {
                                    lineBuilder.Append(c);
                                    if (depth == 0)
                                    {
                                        actualEndLine = i;
                                        contentBuilder.Append(lineBuilder.ToString());
                                        return new ExtractionResult
                                        {
                                            Content = contentBuilder.ToString().Trim(),
                                            StartLine = actualStartLine,
                                            EndLine = actualEndLine
                                        };
                                    }
                                }
                            }
                            break;

                        case LexerState.InString:
                            if (foundStart) lineBuilder.Append(c);
                            if (c == '\\' && nextC != '\0')
                            {
                                if (foundStart) lineBuilder.Append(nextC);
                                j++;
                            }
                            else if (c == '"')
                            {
                                state = LexerState.Normal;
                            }
                            break;

                        case LexerState.InChar:
                            if (foundStart) lineBuilder.Append(c);
                            if (c == '\\' && nextC != '\0')
                            {
                                if (foundStart) lineBuilder.Append(nextC);
                                j++;
                            }
                            else if (c == '\'')
                            {
                                state = LexerState.Normal;
                            }
                            break;

                        case LexerState.InLineComment:
                            if (foundStart) lineBuilder.Append(c);
                            break;

                        case LexerState.InBlockComment:
                            if (foundStart) lineBuilder.Append(c);
                            if (c == '*' && nextC == '/')
                            {
                                if (foundStart) lineBuilder.Append(nextC);
                                j++;
                                state = LexerState.Normal;
                            }
                            break;
                    }
                }

                if (state == LexerState.InLineComment)
                {
                    state = LexerState.Normal;
                }

                if (foundStart)
                {
                    contentBuilder.AppendLine(lineBuilder.ToString());
                }
            }

            return null; // never closed
        }
    }
}
