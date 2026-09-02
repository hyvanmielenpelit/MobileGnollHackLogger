namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkDifficultyBatchPlannerTests
{
    [Fact]
    public void Plan_DividesItemsIntoEvenBatches()
    {
        var items = Enumerable.Range(1, 10).ToList();
        var batches = BenchmarkDifficultyBatchPlanner.Plan(items, 4);

        Assert.Equal(3, batches.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, batches[0]);
        Assert.Equal(new[] { 5, 6, 7, 8 }, batches[1]);
        Assert.Equal(new[] { 9, 10 }, batches[2]);
    }

    [Fact]
    public void Plan_ClampsBatchSizeBetween1And25()
    {
        var items = Enumerable.Range(1, 5).ToList();

        // Size <= 0 clamped to 1
        var batches1 = BenchmarkDifficultyBatchPlanner.Plan(items, 0);
        Assert.Equal(5, batches1.Count);

        // Size > 25 clamped to 25
        var largeItems = Enumerable.Range(1, 30).ToList();
        var batches2 = BenchmarkDifficultyBatchPlanner.Plan(largeItems, 50);
        Assert.Equal(2, batches2.Count);
        Assert.Equal(25, batches2[0].Count);
        Assert.Equal(5, batches2[1].Count);
    }

    [Fact]
    public void Plan_EmptyList_ReturnsEmptyBatches()
    {
        var items = new List<int>();
        var batches = BenchmarkDifficultyBatchPlanner.Plan(items, 4);

        Assert.Empty(batches);
    }

    [Fact]
    public void Split_EvenBatch_SplitsInHalf()
    {
        var batch = new List<int> { 1, 2, 3, 4 };
        var splits = BenchmarkDifficultyBatchPlanner.Split(batch);

        Assert.Equal(2, splits.Count);
        Assert.Equal(new[] { 1, 2 }, splits[0]);
        Assert.Equal(new[] { 3, 4 }, splits[1]);
    }

    [Fact]
    public void Split_OddBatch_SplitsCorrectly()
    {
        var batch = new List<int> { 1, 2, 3 };
        var splits = BenchmarkDifficultyBatchPlanner.Split(batch);

        Assert.Equal(2, splits.Count);
        Assert.Equal(new[] { 1, 2 }, splits[0]);
        Assert.Equal(new[] { 3 }, splits[1]);
    }

    [Fact]
    public void Split_SingleItemOrEmpty_ReturnsEmpty()
    {
        var single = new List<int> { 42 };
        var splitSingle = BenchmarkDifficultyBatchPlanner.Split(single);
        Assert.Empty(splitSingle);

        var empty = new List<int>();
        var splitEmpty = BenchmarkDifficultyBatchPlanner.Split(empty);
        Assert.Empty(splitEmpty);
    }
}
