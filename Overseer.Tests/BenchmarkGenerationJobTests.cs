namespace Overseer.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Covers the pure factories and DTO mapping on <see cref="BenchmarkGenerationJob"/>: the retry
/// and regeneration job builders, and <see cref="BenchmarkGenerationJob.ToDto"/>.
/// </summary>
public class BenchmarkGenerationJobTests
{
    private static GeneratorSelection SampleGenerator(long configId = 55) =>
        new(configId, "New Model", "OpenAI", "gpt-5", "high", "extended", "flex");

    /// <summary>
    /// A completed generation job with one fully failed band, one partially generated band and
    /// one fully generated band, for exercising <see cref="BenchmarkGenerationJob.CreateRetry"/>.
    /// </summary>
    private static BenchmarkGenerationJob CreatePreviousJob() => new()
    {
        Id = "prev-job-id",
        SuiteId = 7,
        SuiteName = "Suite Seven",
        GeneratorConfigId = 1,
        GeneratorDisplayName = "Old Model",
        GeneratorProvider = "Google",
        GeneratorModelId = "gemini-2.5-pro",
        GeneratorThinkingLevel = "medium",
        Instructions = "Old instructions",
        JobKind = "Generation",
        Cts = new CancellationTokenSource(),
        Items = new List<BenchmarkGenerationJobItem>
        {
            new()
            {
                Kind = BenchmarkGenerationItemKind.Band,
                Difficulty = BenchmarkDifficulty.Simple,
                RequestedCount = 6,
                GeneratedCount = 0,
                Status = BenchmarkGenerationItemStatus.Failed,
                CreatedQuestionIds = new List<long>()
            },
            new()
            {
                Kind = BenchmarkGenerationItemKind.Band,
                Difficulty = BenchmarkDifficulty.Intermediate,
                RequestedCount = 6,
                GeneratedCount = 4,
                Status = BenchmarkGenerationItemStatus.Completed,
                CreatedQuestionIds = new List<long> { 10, 11, 12, 13 }
            },
            new()
            {
                Kind = BenchmarkGenerationItemKind.Band,
                Difficulty = BenchmarkDifficulty.Advanced,
                RequestedCount = 6,
                GeneratedCount = 6,
                Status = BenchmarkGenerationItemStatus.Completed,
                CreatedQuestionIds = new List<long> { 20, 21, 22, 23, 24, 25 }
            }
        }
    };

    [Fact]
    public void CreateRetry_FailedBand_RequestsTheFullCount()
    {
        var previous = CreatePreviousJob();

        var retry = BenchmarkGenerationJob.CreateRetry(
            previous, new[] { BenchmarkDifficulty.Simple }, discardExisting: false, SampleGenerator(), "Retry instructions", "user-1");

        var item = retry.Items.Single(i => i.Difficulty == BenchmarkDifficulty.Simple);
        Assert.Equal(6, item.RequestedCount);
        Assert.Equal(BenchmarkGenerationItemStatus.Pending, item.Status);
        Assert.Empty(item.QuestionIdsToDiscard);
    }

    [Fact]
    public void CreateRetry_PartialBand_RequestsOnlyTheShortfall()
    {
        var previous = CreatePreviousJob();

        var retry = BenchmarkGenerationJob.CreateRetry(
            previous, new[] { BenchmarkDifficulty.Intermediate }, discardExisting: false, SampleGenerator(), "Retry instructions", null);

        var item = retry.Items.Single(i => i.Difficulty == BenchmarkDifficulty.Intermediate);
        Assert.Equal(2, item.RequestedCount); // 6 requested - 4 already generated
        Assert.Equal(BenchmarkGenerationItemStatus.Pending, item.Status);
    }

    [Fact]
    public void CreateRetry_DiscardExisting_RequestsTheFullCountAndCarriesTheCreatedIds()
    {
        var previous = CreatePreviousJob();

        var retry = BenchmarkGenerationJob.CreateRetry(
            previous, new[] { BenchmarkDifficulty.Intermediate }, discardExisting: true, SampleGenerator(), "Retry instructions", null);

        var item = retry.Items.Single(i => i.Difficulty == BenchmarkDifficulty.Intermediate);
        Assert.Equal(6, item.RequestedCount);
        Assert.Equal(new long[] { 10, 11, 12, 13 }, item.QuestionIdsToDiscard);
    }

    [Fact]
    public void CreateRetry_UnselectedBands_AreSkippedWithZeroRequested()
    {
        var previous = CreatePreviousJob();

        var retry = BenchmarkGenerationJob.CreateRetry(
            previous, new[] { BenchmarkDifficulty.Simple }, discardExisting: false, SampleGenerator(), "Retry instructions", null);

        var intermediate = retry.Items.Single(i => i.Difficulty == BenchmarkDifficulty.Intermediate);
        Assert.Equal(BenchmarkGenerationItemStatus.Skipped, intermediate.Status);
        Assert.Equal(0, intermediate.RequestedCount);

        var advanced = retry.Items.Single(i => i.Difficulty == BenchmarkDifficulty.Advanced);
        Assert.Equal(BenchmarkGenerationItemStatus.Skipped, advanced.Status);
        Assert.Equal(0, advanced.RequestedCount);
    }

    [Fact]
    public void CreateRetry_WhenNothingIsRequested_Throws()
    {
        var previous = CreatePreviousJob();

        // Advanced is already fully generated (6 of 6), so without discardExisting the shortfall is 0.
        Assert.Throws<ArgumentException>(() =>
            BenchmarkGenerationJob.CreateRetry(
                previous, new[] { BenchmarkDifficulty.Advanced }, discardExisting: false, SampleGenerator(), "Retry instructions", null));
    }

    [Fact]
    public void CreateRetry_RecordsTheOverrideGeneratorAndLinksThePreviousJob()
    {
        var previous = CreatePreviousJob();
        var generator = SampleGenerator(configId: 99);

        var retry = BenchmarkGenerationJob.CreateRetry(
            previous, new[] { BenchmarkDifficulty.Simple }, discardExisting: false, generator, "New instructions", "user-9");

        Assert.Equal(99, retry.GeneratorConfigId);
        Assert.Equal("New Model", retry.GeneratorDisplayName);
        Assert.Equal("OpenAI", retry.GeneratorProvider);
        Assert.Equal("gpt-5", retry.GeneratorModelId);
        Assert.Equal("high", retry.GeneratorThinkingLevel);
        Assert.Equal("extended", retry.GeneratorReasoningMode);
        Assert.Equal("flex", retry.GeneratorServiceTier);
        Assert.Equal("New instructions", retry.Instructions);
        Assert.Equal("user-9", retry.StartedByUserId);
        Assert.Equal(previous.Id, retry.RetryOfJobId);
        Assert.Equal("Retry", retry.JobKind);
        Assert.Equal(previous.SuiteId, retry.SuiteId);
        Assert.Equal(previous.SuiteName, retry.SuiteName);
    }

    [Fact]
    public void CreateRegeneration_BuildsOnePendingItemPerTarget_WithKindAndDifficulty()
    {
        string longText = "First words of the target question. " + new string('x', 200);
        var targets = new List<(long Id, int OrderIndex, BenchmarkDifficulty Difficulty, string Text)>
        {
            (101, 1, BenchmarkDifficulty.Simple, "What is the most urgent threat this turn?"),
            (102, 2, BenchmarkDifficulty.Advanced, longText)
        };

        var job = BenchmarkGenerationJob.CreateRegeneration(
            7, "Suite Seven", 99, "Snapshot Name", targets, BenchmarkGenerationItemKind.ReplaceQuestion, SampleGenerator(), "Instructions", "user-1");

        Assert.Equal(2, job.Items.Count);
        Assert.All(job.Items, i =>
        {
            Assert.Equal(BenchmarkGenerationItemKind.ReplaceQuestion, i.Kind);
            Assert.Equal(BenchmarkGenerationItemStatus.Pending, i.Status);
            Assert.Equal(1, i.RequestedCount);
        });

        var first = job.Items.Single(i => i.TargetQuestionId == 101);
        Assert.Equal(1, first.TargetQuestionOrderIndex);
        Assert.Equal(BenchmarkDifficulty.Simple, first.Difficulty);
        Assert.Equal("What is the most urgent threat this turn?", first.TargetQuestionExcerpt);

        var second = job.Items.Single(i => i.TargetQuestionId == 102);
        Assert.Equal(120, second.TargetQuestionExcerpt!.Length);

        Assert.Equal("Regeneration", job.JobKind);
        Assert.Equal(7, job.SuiteId);
        Assert.Equal("Suite Seven", job.SuiteName);
        Assert.Equal(99, job.GameSnapshotId);
        Assert.Equal("Snapshot Name", job.GameSnapshotName);
    }

    [Fact]
    public void CreateRegeneration_WithNoTargets_Throws()
    {
        var targets = new List<(long Id, int OrderIndex, BenchmarkDifficulty Difficulty, string Text)>();

        Assert.Throws<ArgumentException>(() =>
            BenchmarkGenerationJob.CreateRegeneration(
                7, "Suite Seven", null, null, targets, BenchmarkGenerationItemKind.RubricOnly, SampleGenerator(), "Instructions", null));
    }

    [Fact]
    public void ToDto_MapsKindTargetCountsAndGeneratorFacts()
    {
        var job = new BenchmarkGenerationJob
        {
            SuiteId = 3,
            SuiteName = "Suite",
            GeneratorConfigId = 8,
            GeneratorDisplayName = "Model",
            GeneratorProvider = "Anthropic",
            GeneratorModelId = "claude-x",
            GeneratorThinkingLevel = "high",
            GeneratorReasoningMode = "extended",
            GeneratorServiceTier = "flex",
            GameSnapshotId = 44,
            GameSnapshotName = "Snap",
            Instructions = "Do it",
            JobKind = "Retry",
            RetryOfJobId = "prev-id",
            Cts = new CancellationTokenSource(),
            Items = new List<BenchmarkGenerationJobItem>
            {
                new()
                {
                    Kind = BenchmarkGenerationItemKind.RubricOnly,
                    TargetQuestionId = 55,
                    TargetQuestionOrderIndex = 4,
                    TargetQuestionExcerpt = "Excerpt",
                    StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    CompletedAtUtc = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc),
                    ModelCalls = 2,
                    PromptTokens = 100,
                    OutputTokens = 50,
                    CreatedQuestionIds = new List<long> { 1, 2 },
                    UpdatedQuestionIds = new List<long> { 55 },
                    QuestionIdsToDiscard = new List<long> { 9 },
                    Status = BenchmarkGenerationItemStatus.Completed
                }
            }
        };

        var dto = job.ToDto();

        Assert.Equal("Retry", dto.JobKind);
        Assert.Equal("prev-id", dto.RetryOfJobId);
        Assert.Equal("Do it", dto.Instructions);
        Assert.Equal("Anthropic", dto.GeneratorProvider);
        Assert.Equal("claude-x", dto.GeneratorModelId);
        Assert.Equal("high", dto.GeneratorThinkingLevel);
        Assert.Equal("extended", dto.GeneratorReasoningMode);
        Assert.Equal("flex", dto.GeneratorServiceTier);
        Assert.Equal(44, dto.GameSnapshotId);
        Assert.Equal("Snap", dto.GameSnapshotName);

        var itemDto = Assert.Single(dto.Items);
        Assert.Equal("RubricOnly", itemDto.Kind);
        Assert.Equal(55, itemDto.TargetQuestionId);
        Assert.Equal(4, itemDto.TargetQuestionOrderIndex);
        Assert.Equal("Excerpt", itemDto.TargetQuestionExcerpt);
        Assert.NotNull(itemDto.StartedAtUtc);
        Assert.NotNull(itemDto.CompletedAtUtc);
        Assert.Equal(2, itemDto.ModelCalls);
        Assert.Equal(100, itemDto.PromptTokens);
        Assert.Equal(50, itemDto.OutputTokens);
        Assert.Equal(2, itemDto.CreatedQuestionCount);
        Assert.Equal(1, itemDto.UpdatedQuestionCount);
        Assert.Equal(1, itemDto.DiscardedQuestionCount);
    }
}
