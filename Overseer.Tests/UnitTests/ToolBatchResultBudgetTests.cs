using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ToolBatchResultBudgetTests
{
    [Fact]
    public void Apply_WithinBudget_ReturnsOriginalContent()
    {
        var budget = new ToolBatchResultBudget(100);
        var result1 = budget.Apply("Hello World");
        var result2 = budget.Apply(" Second Part");

        Assert.Equal("Hello World", result1);
        Assert.Equal(" Second Part", result2);
        Assert.False(budget.AnyTruncated);
    }

    [Fact]
    public void Apply_ExceedingBudget_TruncatesPartiallyThenSkips()
    {
        var budget = new ToolBatchResultBudget(20);
        var result1 = budget.Apply("1234567890"); // 10 chars, 10 remaining
        var result2 = budget.Apply("ABCDEFGHIJKLMN"); // 14 chars -> truncates to 10 chars + notice
        var result3 = budget.Apply("Extra Content"); // budget exhausted -> skipped

        Assert.Equal("1234567890", result1);
        Assert.StartsWith("ABCDEFGHIJ\n\n... (truncated: batch output budget reached)", result2);
        Assert.Equal("(skipped: batch output budget reached)", result3);
        Assert.True(budget.AnyTruncated);
    }

    [Fact]
    public void Apply_EmptyOrNull_ReturnsUnchanged()
    {
        var budget = new ToolBatchResultBudget(50);
        Assert.Null(budget.Apply(null!));
        Assert.Equal("", budget.Apply(""));
        Assert.False(budget.AnyTruncated);
    }
}