namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkDifficultyJobTests
{
    private static BenchmarkDifficultyJob JobWithMixedItems() => new()
    {
        SuiteId = 1,
        SuiteName = "Test Suite",
        Cts = new CancellationTokenSource(),
        Items = new List<BenchmarkDifficultyJobItem>
        {
            new() { QuestionId = 1, OrderIndex = 1, Status = BenchmarkDifficultyItemStatus.Pending },
            new() { QuestionId = 2, OrderIndex = 2, Status = BenchmarkDifficultyItemStatus.Assessing },
            new() { QuestionId = 3, OrderIndex = 3, Status = BenchmarkDifficultyItemStatus.Rated, Difficulty = 55 },
            new() { QuestionId = 4, OrderIndex = 4, Status = BenchmarkDifficultyItemStatus.Failed, ErrorMessage = "Timeout" }
        }
    };

    [Fact]
    public void MarkRemainingCancelled_CancelsPendingAndAssessing_LeavesRatedAndFailed()
    {
        var job = JobWithMixedItems();

        job.MarkRemainingCancelled();

        Assert.Equal(
            new[]
            {
                BenchmarkDifficultyItemStatus.Cancelled,
                BenchmarkDifficultyItemStatus.Cancelled,
                BenchmarkDifficultyItemStatus.Rated,
                BenchmarkDifficultyItemStatus.Failed
            },
            job.Items.Select(i => i.Status));
        Assert.Equal(55, job.Items[2].Difficulty);
        Assert.Equal("Timeout", job.Items[3].ErrorMessage);
    }

    [Fact]
    public void MarkRemainingSkipped_SkipsPendingAndAssessing_LeavesRatedAndFailed()
    {
        var job = JobWithMixedItems();

        job.MarkRemainingSkipped();

        Assert.Equal(
            new[]
            {
                BenchmarkDifficultyItemStatus.Skipped,
                BenchmarkDifficultyItemStatus.Skipped,
                BenchmarkDifficultyItemStatus.Rated,
                BenchmarkDifficultyItemStatus.Failed
            },
            job.Items.Select(i => i.Status));
    }

    [Fact]
    public void ToDto_ReportsCancelledItemsAsCancelled()
    {
        var job = JobWithMixedItems();
        job.MarkRemainingCancelled();

        var dto = job.ToDto();

        Assert.Equal(new[] { "Cancelled", "Cancelled", "Rated", "Failed" }, dto.Items.Select(i => i.Status));
    }
}
