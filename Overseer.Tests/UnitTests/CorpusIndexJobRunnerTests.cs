using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.CorpusIndexing;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="CorpusIndexJobRunner"/>: one pass at a time, a manual request refused while a pass runs,
/// automatic requests folded into one follow-up, failures and cancellation recorded without a faulted
/// task, the capped history, and the live phase and file counts of the running job. Passes are held
/// mid-flight by <see cref="TaskCompletionSource"/>s; every wait is bounded.
/// </summary>
public sealed class CorpusIndexJobRunnerTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private readonly List<CorpusIndexJobRunner> _runners = new();

    public void Dispose()
    {
        foreach (var runner in _runners)
        {
            runner.Dispose();
        }
    }

    // -- helpers -------------------------------------------------------------------------------------

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private CorpusIndexJobRunner CreateRunner(TimeProvider? time = null)
    {
        var runner = new CorpusIndexJobRunner("Test corpus", NullLogger.Instance, time);
        _runners.Add(runner);
        return runner;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A pass that signals <paramref name="entered"/>, then waits for <paramref name="release"/> or the runner's cancellation.</summary>
    private static Func<CorpusPassContext, CancellationToken, Task<string?>> HeldPass(TaskCompletionSource entered, TaskCompletionSource release, string? message = "held pass done")
    {
        return async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return message;
        };
    }

    private static Func<CorpusPassContext, CancellationToken, Task<string?>> ImmediatePass(string? message) =>
        (_, _) => Task.FromResult(message);

    private static Task StartOrFail(CorpusIndexJobRunner runner, CorpusIndexTrigger trigger, Func<CorpusPassContext, CancellationToken, Task<string?>> pass)
    {
        var task = runner.TryStart(trigger, pass, out var result);
        Assert.Equal(CorpusIndexStartResult.Started, result);
        Assert.NotNull(task);
        return task!;
    }

    // -- starting a pass ------------------------------------------------------------------------------

    [Fact]
    public async Task TryStart_WhenIdle_StartsAndRecordsTheReturnedMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FixedClock();
        var runner = CreateRunner(clock);

        var task = StartOrFail(runner, CorpusIndexTrigger.Startup, ImmediatePass("2,512 articles, 3 unreadable"));
        await task.WaitAsync(WaitLimit, ct);

        Assert.False(runner.IsRunning);
        Assert.Null(runner.CurrentJob);
        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexTrigger.Startup, job.Trigger);
        Assert.Equal(CorpusIndexOutcome.Succeeded, job.Outcome);
        Assert.Equal("2,512 articles, 3 unreadable", job.Message);
        Assert.Equal(clock.Now.UtcDateTime, job.StartedAtUtc);
        Assert.NotNull(job.FinishedAtUtc);
        Assert.Equal(clock.Now.UtcDateTime, job.FinishedAtUtc!.Value);
        Assert.Equal(12, job.Id.Length);
        Assert.Equal(job.Id, runner.LastStartedJobId);
        Assert.Same(job, runner.LastFinishedJob);
    }

    [Fact]
    public async Task TryStart_ManualWhileAPassRuns_ReturnsAlreadyRunningAndNoTask()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        var entered = NewSignal();
        var release = NewSignal();
        int manualInvocations = 0;

        var running = StartOrFail(runner, CorpusIndexTrigger.Startup, HeldPass(entered, release));
        await entered.Task.WaitAsync(WaitLimit, ct);

        var manual = runner.TryStart(CorpusIndexTrigger.Manual, (_, _) =>
        {
            Interlocked.Increment(ref manualInvocations);
            return Task.FromResult<string?>("manual");
        }, out var result);

        Assert.Equal(CorpusIndexStartResult.AlreadyRunning, result);
        Assert.Null(manual);

        release.SetResult();
        await running.WaitAsync(WaitLimit, ct);

        Assert.False(runner.IsRunning);
        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexTrigger.Startup, job.Trigger);
        Assert.Equal(0, Volatile.Read(ref manualInvocations));
    }

    [Fact]
    public async Task TryStart_TwoAutomaticRequestsWhileAPassRuns_QueueExactlyOneFollowUpWithTheLatestDelegate()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        var entered = NewSignal();
        var release = NewSignal();
        int firstInvocations = 0;
        int secondInvocations = 0;

        var running = StartOrFail(runner, CorpusIndexTrigger.Startup, HeldPass(entered, release));
        await entered.Task.WaitAsync(WaitLimit, ct);

        var queuedFirst = runner.TryStart(CorpusIndexTrigger.RepositoryChange, (_, _) =>
        {
            Interlocked.Increment(ref firstInvocations);
            return Task.FromResult<string?>("first request");
        }, out var firstResult);
        var queuedSecond = runner.TryStart(CorpusIndexTrigger.RepositoryChange, (_, _) =>
        {
            Interlocked.Increment(ref secondInvocations);
            return Task.FromResult<string?>("second request");
        }, out var secondResult);

        Assert.Equal(CorpusIndexStartResult.AlreadyRunning, firstResult);
        Assert.Equal(CorpusIndexStartResult.AlreadyRunning, secondResult);
        Assert.NotNull(queuedFirst);
        Assert.NotNull(queuedSecond);
        Assert.Same(queuedFirst, queuedSecond);

        release.SetResult();
        await running.WaitAsync(WaitLimit, ct);
        await queuedSecond!.WaitAsync(WaitLimit, ct);

        Assert.False(runner.IsRunning);
        var jobs = runner.RecentJobs;
        Assert.Equal(2, jobs.Count);
        Assert.Equal(CorpusIndexTrigger.FollowUp, jobs[0].Trigger);
        Assert.Equal(CorpusIndexOutcome.Succeeded, jobs[0].Outcome);
        Assert.Equal("second request", jobs[0].Message);
        Assert.Equal(CorpusIndexTrigger.Startup, jobs[1].Trigger);
        Assert.Equal(0, Volatile.Read(ref firstInvocations));
        Assert.Equal(1, Volatile.Read(ref secondInvocations));
        Assert.Equal(jobs[0].Id, runner.LastStartedJobId);
    }

    [Fact]
    public async Task TryStart_StartedTask_CompletesWithoutWaitingForTheFollowUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        var firstEntered = NewSignal();
        var firstRelease = NewSignal();
        var followUpEntered = NewSignal();
        var followUpRelease = NewSignal();

        var running = StartOrFail(runner, CorpusIndexTrigger.Startup, HeldPass(firstEntered, firstRelease, "first"));
        await firstEntered.Task.WaitAsync(WaitLimit, ct);
        var queued = runner.TryStart(CorpusIndexTrigger.RepositoryChange, HeldPass(followUpEntered, followUpRelease, "follow-up"), out _);
        Assert.NotNull(queued);

        firstRelease.SetResult();
        await running.WaitAsync(WaitLimit, ct);

        Assert.True(runner.IsRunning);
        var current = runner.CurrentJob;
        Assert.NotNull(current);
        Assert.Equal(CorpusIndexTrigger.FollowUp, current!.Trigger);
        Assert.Equal(CorpusIndexOutcome.Running, current.Outcome);
        Assert.False(queued!.IsCompleted);
        var first = Assert.Single(runner.RecentJobs);
        Assert.Equal("first", first.Message);

        await followUpEntered.Task.WaitAsync(WaitLimit, ct);
        followUpRelease.SetResult();
        await queued.WaitAsync(WaitLimit, ct);

        Assert.False(runner.IsRunning);
        Assert.Equal(2, runner.RecentJobs.Count);
        Assert.Equal("follow-up", runner.RecentJobs[0].Message);
    }

    // -- failures -------------------------------------------------------------------------------------

    [Fact]
    public async Task TryStart_PassThrows_RecordsFailedWithTheFirstLineAndTheTaskCompletes()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();

        var task = StartOrFail(runner, CorpusIndexTrigger.Manual, async (_, token) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("The index could not be built.\r\nStack details that are not shown.");
        });
        await task.WaitAsync(WaitLimit, ct);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.False(runner.IsRunning);
        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("The index could not be built.", job.Message);
        Assert.NotNull(job.FinishedAtUtc);
    }

    [Fact]
    public async Task TryStart_PassThrowsBeforeReturningATask_RecordsFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();

        var task = StartOrFail(runner, CorpusIndexTrigger.Manual, (_, _) => throw new ArgumentException("Synchronous failure."));
        await task.WaitAsync(WaitLimit, ct);

        Assert.True(task.IsCompletedSuccessfully);
        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("Synchronous failure.", job.Message);
    }

    [Fact]
    public async Task TryStart_LongExceptionMessage_IsCutToMaxMessageLength()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        string longMessage = new string('x', CorpusIndexJobRunner.MaxMessageLength * 2);

        var task = StartOrFail(runner, CorpusIndexTrigger.Manual, (_, _) => Task.FromException<string?>(new InvalidOperationException(longMessage)));
        await task.WaitAsync(WaitLimit, ct);

        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.NotNull(job.Message);
        Assert.Equal(CorpusIndexJobRunner.MaxMessageLength, job.Message!.Length);
        Assert.EndsWith("…", job.Message);
    }

    [Fact]
    public async Task TryStart_LongReturnedMessage_IsCutToMaxMessageLength()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        string longMessage = new string('y', CorpusIndexJobRunner.MaxMessageLength + 1);

        var task = StartOrFail(runner, CorpusIndexTrigger.Manual, ImmediatePass(longMessage));
        await task.WaitAsync(WaitLimit, ct);

        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Succeeded, job.Outcome);
        Assert.Equal(CorpusIndexJobRunner.MaxMessageLength, job.Message!.Length);
        Assert.EndsWith("…", job.Message);
    }

    [Fact]
    public async Task TryStart_PassCallsFail_RecordsFailedWithThatMessageAndIgnoresTheReturnedOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();

        var task = StartOrFail(runner, CorpusIndexTrigger.Startup, (context, _) =>
        {
            context.Fail("The WikiPath folder does not exist.");
            return Task.FromResult<string?>("ignored");
        });
        await task.WaitAsync(WaitLimit, ct);

        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("The WikiPath folder does not exist.", job.Message);
    }

    [Fact]
    public async Task TryStart_OperationCanceledNotCausedByDispose_RecordsFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();

        var task = StartOrFail(runner, CorpusIndexTrigger.Manual, (_, _) => Task.FromException<string?>(new OperationCanceledException("Stopped by the pass itself.")));
        await task.WaitAsync(WaitLimit, ct);

        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("Stopped by the pass itself.", job.Message);
    }

    // -- history --------------------------------------------------------------------------------------

    [Fact]
    public async Task RecentJobs_KeepsTheTwentyNewestNewestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        const int passes = CorpusIndexJobRunner.MaxRecentJobs + 5;

        for (int i = 0; i < passes; i++)
        {
            var task = StartOrFail(runner, CorpusIndexTrigger.Manual, ImmediatePass("pass " + i));
            await task.WaitAsync(WaitLimit, ct);
        }

        var jobs = runner.RecentJobs;
        Assert.Equal(20, jobs.Count);
        for (int i = 0; i < jobs.Count; i++)
        {
            Assert.Equal("pass " + (passes - 1 - i), jobs[i].Message);
        }
        Assert.Same(jobs[0], runner.LastFinishedJob);
        Assert.Equal(jobs[0].Id, runner.LastStartedJobId);
    }

    // -- live progress --------------------------------------------------------------------------------

    [Fact]
    public async Task CurrentJob_MidPass_ShowsThePhaseAndFileCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        var reading = NewSignal();
        var continueToBuild = NewSignal();
        var building = NewSignal();
        var finish = NewSignal();
        CorpusIndexPhase? initialPhase = null;
        int? initialTotal = -1;
        int? unknownTotal = -1;

        var task = StartOrFail(runner, CorpusIndexTrigger.Manual, async (context, token) =>
        {
            initialPhase = context.Phase;
            initialTotal = context.FilesTotal;
            context.ReportFiles(1, null);
            unknownTotal = context.FilesTotal;

            context.SetPhase(CorpusIndexPhase.ReadingFiles);
            context.ReportFiles(3, 10);
            reading.SetResult();
            await continueToBuild.Task.WaitAsync(token);

            context.ReportFiles(10, 10);
            context.SetPhase(CorpusIndexPhase.BuildingIndex);
            building.SetResult();
            await finish.Task.WaitAsync(token);

            context.SetPhase(CorpusIndexPhase.Publishing);
            return "10 files";
        });

        await reading.Task.WaitAsync(WaitLimit, ct);
        var first = runner.CurrentJob;
        Assert.NotNull(first);
        Assert.Equal(CorpusIndexTrigger.Manual, first!.Trigger);
        Assert.Equal(CorpusIndexOutcome.Running, first.Outcome);
        Assert.Null(first.FinishedAtUtc);
        Assert.Null(first.Message);
        Assert.Equal(CorpusIndexPhase.ReadingFiles, first.Phase);
        Assert.Equal(3, first.FilesRead);
        Assert.Equal(10, first.FilesTotal);
        Assert.Equal(first.Id, runner.LastStartedJobId);
        Assert.Empty(runner.RecentJobs);

        continueToBuild.SetResult();
        await building.Task.WaitAsync(WaitLimit, ct);
        var second = runner.CurrentJob;
        Assert.NotNull(second);
        Assert.Equal(first.Id, second!.Id);
        Assert.Equal(CorpusIndexPhase.BuildingIndex, second.Phase);
        Assert.Equal(10, second.FilesRead);
        Assert.Equal(10, second.FilesTotal);

        finish.SetResult();
        await task.WaitAsync(WaitLimit, ct);

        Assert.Equal(CorpusIndexPhase.Idle, initialPhase);
        Assert.Null(initialTotal);
        Assert.Null(unknownTotal);
        Assert.Null(runner.CurrentJob);
        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(first.Id, job.Id);
        Assert.Equal(CorpusIndexOutcome.Succeeded, job.Outcome);
        Assert.Equal(CorpusIndexPhase.Publishing, job.Phase);
        Assert.Equal(10, job.FilesRead);
        Assert.Equal(10, job.FilesTotal);
        Assert.Equal("10 files", job.Message);
    }

    // -- dispose --------------------------------------------------------------------------------------

    [Fact]
    public async Task Dispose_WhileAPassRuns_RecordsCanceled()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        var entered = NewSignal();
        var neverReleased = NewSignal();

        var task = StartOrFail(runner, CorpusIndexTrigger.Startup, HeldPass(entered, neverReleased));
        await entered.Task.WaitAsync(WaitLimit, ct);

        runner.Dispose();
        await task.WaitAsync(WaitLimit, ct);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.False(runner.IsRunning);
        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Canceled, job.Outcome);
        Assert.Equal(CorpusIndexJobRunner.CanceledMessage, job.Message);
    }

    [Fact]
    public async Task Dispose_WithAQueuedFollowUp_CompletesItsTaskWithoutRunningIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = CreateRunner();
        var entered = NewSignal();
        var neverReleased = NewSignal();
        int followUpInvocations = 0;

        var running = StartOrFail(runner, CorpusIndexTrigger.Startup, HeldPass(entered, neverReleased));
        await entered.Task.WaitAsync(WaitLimit, ct);
        var queued = runner.TryStart(CorpusIndexTrigger.RepositoryChange, (_, _) =>
        {
            Interlocked.Increment(ref followUpInvocations);
            return Task.FromResult<string?>("follow-up");
        }, out _);
        Assert.NotNull(queued);

        runner.Dispose();
        await running.WaitAsync(WaitLimit, ct);
        await queued!.WaitAsync(WaitLimit, ct);

        Assert.True(queued.IsCompletedSuccessfully);
        Assert.False(runner.IsRunning);
        Assert.Equal(0, Volatile.Read(ref followUpInvocations));
        var job = Assert.Single(runner.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Canceled, job.Outcome);
    }

    [Theory]
    [InlineData(CorpusIndexTrigger.Manual)]
    [InlineData(CorpusIndexTrigger.Startup)]
    [InlineData(CorpusIndexTrigger.RepositoryChange)]
    public void TryStart_AfterDispose_ReturnsAlreadyRunningAndNoTask(CorpusIndexTrigger trigger)
    {
        var runner = CreateRunner();
        int invocations = 0;
        runner.Dispose();

        var task = runner.TryStart(trigger, (_, _) =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromResult<string?>("never");
        }, out var result);

        Assert.Equal(CorpusIndexStartResult.AlreadyRunning, result);
        Assert.Null(task);
        Assert.Equal(0, Volatile.Read(ref invocations));
        Assert.Empty(runner.RecentJobs);
        Assert.Null(runner.LastStartedJobId);
    }
}
