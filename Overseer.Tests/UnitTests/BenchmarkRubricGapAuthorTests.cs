namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The Rubric Gap Author: what it may draft, and what an acceptance actually writes.
///
/// The whole subsystem rests on one rule — a draft is never a rubric. Nothing the author produces
/// reaches a question except through an explicit human acceptance of one draft at a time, and the
/// text that lands is the operator's submission rather than the model's. These tests hold both
/// halves of that: the eligibility gate that keeps an unsupported claim out of the drafting set at
/// all, and the acceptance path that records who actually authored the words.
/// </summary>
public class BenchmarkRubricGapAuthorTests
{
    // -------------------------------------------------------------------------------------------
    // Eligibility: what may be drafted at all
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A cluster the verifier supported with a citation is the only eligible kind. Absorbing one
    /// family's invention into the answer key is the exact failure the subsystem exists to prevent,
    /// so cross-model agreement alone is not enough.
    /// </summary>
    [Theory]
    [InlineData(BenchmarkRubricGapVerdict.VerifiedRubricGap, "monst.c:1420", true)]
    [InlineData(BenchmarkRubricGapVerdict.VerifiedRubricGap, null, false)]
    [InlineData(BenchmarkRubricGapVerdict.VerifiedRubricGap, "   ", false)]
    [InlineData(BenchmarkRubricGapVerdict.LikelyRubricGap, "monst.c:1420", false)]
    [InlineData(BenchmarkRubricGapVerdict.LikelyHallucination, "monst.c:1420", false)]
    public void IsClusterEligible_RequiresAVerifiedVerdictAndACitation(
        BenchmarkRubricGapVerdict verdict, string? citation, bool expected)
    {
        Assert.Equal(expected, BenchmarkRubricGapAuthorService.IsClusterEligible(verdict, citation));
    }

    private static BenchmarkUnverifiedClaimSample Sample(
        string claim,
        string provider = "OpenAI",
        string modelId = "gpt-5.6-luna",
        long runId = 1,
        BenchmarkClaimVerdict? verdict = null,
        string? citation = null) => new()
        {
            QuestionId = 1,
            QuestionOrderIndex = 1,
            ItemRevisionUsed = 1,
            RunId = runId,
            Provider = provider,
            ModelId = modelId,
            Claim = claim,
            VerificationVerdict = verdict,
            Citation = citation
        };

    /// <summary>
    /// The claim two families agreed on but no verifier ever supported never reaches the drafting
    /// set. This is the "a draft citing a claim the verifier did not support is rejected" rule,
    /// enforced at the point the drafting set is built rather than after a draft exists.
    /// </summary>
    [Fact]
    public void BuildEligibleClusters_ExcludesAClusterNoVerifierSupported()
    {
        var samples = new[]
        {
            Sample("Gnolls gain infravision at experience level 1", provider: "OpenAI", modelId: "gpt-5.6-luna"),
            Sample("Gnolls gain infravision at experience level one", provider: "Anthropic", modelId: "claude-opus-5", runId: 2)
        };

        var eligible = BenchmarkRubricGapAuthorService.BuildEligibleClusters(samples);

        Assert.Empty(eligible);
    }

    [Fact]
    public void BuildEligibleClusters_IncludesAClusterTheVerifierSupportedWithACitation()
    {
        var samples = new[]
        {
            Sample("Gnolls gain infravision at experience level 1", provider: "OpenAI", modelId: "gpt-5.6-luna",
                verdict: BenchmarkClaimVerdict.Supported, citation: "monst.c:1420"),
            Sample("Gnolls gain infravision at experience level one", provider: "Anthropic", modelId: "claude-opus-5", runId: 2)
        };

        var eligible = BenchmarkRubricGapAuthorService.BuildEligibleClusters(samples);

        var (cluster, evidence) = Assert.Single(eligible);
        Assert.Equal(BenchmarkRubricGapVerdict.VerifiedRubricGap, cluster.Verdict);
        Assert.Equal("monst.c:1420", evidence.Citation);
        Assert.Equal("1:0", evidence.ClusterKey);
        Assert.Equal(2, evidence.Recurrence);
    }

    /// <summary>
    /// A Refuted claim carries a citation of its own — the source that contradicts it. Treating
    /// that as evidence for an addition would fold a refutation into the answer key as a fact.
    /// </summary>
    [Fact]
    public void BuildEligibleClusters_DoesNotTreatARefutedClaimsCitationAsSupport()
    {
        var samples = new[]
        {
            Sample("Gnolls gain infravision at experience level 1", provider: "OpenAI", modelId: "gpt-5.6-luna",
                verdict: BenchmarkClaimVerdict.Refuted, citation: "monst.c:1420"),
            Sample("Gnolls gain infravision at experience level one", provider: "Anthropic", modelId: "claude-opus-5", runId: 2)
        };

        Assert.Empty(BenchmarkRubricGapAuthorService.BuildEligibleClusters(samples));
    }

    // -------------------------------------------------------------------------------------------
    // Draft parsing: the prompt's rules enforced here rather than trusted there
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ParseDraft_RejectsAProposalWithNoCitation()
    {
        var result = BenchmarkRubricGapAuthorService.ParseDraft(
            """{"proposeAddition": true, "proposedText": "Gnolls have infravision."}""");

        Assert.False(result.Success);
        Assert.Contains(result.ValidationErrors, e => e.Contains("citation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseDraft_RejectsAProposalWithNoText()
    {
        var result = BenchmarkRubricGapAuthorService.ParseDraft(
            """{"proposeAddition": true, "citation": "monst.c:1420"}""");

        Assert.False(result.Success);
        Assert.Contains(result.ValidationErrors, e => e.Contains("proposedText", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"The rubric already covers this" is a useful answer, not a failure.</summary>
    [Fact]
    public void ParseDraft_AcceptsARefusalToProposeWithoutRequiringTextOrCitation()
    {
        var result = BenchmarkRubricGapAuthorService.ParseDraft(
            """{"proposeAddition": false, "justification": "The rubric already covers infravision."}""");

        Assert.True(result.Success);
        Assert.False(result.ProposeAddition);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void ParseDraft_ReadsACompleteProposal()
    {
        var result = BenchmarkRubricGapAuthorService.ParseDraft(
            """
            {
              "proposeAddition": true,
              "proposedText": "Gnolls gain infravision at experience level 1.",
              "citation": "monst.c:1420",
              "justification": "Verified against the monster table.",
              "confidenceNote": "High."
            }
            """);

        Assert.True(result.Success);
        Assert.Equal("Gnolls gain infravision at experience level 1.", result.ProposedText);
        Assert.Equal("monst.c:1420", result.Citation);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void ParseDraft_ReportsAMalformedResponseRatherThanThrowing()
    {
        var result = BenchmarkRubricGapAuthorService.ParseDraft("not json at all");

        Assert.False(result.Success);
        Assert.NotEmpty(result.ValidationErrors);
    }

    // -------------------------------------------------------------------------------------------
    // Acceptance: the only path from a draft into a rubric
    // -------------------------------------------------------------------------------------------

    private static (AdminBenchmarkController Controller, ApplicationDbContext Db, BenchmarkRubricGapAuthorJobManager Jobs)
        CreateController()
    {
        string dbName = Guid.NewGuid().ToString();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var guard = new BenchmarkComplianceGuard(config, db);
        var runManager = new BenchmarkRunManager();

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddScoped<SystemAiConfigService>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var scoringProfileService = new BenchmarkScoringProfileService(
            scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance);
        var jobs = new BenchmarkRubricGapAuthorJobManager();

        // Only the acceptance endpoint is exercised, and it touches the DbContext and the job
        // manager alone. The remaining dependencies are unreachable from it.
        var controller = new AdminBenchmarkController(
            db, null!, scoringProfileService, runManager, new BenchmarkDifficultyJobManager(), guard, scopeFactory,
            null!, null!, null!, null!, null!, null!, null!, null!, jobs, null!,
            null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "operator-1") }, "TestAuth"))
                }
            }
        };

        return (controller, db, jobs);
    }

    private static async Task<BenchmarkQuestion> SeedQuestionAsync(ApplicationDbContext db, string? expectedPoints)
    {
        var suite = new BenchmarkSuite { Name = "Suite", Description = "Desc" };
        var question = new BenchmarkQuestion
        {
            QuestionText = "Which intrinsics do gnolls gain?",
            OrderIndex = 1,
            Difficulty = BenchmarkDifficulty.Intermediate,
            AssessedDifficulty = 55,
            ExpectedPoints = expectedPoints,
            ItemRevision = 3
        };
        suite.Questions.Add(question);
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync();
        return question;
    }

    private static BenchmarkRubricGapAuthorJob SeedJob(
        BenchmarkRubricGapAuthorJobManager jobs, long questionId, string proposedText)
    {
        var job = new BenchmarkRubricGapAuthorJob
        {
            SuiteId = 1,
            SuiteName = "Suite",
            AuthorConfigId = 7,
            AuthorDisplayName = "Claude Opus 5",
            AuthorProviderUsed = "Anthropic",
            AuthorModelIdUsed = "claude-opus-5",
            Cts = new CancellationTokenSource(),
            Drafts =
            {
                new BenchmarkRubricGapAuthorDraft
                {
                    ClusterKey = $"{questionId}:0",
                    QuestionId = questionId,
                    QuestionOrderIndex = 1,
                    Claims = { "Gnolls gain infravision at experience level 1" },
                    ProposedText = proposedText,
                    Citation = "monst.c:1420",
                    Status = BenchmarkRubricGapAuthorDraftStatus.Completed
                }
            }
        };
        Assert.True(jobs.TryStart(job, out _));
        return job;
    }

    [Fact]
    public async Task Accept_AppendsTheSubmittedTextAndBumpsExactlyOneItemRevision()
    {
        var (controller, db, jobs) = CreateController();
        var question = await SeedQuestionAsync(db, "Mentions infravision.");
        var job = SeedJob(jobs, question.Id, "Gnolls gain infravision at experience level 1.");

        var result = await controller.AcceptRubricAddition(question.Id, new AcceptRubricAdditionRequest
        {
            AcceptedText = "Gnolls gain infravision at experience level 1.",
            JobId = job.Id,
            ClusterKey = $"{question.Id}:0"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);

        var stored = await db.BenchmarkQuestions.FirstAsync(q => q.Id == question.Id);
        Assert.Contains("Mentions infravision.", stored.ExpectedPoints);
        Assert.Contains("Gnolls gain infravision at experience level 1.", stored.ExpectedPoints);
        // A rubric edit is a content change: exactly one bump, from 3 to 4.
        Assert.Equal(4, stored.ItemRevision);
        // The same clear-and-bump the question editor performs, so statistics cannot straddle it.
        Assert.Null(stored.AssessedDifficulty);

        var acceptance = Assert.Single(db.BenchmarkRubricAdditionAcceptances);
        Assert.Equal(4, acceptance.ItemRevisionAfter);
    }

    /// <summary>
    /// The acceptance stores what the operator submitted, and records that it was not the model's
    /// words. A verbatim acceptance and an edited one are different authorship claims.
    /// </summary>
    [Fact]
    public async Task Accept_StoresAnEditedSubmissionAsEditedAndKeepsTheDraft()
    {
        var (controller, db, jobs) = CreateController();
        var question = await SeedQuestionAsync(db, null);
        var job = SeedJob(jobs, question.Id, "Gnolls gain infravision at experience level 1.");

        await controller.AcceptRubricAddition(question.Id, new AcceptRubricAdditionRequest
        {
            AcceptedText = "Gnolls gain infravision from experience level 1 onward.",
            JobId = job.Id,
            ClusterKey = $"{question.Id}:0"
        }, CancellationToken.None);

        var acceptance = Assert.Single(db.BenchmarkRubricAdditionAcceptances);
        Assert.False(acceptance.AcceptedVerbatim);
        Assert.Equal("Gnolls gain infravision from experience level 1 onward.", acceptance.AcceptedText);
        // The draft is kept verbatim, so the edit stays reconstructable.
        Assert.Equal("Gnolls gain infravision at experience level 1.", acceptance.DraftText);

        var stored = await db.BenchmarkQuestions.FirstAsync(q => q.Id == question.Id);
        Assert.Equal("Gnolls gain infravision from experience level 1 onward.", stored.ExpectedPoints);
        Assert.DoesNotContain("at experience level 1.", stored.ExpectedPoints);
    }

    [Fact]
    public async Task Accept_StoresAnUnmodifiedSubmissionAsVerbatim()
    {
        var (controller, db, jobs) = CreateController();
        var question = await SeedQuestionAsync(db, null);
        var job = SeedJob(jobs, question.Id, "Gnolls gain infravision at experience level 1.");

        await controller.AcceptRubricAddition(question.Id, new AcceptRubricAdditionRequest
        {
            AcceptedText = "Gnolls gain infravision at experience level 1.",
            JobId = job.Id,
            ClusterKey = $"{question.Id}:0"
        }, CancellationToken.None);

        var acceptance = Assert.Single(db.BenchmarkRubricAdditionAcceptances);
        Assert.True(acceptance.AcceptedVerbatim);
    }

    /// <summary>The provenance is the point of the row: both shapes of acceptance carry it.</summary>
    [Theory]
    [InlineData("Gnolls gain infravision at experience level 1.")]
    [InlineData("Gnolls gain infravision from experience level 1 onward.")]
    public async Task Accept_RecordsTheDraftingModelAndTheCitation(string submitted)
    {
        var (controller, db, jobs) = CreateController();
        var question = await SeedQuestionAsync(db, null);
        var job = SeedJob(jobs, question.Id, "Gnolls gain infravision at experience level 1.");

        await controller.AcceptRubricAddition(question.Id, new AcceptRubricAdditionRequest
        {
            AcceptedText = submitted,
            JobId = job.Id,
            ClusterKey = $"{question.Id}:0"
        }, CancellationToken.None);

        var acceptance = Assert.Single(db.BenchmarkRubricAdditionAcceptances);
        Assert.Equal("monst.c:1420", acceptance.Citation);
        Assert.Equal(7, acceptance.AuthorModelConfigurationId);
        Assert.Equal("Anthropic", acceptance.AuthorProviderUsed);
        Assert.Equal("claude-opus-5", acceptance.AuthorModelIdUsed);
        Assert.Equal("Claude Opus 5", acceptance.AuthorModelDisplayName);
        Assert.Equal("Gnolls gain infravision at experience level 1", acceptance.ClusterClaim);
        Assert.Equal("operator-1", acceptance.AcceptedByUserId);
    }

    [Fact]
    public async Task Accept_AppliesOneDraftOnly_LeavingEveryOtherQuestionUntouched()
    {
        var (controller, db, jobs) = CreateController();
        var question = await SeedQuestionAsync(db, null);
        var suite = await db.BenchmarkSuites.Include(s => s.Questions).FirstAsync();
        var other = new BenchmarkQuestion
        {
            QuestionText = "Second question",
            OrderIndex = 2,
            Difficulty = BenchmarkDifficulty.Simple,
            AssessedDifficulty = 25,
            ExpectedPoints = "Untouched.",
            ItemRevision = 3,
            BenchmarkSuiteId = suite.Id
        };
        db.BenchmarkQuestions.Add(other);
        await db.SaveChangesAsync();

        var job = SeedJob(jobs, question.Id, "Gnolls gain infravision at experience level 1.");
        await controller.AcceptRubricAddition(question.Id, new AcceptRubricAdditionRequest
        {
            AcceptedText = "Gnolls gain infravision at experience level 1.",
            JobId = job.Id,
            ClusterKey = $"{question.Id}:0"
        }, CancellationToken.None);

        var untouched = await db.BenchmarkQuestions.FirstAsync(q => q.Id == other.Id);
        Assert.Equal("Untouched.", untouched.ExpectedPoints);
        Assert.Equal(3, untouched.ItemRevision);
        Assert.Equal(25, untouched.AssessedDifficulty);
        Assert.Single(db.BenchmarkRubricAdditionAcceptances);
    }

    [Fact]
    public async Task Accept_RefusesEmptyText()
    {
        var (controller, db, _) = CreateController();
        var question = await SeedQuestionAsync(db, "Existing.");

        var result = await controller.AcceptRubricAddition(question.Id, new AcceptRubricAdditionRequest
        {
            AcceptedText = "   "
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        var stored = await db.BenchmarkQuestions.FirstAsync(q => q.Id == question.Id);
        Assert.Equal("Existing.", stored.ExpectedPoints);
        Assert.Equal(3, stored.ItemRevision);
        Assert.Empty(db.BenchmarkRubricAdditionAcceptances);
    }

    [Fact]
    public async Task Accept_RefusesADraftBelongingToAnotherQuestion()
    {
        var (controller, db, jobs) = CreateController();
        var question = await SeedQuestionAsync(db, "Existing.");
        // The draft names a different question than the route does.
        var job = SeedJob(jobs, question.Id + 999, "Gnolls gain infravision at experience level 1.");

        var result = await controller.AcceptRubricAddition(question.Id, new AcceptRubricAdditionRequest
        {
            AcceptedText = "Gnolls gain infravision at experience level 1.",
            JobId = job.Id,
            ClusterKey = $"{question.Id + 999}:0"
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        var stored = await db.BenchmarkQuestions.FirstAsync(q => q.Id == question.Id);
        Assert.Equal("Existing.", stored.ExpectedPoints);
        Assert.Empty(db.BenchmarkRubricAdditionAcceptances);
    }

    [Fact]
    public async Task Accept_ReturnsNotFoundForAnUnknownQuestion()
    {
        var (controller, db, _) = CreateController();
        await SeedQuestionAsync(db, "Existing.");

        var result = await controller.AcceptRubricAddition(4242, new AcceptRubricAdditionRequest
        {
            AcceptedText = "Anything."
        }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
