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
    [Fact]
    public void SingleWrappedUnion_UnwrapsThenSplits()
    {
        var result = SourceCodeService.ParseFlagField("(A | B)");

        var flags = Assert.IsType<List<string>>(result);
        Assert.Equal(new[] { "A", "B" }, flags);
    }

    [Fact]
    public void DoublyWrappedUnion_UnwrapsBothLayersThenSplits()
    {
        var result = SourceCodeService.ParseFlagField("((A | B))");

        var flags = Assert.IsType<List<string>>(result);
        Assert.Equal(new[] { "A", "B" }, flags);
    }

    [Fact]
    public void SingleWrappedFlag_UnwrapsToAPlainString()
    {
        var result = SourceCodeService.ParseFlagField("(A)");

        Assert.Equal("A", Assert.IsType<string>(result));
    }

    [Fact]
    public void UnwrappedUnion_SplitsWithoutNeedingParentheses()
    {
        var result = SourceCodeService.ParseFlagField("A | B");

        var flags = Assert.IsType<List<string>>(result);
        Assert.Equal(new[] { "A", "B" }, flags);
    }

    [Fact]
    public void PlainNumber_StaysAPlainString()
    {
        var result = SourceCodeService.ParseFlagField("0");

        Assert.Equal("0", Assert.IsType<string>(result));
    }

    [Fact]
    public void UnbalancedOpenParen_HasNoMatchingCloseAtTheEnd_IsReturnedVerbatim()
    {
        var result = SourceCodeService.ParseFlagField("(A | B");

        Assert.Equal("(A | B", Assert.IsType<string>(result));
    }

    /// <summary>
    /// The opening paren's own matching close is not the last character — the string is a union
    /// of two already-wrapped unions — so nothing is unwrapped, and the leftover parentheses mean
    /// this is returned whole rather than split into "A", "B) | (C" and "D" style fragments.
    /// </summary>
    [Fact]
    public void UnionOfTwoWrappedUnions_IsNotUnwrapped_IsReturnedVerbatim()
    {
        var result = SourceCodeService.ParseFlagField("(A | B) | (C | D)");

        Assert.Equal("(A | B) | (C | D)", Assert.IsType<string>(result));
    }
}
