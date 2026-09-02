namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Threading;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkDifficultyJobManagerTests
{
    [Fact]
    public void TryStart_CreatesAndTracksJob_FailsWhenAnotherIsRunning()
    {
        var manager = new BenchmarkDifficultyJobManager();
        var job = new BenchmarkDifficultyJob
        {
            SuiteId = 10,
            SuiteName = "Test Suite",
            Scope = "suite",
            AssessorConfigId = 1,
            AssessorDisplayName = "Assessor Model",
            Cts = new CancellationTokenSource(),
            Items = new List<BenchmarkDifficultyJobItem>
            {
                new() { QuestionId = 1, OrderIndex = 1, QuestionTextExcerpt = "Test Q1" },
                new() { QuestionId = 2, OrderIndex = 2, QuestionTextExcerpt = "Test Q2" }
            }
        };

        var started = manager.TryStart(job, out var existing);

        Assert.True(started);
        Assert.Null(existing);
        Assert.Equal(BenchmarkDifficultyJobStatus.Running, job.Status);
        Assert.Equal(2, job.Items.Count);
        Assert.Same(job, manager.Current);

        // Second TryStart should fail with 409 conflict pattern
        var secondJob = new BenchmarkDifficultyJob
        {
            SuiteId = 10,
            SuiteName = "Test Suite",
            Cts = new CancellationTokenSource()
        };
        var secondStarted = manager.TryStart(secondJob, out var conflictJob);

        Assert.False(secondStarted);
        Assert.Same(job, conflictJob);
    }

    [Fact]
    public void TryGet_ReturnsJobById()
    {
        var manager = new BenchmarkDifficultyJobManager();
        var job = new BenchmarkDifficultyJob
        {
            SuiteId = 5,
            SuiteName = "Suite 5",
            Cts = new CancellationTokenSource()
        };
        manager.TryStart(job, out _);

        var retrieved = manager.TryGet(job.Id);
        Assert.NotNull(retrieved);
        Assert.Same(job, retrieved);

        var missing = manager.TryGet("non-existent-id");
        Assert.Null(missing);
    }

    [Fact]
    public void TryCancel_CancelsRunningJob()
    {
        var manager = new BenchmarkDifficultyJobManager();
        var job = new BenchmarkDifficultyJob
        {
            SuiteId = 5,
            SuiteName = "Suite 5",
            Cts = new CancellationTokenSource()
        };
        manager.TryStart(job, out _);

        var cancelled = manager.TryCancel(job.Id);
        Assert.True(cancelled);
        Assert.True(job.Cts.IsCancellationRequested);

        // Cancelled job status can be updated to Cancelled
        manager.Complete(job.Id, BenchmarkDifficultyJobStatus.Cancelled);
        Assert.Equal(BenchmarkDifficultyJobStatus.Cancelled, job.Status);

        // Subsequent TryCancel on non-running returns false
        var cancelledAgain = manager.TryCancel(job.Id);
        Assert.False(cancelledAgain);
    }

    [Fact]
    public void Complete_TransitionsStatus()
    {
        var manager = new BenchmarkDifficultyJobManager();
        var job = new BenchmarkDifficultyJob
        {
            SuiteId = 5,
            SuiteName = "Suite 5",
            Cts = new CancellationTokenSource()
        };
        manager.TryStart(job, out _);

        manager.Complete(job.Id, BenchmarkDifficultyJobStatus.Completed);

        Assert.Equal(BenchmarkDifficultyJobStatus.Completed, job.Status);
        Assert.NotNull(job.CompletedAtUtc);

        // Since job is now Completed (not Running), a new TryStart can now succeed
        var newJob = new BenchmarkDifficultyJob
        {
            SuiteId = 6,
            SuiteName = "Suite 6",
            Cts = new CancellationTokenSource()
        };
        var startNew = manager.TryStart(newJob, out var existing);

        Assert.True(startNew);
        Assert.Null(existing);
        Assert.NotEqual(job.Id, newJob.Id);
    }
}
