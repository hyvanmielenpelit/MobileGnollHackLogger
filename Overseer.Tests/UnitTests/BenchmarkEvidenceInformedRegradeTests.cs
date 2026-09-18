namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The evidence-informed re-grade may withdraw only a deduction a finding bears on, and only on that
/// finding; a re-grade that does anything else is stored as rejected and never counted. One
/// predicate decides what counts, everywhere.
/// </summary>
public class BenchmarkEvidenceInformedRegradeTests
{
    private const string Quote = "Praying at 1 HP is always safe.";
    private const string Basis = "prayer timeout starts at 300.";

    private static BenchmarkClaimVerification V(int index, string claim, BenchmarkClaimVerdict verdict, string? citation, params string[] roles)
        => new(index, claim, verdict, citation, "basis") { Roles = roles.Length > 0 ? roles : null };

    private static BenchmarkRunAnswer ContestedAnswer() => new()
    {
        AccuracyLevel = 3,
        CompletenessLevel = 5,
        ConcisenessLevel = 5,
        ReadabilityLevel = 4,
        CriticalError = true,
        CriticalErrorQuote = Quote,
        AnswerFlags = (int)BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction,
        AssessmentEvidenceJson = JsonSerializer.Serialize(new { accuracy = $"Not in rubric: {Basis}" })
    };

    private static List<BenchmarkClaimVerification> Findings() => new()
    {
        V(0, Quote, BenchmarkClaimVerdict.Supported, "src/pray.c:10", BenchmarkClaimRoles.CriticalErrorQuote),
        V(1, Basis, BenchmarkClaimVerdict.Refuted, "src/pray.c:20", BenchmarkClaimRoles.OutOfRubricBasis),
        V(2, "Gnolls have infravision.", BenchmarkClaimVerdict.Supported, "src/role.c:5", BenchmarkClaimRoles.UnverifiedClaim),
        V(3, "It has no charges.", BenchmarkClaimVerdict.Supported, "src/objects.c:2889", BenchmarkClaimRoles.AccusedQuote),
        V(4, "It weighs 40.", BenchmarkClaimVerdict.Refuted, "src/objects.c:2889", BenchmarkClaimRoles.AccusedQuote)
    };

    // --- Target catalog ---------------------------------------------------------------------

    [Fact]
    public void Targets_OnePerDeductionAFindingBearsOn_WithItsOwnFindingsOnly()
    {
        var targets = BenchmarkService.BuildEvidenceInformedTargets(ContestedAnswer(), Findings());

        Assert.Equal(3, targets.Count);
        Assert.Equal(("T1", BenchmarkEvidenceInformedTarget.CriticalErrorKind, Quote), (targets[0].Id, targets[0].Kind, targets[0].Text));
        Assert.Equal(new[] { "F0" }, targets[0].FindingIds);
        Assert.Equal(("T2", BenchmarkEvidenceInformedTarget.AccuracyKind, BenchmarkClaimRoles.OutOfRubricBasis), (targets[1].Id, targets[1].Kind, targets[1].Source));
        Assert.Equal(new[] { "F1" }, targets[1].FindingIds);
        Assert.Equal(("T3", BenchmarkClaimRoles.AccusedQuote, "It has no charges."), (targets[2].Id, targets[2].Source, targets[2].Text));
        Assert.Equal(new[] { "F3" }, targets[2].FindingIds);
    }

    [Fact]
    public void Targets_AnOrdinarySupportedClaimCreatesNone_AndNoTargetMeansNoRegrade()
    {
        var answer = new BenchmarkRunAnswer { AccuracyLevel = 4, AnswerFlags = (int)BenchmarkAnswerFlags.UnevidencedDeduction };
        var findings = new[] { V(0, "Gnolls have infravision.", BenchmarkClaimVerdict.Supported, "src/role.c:5", BenchmarkClaimRoles.UnverifiedClaim) };

        Assert.Empty(BenchmarkService.BuildEvidenceInformedTargets(answer, findings));
    }

    [Fact]
    public void Targets_NeedACitation_AndTheRightVerdict()
    {
        var findings = new[]
        {
            V(0, Quote, BenchmarkClaimVerdict.Supported, null, BenchmarkClaimRoles.CriticalErrorQuote),
            V(1, Basis, BenchmarkClaimVerdict.Supported, "src/pray.c:20", BenchmarkClaimRoles.OutOfRubricBasis)
        };

        Assert.Empty(BenchmarkService.BuildEvidenceInformedTargets(ContestedAnswer(), findings));
    }

    [Fact]
    public void Targets_ALegacyListIsMatchedByText()
    {
        var legacy = Findings().Take(2).Select(v => v with { Roles = null }).ToList();

        var targets = BenchmarkService.BuildEvidenceInformedTargets(ContestedAnswer(), legacy);

        Assert.Equal(new[] { BenchmarkEvidenceInformedTarget.CriticalErrorKind, BenchmarkEvidenceInformedTarget.AccuracyKind }, targets.Select(t => t.Kind));
    }

    // --- Validation -------------------------------------------------------------------------

    private static List<BenchmarkEvidenceInformedTarget> Targets()
        => BenchmarkService.BuildEvidenceInformedTargets(ContestedAnswer(), Findings());

    private static string Raw(string withdrawn) => $"{{\"accuracyLevel\":5,\"withdrawn\":{withdrawn}}}";

    [Fact]
    public void Validation_AcceptsWithdrawalsOnListedTargets_OnTheirOwnFindings()
    {
        var result = BenchmarkService.ValidateEvidenceInformed(
            Raw("[{\"targetId\":\"T1\",\"findingIds\":[\"F0\"],\"reason\":\"The quoted sentence is true and the clause is about it.\"},"
                + "{\"targetId\":\"T2\",\"findingIds\":[\"F1\"],\"reason\":\"The basis was refuted.\"}]"),
            Targets(), primaryAccuracyLevel: 3, primaryCriticalError: true, regradeAccuracyLevel: 5, regradeCriticalError: false);

        Assert.True(result.Eligible);
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "T1", "T2" }, result.Accepted.Select(a => a.TargetId));
        Assert.Contains("T1 criticalError: \"Praying at 1 HP is always safe.\" (F0)", result.Accepted[0].Summary);
    }

    [Theory]
    [InlineData("[\"Accuracy deduction: the basis\"]", "is not an object")]
    [InlineData("[{\"targetId\":\"T9\",\"findingIds\":[\"F1\"],\"reason\":\"r\"}]", "unknown targetId")]
    [InlineData("[{\"targetId\":\"T2\",\"findingIds\":[\"F1\"],\"reason\":\"r\"},{\"targetId\":\"T2\",\"findingIds\":[\"F1\"],\"reason\":\"r\"}]", "duplicate targetId")]
    [InlineData("[{\"targetId\":\"T2\",\"findingIds\":[\"F1\"],\"reason\":\" \"}]", "empty reason")]
    [InlineData("[{\"targetId\":\"T2\",\"findingIds\":[\"F2\"],\"reason\":\"An ordinary claim was supported.\"}]", "do not bear on this target")]
    [InlineData("[{\"targetId\":\"T2\",\"findingIds\":[],\"reason\":\"r\"}]", "no findingIds")]
    public void Validation_RejectsAMalformedOrUnrelatedWithdrawal(string withdrawn, string expectedError)
    {
        var result = BenchmarkService.ValidateEvidenceInformed(
            Raw(withdrawn), Targets(), primaryAccuracyLevel: 3, primaryCriticalError: true, regradeAccuracyLevel: 3, regradeCriticalError: true);

        Assert.False(result.Eligible);
        Assert.Contains(result.Dropped, d => d.Contains(expectedError, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"accuracyLevel\":3}")]
    [InlineData("{\"withdrawn\":\"T2\"}")]
    [InlineData("not json at all")]
    public void Validation_RejectsAMissingOrNonArrayList(string raw)
    {
        var result = BenchmarkService.ValidateEvidenceInformed(
            raw, Targets(), primaryAccuracyLevel: 3, primaryCriticalError: true, regradeAccuracyLevel: 3, regradeCriticalError: true);

        Assert.False(result.Eligible);
        Assert.Contains("`withdrawn` is missing or not an array.", result.Errors);
    }

    [Fact]
    public void Validation_AnEmptyListIsValid_OnlyWhenAccuracyAndTheCriticalErrorAreUnchanged()
    {
        var unchanged = BenchmarkService.ValidateEvidenceInformed(
            Raw("[]"), Targets(), primaryAccuracyLevel: 3, primaryCriticalError: true, regradeAccuracyLevel: 3, regradeCriticalError: true);
        Assert.True(unchanged.Eligible);

        var raised = BenchmarkService.ValidateEvidenceInformed(
            Raw("[]"), Targets(), primaryAccuracyLevel: 3, primaryCriticalError: true, regradeAccuracyLevel: 5, regradeCriticalError: true);
        Assert.Contains("Accuracy was raised from 3 to 5 with no valid accuracy withdrawal.", raised.Errors);

        var cleared = BenchmarkService.ValidateEvidenceInformed(
            Raw("[]"), Targets(), primaryAccuracyLevel: 3, primaryCriticalError: true, regradeAccuracyLevel: 3, regradeCriticalError: false);
        Assert.Contains("the critical error was removed with no valid criticalError withdrawal.", cleared.Errors);
    }

    [Fact]
    public void Validation_RejectsALowerAccuracy_AndAnAddedCriticalError()
    {
        var result = BenchmarkService.ValidateEvidenceInformed(
            Raw("[]"), Targets(), primaryAccuracyLevel: 4, primaryCriticalError: false, regradeAccuracyLevel: 3, regradeCriticalError: true);

        Assert.Contains("the re-grade lowered Accuracy from 4 to 3.", result.Errors);
        Assert.Contains("the re-grade added a critical error the primary verdict did not have.", result.Errors);
    }

    [Fact]
    public void Validation_AnAccuracyWithdrawalDoesNotLicenceRemovingTheCriticalError()
    {
        var result = BenchmarkService.ValidateEvidenceInformed(
            Raw("[{\"targetId\":\"T2\",\"findingIds\":[\"F1\"],\"reason\":\"The basis was refuted.\"}]"),
            Targets(), primaryAccuracyLevel: 3, primaryCriticalError: true, regradeAccuracyLevel: 5, regradeCriticalError: false);

        Assert.False(result.Eligible);
        Assert.Single(result.Accepted);
        Assert.Contains("the critical error was removed with no valid criticalError withdrawal.", result.Errors);
    }

    [Fact]
    public void Validation_TheHistoricalReaderStillReadsAStringArray()
    {
        Assert.Equal(new[] { "Accuracy deduction: x" }, BenchmarkService.ReadWithdrawn("{\"withdrawn\":[\"Accuracy deduction: x\"]}"));
    }

    // --- The one predicate ------------------------------------------------------------------

    private static BenchmarkRun RunStamped(string harness) => new() { HarnessVersion = harness };

    [Fact]
    public void Eligibility_AValidatedRecordCountsWhenItPassed()
    {
        var answer = new BenchmarkRunAnswer
        {
            EvidenceInformedQualityScore = 80,
            EvidenceInformedJson = "{\"validationVersion\":1,\"eligibleForSensitivity\":true}"
        };
        Assert.True(BenchmarkService.IsEligibleEvidenceInformedRegrade(RunStamped("31"), answer));

        answer.EvidenceInformedJson = "{\"validationVersion\":1,\"eligibleForSensitivity\":false}";
        Assert.False(BenchmarkService.IsEligibleEvidenceInformedRegrade(RunStamped("31"), answer));
    }

    [Fact]
    public void Eligibility_MissingProvenanceIsIneligibleFromHarness31_AndLegacyBeforeIt()
    {
        var answer = new BenchmarkRunAnswer
        {
            EvidenceInformedQualityScore = 80,
            EvidenceInformedJson = "{\"withdrawn\":[\"x\"]}"
        };

        Assert.False(BenchmarkService.IsEligibleEvidenceInformedRegrade(RunStamped("31"), answer));
        Assert.False(BenchmarkService.IsLegacyEvidenceInformed(RunStamped("31"), answer));

        Assert.True(BenchmarkService.IsEligibleEvidenceInformedRegrade(RunStamped("30"), answer));
        Assert.True(BenchmarkService.IsLegacyEvidenceInformed(RunStamped("30"), answer));

        answer.EvidenceInformedQualityScore = null;
        Assert.False(BenchmarkService.IsEligibleEvidenceInformedRegrade(RunStamped("30"), answer));
    }

    // --- No vacuous level 6 -----------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkAnswerStatus.EmptyAnswer, "STOP", BenchmarkAssessmentStatus.Scored)]
    [InlineData(BenchmarkAnswerStatus.ProviderError, null, BenchmarkAssessmentStatus.Failed)]
    [InlineData(BenchmarkAnswerStatus.Failed, null, BenchmarkAssessmentStatus.Failed)]
    [InlineData(BenchmarkAnswerStatus.Canceled, null, BenchmarkAssessmentStatus.Failed)]
    public async Task AnAnswerWithNoText_NeverReachesTheGrader_SoCannotBeAVacuousSix(
        BenchmarkAnswerStatus status, string? finishReason, BenchmarkAssessmentStatus expectedAssessment)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        // The agent loop is null: reaching the grader would throw.
        var service = new BenchmarkService(
            scopeFactory, null!, null!, null!, new BenchmarkRunManager(), new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new ConfigurationBuilder().Build(), NullLogger<BenchmarkService>.Instance);

        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q",
            AnswerText = string.Empty,
            Status = status,
            ProviderFinishReason = finishReason
        };

        await using var db = new ApplicationDbContext(dbOptions);
        await service.ExecutePerQuestionAssessmentAsync(
            db, null!, new BenchmarkRun(), answer, null, new SystemAiApiConfiguration(), "key",
            BenchmarkScoringConstants.Default, CancellationToken.None);

        Assert.Equal(expectedAssessment, answer.AssessmentStatus);
        Assert.Null(answer.AccuracyLevel);
        Assert.NotEqual(100, answer.QualityScore ?? 0);
        if (expectedAssessment == BenchmarkAssessmentStatus.Scored)
        {
            Assert.Equal(0, answer.QualityScore);
        }
        else
        {
            Assert.Null(answer.QualityScore);
        }
    }
}
