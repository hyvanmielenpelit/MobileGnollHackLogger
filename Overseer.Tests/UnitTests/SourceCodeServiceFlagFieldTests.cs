namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Overseer.Services;
using Xunit;

/// <summary>
/// ParseFlagField turns one macro argument like "M1_HUMANOID | M1_CARNIVORE" into a list of flag
/// names, or leaves a non-union value as a single string. Today it splits on '|' without first
/// removing an enclosing pair of parentheses, so a parenthesised union — the shape objects.c and
/// artilist.h both write flag fields in — leaks a stray '(' or ')' onto the first or last flag
/// name. These pin the balanced-unwrap fix: a full wrapper is removed, repeating for a nested one,
/// and anything that still holds a parenthesis afterwards is returned verbatim rather than split
/// into names that were never in the source.
/// </summary>
public class SourceCodeServiceFlagFieldTests
{
    [Theory]
    [InlineData("(A | B)", new[] { "A", "B" })] // A single wrapper is removed before splitting.
    [InlineData("((A | B))", new[] { "A", "B" })] // Both layers of a nested wrapper are removed.
    [InlineData("A | B", new[] { "A", "B" })] // An unwrapped union splits without needing parentheses.
    public void Union_IsUnwrappedThenSplitIntoFlagNames(string field, string[] expected)
    {
        var result = SourceCodeService.ParseFlagField(field);

        var flags = Assert.IsType<List<string>>(result);
        Assert.Equal(expected, flags);
    }

    [Theory]
    [InlineData("(A)", "A")] // A single wrapped flag unwraps to a plain string.
    [InlineData("0", "0")] // A plain number stays a plain string.
    [InlineData("(A | B", "(A | B")] // An unbalanced open paren has no matching close at the end: returned verbatim.
    // The opening paren's own matching close is not the last character — the string is a union
    // of two already-wrapped unions — so nothing is unwrapped, and the leftover parentheses mean
    // this is returned whole rather than split into "A", "B) | (C" and "D" style fragments.
    [InlineData("(A | B) | (C | D)", "(A | B) | (C | D)")]
    public void NonUnionOrUnsplittableField_IsReturnedAsOneString(string field, string expected)
    {
        var result = SourceCodeService.ParseFlagField(field);

        Assert.Equal(expected, Assert.IsType<string>(result));
    }
}
