using System;
using Microsoft.EntityFrameworkCore;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ExceptionDetailsTests
{
    [Fact]
    public void Describe_TwoLevelChain_ListsBothLevelsOutermostFirst()
    {
        var ex = new InvalidOperationException(
            "An error occurred while saving the entity changes. See the inner exception for details.",
            new ArgumentException("The INSERT statement conflicted with the FOREIGN KEY constraint."));

        string described = ExceptionDetails.Describe(ex);

        int outerIndex = described.IndexOf("InvalidOperationException: An error occurred", StringComparison.Ordinal);
        int innerIndex = described.IndexOf("ArgumentException: The INSERT statement conflicted", StringComparison.Ordinal);

        Assert.True(outerIndex >= 0, "The outermost exception should be described.");
        Assert.True(innerIndex >= 0, "The inner exception should be described.");
        Assert.True(outerIndex < innerIndex, "The outermost exception should come first.");
    }

    [Fact]
    public void Describe_DeepChain_StopsAtCapAndSaysSo()
    {
        Exception ex = new InvalidOperationException("level-8");
        for (int i = 7; i >= 1; i--)
        {
            ex = new InvalidOperationException($"level-{i}", ex);
        }

        string described = ExceptionDetails.Describe(ex);

        Assert.Contains("level-1", described);
        Assert.Contains("level-5", described);
        Assert.DoesNotContain("level-6", described);
        Assert.Contains("truncated", described);
    }

    [Fact]
    public void Describe_LongText_IsTruncatedToMaxLength()
    {
        var ex = new InvalidOperationException(new string('x', 500));

        string described = ExceptionDetails.Describe(ex, maxLength: 100);

        Assert.EndsWith("...", described);
        Assert.Equal(103, described.Length);
    }

    [Fact]
    public void Describe_NullException_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ExceptionDetails.Describe(null));
    }

    [Fact]
    public void Describe_DbUpdateExceptionWithoutEntries_DoesNotThrow()
    {
        var ex = new DbUpdateException(
            "An error occurred while saving the entity changes. See the inner exception for details.",
            new InvalidOperationException("FK violation"));

        string described = ExceptionDetails.Describe(ex);

        Assert.Contains("DbUpdateException:", described);
        Assert.Contains("FK violation", described);
    }

    [Fact]
    public void DescribeShort_ChainedException_IncludesOutermostAndInnermostMessages()
    {
        var ex = new InvalidOperationException(
            "See the inner exception for details.",
            new InvalidOperationException("middle", new ArgumentException("root cause")));

        string described = ExceptionDetails.DescribeShort(ex);

        Assert.Contains("See the inner exception for details.", described);
        Assert.Contains("root cause", described);
        Assert.DoesNotContain("middle", described);
    }

    [Fact]
    public void DescribeShort_SingleException_ReturnsItsMessageOnly()
    {
        var ex = new InvalidOperationException("only one");

        Assert.Equal("only one", ExceptionDetails.DescribeShort(ex));
    }
}
