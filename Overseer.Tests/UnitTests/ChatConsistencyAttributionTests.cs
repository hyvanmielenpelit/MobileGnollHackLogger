namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Xunit;

/// <summary>
/// The attribution decision table, one test per row on synthetic endpoint results, events and controls,
/// and the vocabulary rule: no label or evidence names an intent or a mechanism.
/// </summary>
public class ChatConsistencyAttributionTests
{
    private static readonly ChatConsistencyEventView ToolGuidesEvent = new()
    {
        AtUtc = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc),
        Kind = OverseerEventKinds.ToolGuides,
        Label = "tool guides edited on 2026-10-03 (run #12)",
        From = "aaa",
        To = "bbb",
        RunId = 12,
        PreviousRunId = 11,
        InTargetSeries = true
    };

    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [ChatConsistencyEndpointIds.Quality] = "Quality",
        [ChatConsistencyEndpointIds.TimeToFirstAnswerText] = "Time to first answer text",
        [ChatConsistencyEndpointIds.StreamingRate] = "Answer streaming rate",
        [ChatConsistencyEndpointIds.Work] = "Work per turn",
        [ChatConsistencyEndpointIds.Cost] = "Cost per question",
        [ChatConsistencyEndpointIds.ProviderErrorRate] = "Provider error rate"
    };

    private static ChatConsistencyAttributionEndpoint Endpoint(
        string id,
        ConsistencyVerdict verdict,
        ChatConsistencyEvidenceGrade grade = ChatConsistencyEvidenceGrade.Established)
    {
        bool work = id == ChatConsistencyEndpointIds.Work;
        bool availability = id == ChatConsistencyEndpointIds.ProviderErrorRate;
        bool higherIsBetter = id is ChatConsistencyEndpointIds.Quality or ChatConsistencyEndpointIds.StreamingRate;
        double estimate = verdict switch
        {
            ConsistencyVerdict.ChangedDegraded => higherIsBetter ? -0.5 : 0.5,
            ConsistencyVerdict.ChangedImproved => higherIsBetter ? 0.5 : -0.5,
            ConsistencyVerdict.ChangedNegligible => 0.01,
            _ => 0.0
        };

        string label = verdict switch
        {
            ConsistencyVerdict.ChangedDegraded => work ? "more work" : availability ? "more provider errors" : "degraded",
            ConsistencyVerdict.ChangedImproved => work ? "less work" : availability ? "fewer provider errors" : "improved",
            ConsistencyVerdict.ChangedNegligible => "changed, negligible",
            ConsistencyVerdict.Equivalent => "equivalent",
            _ => "inconclusive"
        };

        return new ChatConsistencyAttributionEndpoint
        {
            Id = id,
            Name = Names[id],
            Computed = true,
            Verdict = verdict,
            VerdictLabel = label,
            Grade = verdict == ConsistencyVerdict.Inconclusive ? ChatConsistencyEvidenceGrade.NotEstablished : grade,
            Estimate = estimate
        };
    }

    /// <summary>Every endpoint of Protocol V2 Equivalent, no event, no control, the same served model in both periods.</summary>
    private static ChatConsistencyAttributionInput Quiet(params ChatConsistencyAttributionEndpoint[] changes)
    {
        var endpoints = ChatConsistencyProtocol.V2.Endpoints
            .Select(e => changes.FirstOrDefault(c => c.Id == e.Id) ?? Endpoint(e.Id, ConsistencyVerdict.Equivalent))
            .ToList();

        return new ChatConsistencyAttributionInput
        {
            Endpoints = endpoints,
            SubjectProvider = "OpenAI",
            ServedModels = new ChatConsistencyServedModels
            {
                Baseline = new[] { new ChatConsistencyServedModelCount("gpt-test-2026-09-01", 48) },
                Comparison = new[] { new ChatConsistencyServedModelCount("gpt-test-2026-09-01", 48) },
                Changed = false
            }
        };
    }

    private static ChatConsistencyControlEffect Control(
        string endpointId, bool didIncludesZero, bool movedSameWay, bool separates, bool sameProvider = false)
        => new()
        {
            EndpointId = endpointId,
            ControlSubjectKey = "CandidateProvider=" + (sameProvider ? "OpenAI" : "Google") + ";CandidateModelId=control",
            ControlDisplay = sameProvider ? "gpt-control" : "gemini-control",
            ControlProvider = sameProvider ? "OpenAI" : "Google",
            SameProvider = sameProvider,
            DidIncludesZero = didIncludesZero,
            ControlMovedSameWay = movedSameWay,
            DidSeparatesTarget = separates
        };

    private static ChatConsistencyAttributionResult Single(ChatConsistencyAttributionOutcome outcome, string rule)
        => Assert.Single(outcome.Attributions, a => a.Rule == rule);

    // --- Totals ---------------------------------------------------------------------------------

    [Fact]
    public void EveryEndpointsTotalChangeIsReportedFirstInInputOrder()
    {
        // Out of protocol order; Cost carries an Established verdict but is not computed.
        var endpoints = new[]
        {
            Endpoint(ChatConsistencyEndpointIds.Cost, ConsistencyVerdict.ChangedDegraded) with { Computed = false },
            Endpoint(ChatConsistencyEndpointIds.Work, ConsistencyVerdict.Equivalent),
            Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedDegraded),
            Endpoint(ChatConsistencyEndpointIds.ProviderErrorRate, ConsistencyVerdict.Equivalent),
            Endpoint(ChatConsistencyEndpointIds.StreamingRate, ConsistencyVerdict.Equivalent),
            Endpoint(ChatConsistencyEndpointIds.TimeToFirstAnswerText, ConsistencyVerdict.Equivalent)
        };
        Assert.NotEqual(ChatConsistencyEndpointIds.All, endpoints.Select(e => e.Id).ToList());

        var outcome = ChatConsistencyAttribution.Attribute(Quiet() with { Endpoints = endpoints });

        Assert.Equal(endpoints.Select(e => e.Id).ToList(), outcome.TotalChanges.Select(t => t.EndpointId).ToList());
        Assert.Equal("not computable", outcome.TotalChanges[0].VerdictLabel);
        Assert.Equal(ChatConsistencyEvidenceGrade.NotEstablished, outcome.TotalChanges[0].Grade);
        Assert.Equal("equivalent", outcome.TotalChanges[1].VerdictLabel);
        Assert.Equal("degraded", outcome.TotalChanges[2].VerdictLabel);
        Assert.Equal(ChatConsistencyEvidenceGrade.Established, outcome.TotalChanges[2].Grade);
    }

    // --- R1 / R2 --------------------------------------------------------------------------------

    [Fact]
    public void AChangeWithAnOverseerEventAndAControlMovingTheSameWayIsAnOverseerChange()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedDegraded)) with
        {
            Events = new[] { ToolGuidesEvent },
            ControlEffects = new[] { Control(ChatConsistencyEndpointIds.Quality, didIncludesZero: true, movedSameWay: true, separates: false) }
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        var row = Single(outcome, ChatConsistencyAttribution.RuleOverseerChange);
        Assert.Equal("Overseer change", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideOurs, row.Side);
        Assert.Contains(ToolGuidesEvent.Label, row.EventRefs);
        Assert.Contains("tool guides edited on 2026-10-03", row.Evidence);
        Assert.Equal(new[] { ChatConsistencyEndpointIds.Quality }, row.Endpoints);
        Assert.DoesNotContain(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleNotAttributable);
    }

    [Fact]
    public void AChangeWithAnOverseerEventAndNoControlIsNotAttributable()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedDegraded)) with
        {
            Events = new[] { ToolGuidesEvent },
            MissingControls = new[]
            {
                new ChatConsistencyMissingControlView
                {
                    Period = "comparison",
                    SuiteName = "Core Suite",
                    SuggestedText = "No control run for period comparison: make a run of a model from a provider other than OpenAI on suite Core Suite.",
                    TargetRunId = 14
                }
            }
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        var row = Single(outcome, ChatConsistencyAttribution.RuleNotAttributable);
        Assert.Equal("Total change, not attributable", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideUndetermined, row.Side);
        Assert.Equal(ChatConsistencyEvidenceGrade.NotEstablished, row.Grade);
        Assert.Contains(ToolGuidesEvent.Label, row.Evidence);
        Assert.Contains("OpenAI", row.Evidence);
        Assert.Contains("No control run for period comparison", row.Evidence);
        Assert.DoesNotContain(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleOverseerChange);
    }

    // --- R3 / R4 --------------------------------------------------------------------------------

    [Fact]
    public void ADifferentServedModelIdIsADeclaredSnapshotChange()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedImproved)) with
        {
            ServedModels = new ChatConsistencyServedModels
            {
                Baseline = new[] { new ChatConsistencyServedModelCount("gpt-test-2026-09-01", 48) },
                Comparison = new[] { new ChatConsistencyServedModelCount("gpt-test-2026-10-01", 48) },
                Changed = true
            }
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        var row = Single(outcome, ChatConsistencyAttribution.RuleDeclaredSnapshotChange);
        Assert.Equal("Declared snapshot change", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideProvider, row.Side);
        Assert.Contains("gpt-test-2026-10-01", row.Evidence);
        Assert.Contains(ChatConsistencyEndpointIds.Quality, row.Endpoints);

        var improvement = Single(outcome, ChatConsistencyAttribution.RuleImprovement);
        Assert.Equal("Improvement with a new snapshot", improvement.Label);
    }

    [Fact]
    public void CallsServedAtAnotherTierAreAServedConfigurationDifference()
    {
        var input = Quiet() with
        {
            ServedModels = new ChatConsistencyServedModels
            {
                Baseline = new[] { new ChatConsistencyServedModelCount("gpt-test-2026-09-01", 48) },
                Comparison = new[] { new ChatConsistencyServedModelCount("gpt-test-2026-09-01", 48) },
                BaselineCalls = 48,
                ComparisonCalls = 48,
                ComparisonTierMismatchCalls = 12,
                ServedConfigurationDiffers = true
            }
        };

        var row = Single(ChatConsistencyAttribution.Attribute(input), ChatConsistencyAttribution.RuleServedConfiguration);

        Assert.Equal("Served configuration differs from requested", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideProvider, row.Side);
        Assert.Contains("comparison 12 of 48", row.Evidence);
    }

    // --- R5 ----------------------------------------------------------------------------------

    [Fact]
    public void MoreWorkWithAnEquivalentStreamingRateAndNoEventIsAModelBehaviorChange()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.Work, ConsistencyVerdict.ChangedDegraded)) with
        {
            ModelCallsChanged = true
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        var row = Single(outcome, ChatConsistencyAttribution.RuleModelBehavior);
        Assert.Equal("Model behavior change", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideProvider, row.Side);
        Assert.Contains(ChatConsistencyEndpointIds.Work, row.Endpoints);
        Assert.Contains("Work per turn more work", row.Evidence);
        Assert.Contains("no Overseer event", row.Evidence);
    }

    [Fact]
    public void ABehaviorChangeWithAnUnseparatedOverseerEventIsNotAModelBehaviorChange()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.Work, ConsistencyVerdict.ChangedDegraded)) with
        {
            Events = new[] { ToolGuidesEvent }
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        Assert.DoesNotContain(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleModelBehavior);
        Assert.Contains(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleNotAttributable && a.Endpoints.Contains(ChatConsistencyEndpointIds.Work));
    }

    // --- R6 ----------------------------------------------------------------------------------

    [Fact]
    public void ASpeedChangeThatDiffersBetweenStrataIsLoadRelated()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.TimeToFirstAnswerText, ConsistencyVerdict.ChangedDegraded)) with
        {
            TimeToFirstAnswerTextStrataDiffer = true
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        var row = Single(outcome, ChatConsistencyAttribution.RuleLoadRelated);
        Assert.Equal("Load- or capacity-related (within the sampled hours)", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideProvider, row.Side);
        Assert.Equal(new[] { ChatConsistencyEndpointIds.TimeToFirstAnswerText }, row.Endpoints);
        Assert.Contains("differs between the sampled time strata", row.Evidence);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, row.Grade);
    }

    [Fact]
    public void ASpeedChangeWithRisingRateLimitResponsesIsLoadRelated()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.TimeToFirstAnswerText, ConsistencyVerdict.ChangedDegraded)) with
        {
            RateLimitOrServerErrorIncrease = true
        };

        var row = Single(ChatConsistencyAttribution.Attribute(input), ChatConsistencyAttribution.RuleLoadRelated);

        Assert.Contains("429 or 5xx", row.Evidence);
    }

    // --- R7 ----------------------------------------------------------------------------------

    [Fact]
    public void AStreamingChangeAtEveryDecileAcrossBusinessAndOtherHoursIsALoadIndependentPersistentServingChange()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.StreamingRate, ConsistencyVerdict.ChangedDegraded)) with
        {
            StreamingChangedAtEveryDecile = true,
            StreamingRateStrataSameSign = true,
            UsBusinessHoursCovered = true,
            OutsideBusinessHoursCovered = true
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        var row = Single(outcome, ChatConsistencyAttribution.RulePersistentServing);
        Assert.Equal("Persistent serving change within the sampled hours (model-specific; load-independent)", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideProvider, row.Side);
        Assert.DoesNotContain(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleLoadRelated);
    }

    [Fact]
    public void APersistentServingChangeOutsideBusinessHoursOnlyLeavesTimeOfDayDependenceNotAssessable()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.StreamingRate, ConsistencyVerdict.ChangedDegraded)) with
        {
            StreamingChangedAtEveryDecile = true,
            StreamingRateStrataSameSign = null,
            UsBusinessHoursCovered = false,
            OutsideBusinessHoursCovered = true,
            ControlEffects = new[] { Control(ChatConsistencyEndpointIds.StreamingRate, didIncludesZero: true, movedSameWay: true, separates: false, sameProvider: true) }
        };

        var row = Single(ChatConsistencyAttribution.Attribute(input), ChatConsistencyAttribution.RulePersistentServing);

        Assert.Equal("Persistent serving change within the sampled hours (provider-wide; time-of-day dependence not assessable)", row.Label);
        Assert.DoesNotContain("load-independent", row.Label);
    }

    // --- R8 ----------------------------------------------------------------------------------

    [Fact]
    public void AGrossSpeedChangeThatIsEquivalentNetOfOwnWaitsIsOurInfrastructure()
    {
        var input = Quiet() with
        {
            GrossTimeToFirstAnswerTextVerdict = ConsistencyVerdict.ChangedDegraded
        };

        var row = Single(ChatConsistencyAttribution.Attribute(input), ChatConsistencyAttribution.RuleInfrastructure);

        Assert.Equal("Our infrastructure", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideInfrastructure, row.Side);
        Assert.Contains("Overseer's own waits", row.Evidence);
    }

    [Fact]
    public void AMaterialOwnWaitShiftWithASpeedChangeIsOurInfrastructure()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.TimeToFirstAnswerText, ConsistencyVerdict.ChangedDegraded)) with
        {
            OwnWaitShareMovedMaterially = true
        };

        var row = Single(ChatConsistencyAttribution.Attribute(input), ChatConsistencyAttribution.RuleInfrastructure);

        Assert.Contains("permit and retry waits moved materially", row.Evidence);
    }

    // --- R9 / R10 --------------------------------------------------------------------------------

    [Fact]
    public void AQualityDropOfTheTargetAloneWithAnUnchangedServedModelIsAnUndeclaredModelChange()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedDegraded)) with
        {
            ControlEffects = new[] { Control(ChatConsistencyEndpointIds.Quality, didIncludesZero: false, movedSameWay: false, separates: true) }
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        var row = Single(outcome, ChatConsistencyAttribution.RuleUndeclaredChange);
        Assert.Equal("Undeclared model change", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideProvider, row.Side);
        Assert.StartsWith("Flagged for review", row.Evidence);
        Assert.Contains("gpt-test-2026-09-01", row.Evidence);
        Assert.DoesNotContain(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleUndetermined);
    }

    [Fact]
    public void AQualityDropWithoutASeparatingControlIsNotAnUndeclaredModelChange()
    {
        var outcome = ChatConsistencyAttribution.Attribute(Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedDegraded)));

        Assert.DoesNotContain(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleUndeclaredChange);
        var row = Single(outcome, ChatConsistencyAttribution.RuleUndetermined);
        Assert.Contains(ChatConsistencyEndpointIds.Quality, row.Endpoints);
    }

    [Fact]
    public void AnEstablishedImprovementWithoutASnapshotChangeIsAnUndeclaredImprovement()
    {
        var row = Single(
            ChatConsistencyAttribution.Attribute(Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedImproved))),
            ChatConsistencyAttribution.RuleImprovement);

        Assert.Equal("Undeclared improvement", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideProvider, row.Side);
    }

    // --- R12 ---------------------------------------------------------------------------------

    [Fact]
    public void AnIsolatedProviderErrorRateChangeIsAProviderAvailabilityChange_AndNotUndetermined()
    {
        var outcome = ChatConsistencyAttribution.Attribute(
            Quiet(Endpoint(ChatConsistencyEndpointIds.ProviderErrorRate, ConsistencyVerdict.ChangedDegraded, ChatConsistencyEvidenceGrade.Indicated)));

        var row = Single(outcome, ChatConsistencyAttribution.RuleProviderAvailability);
        Assert.Equal("Provider availability changed (within the sampled hours)", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideProvider, row.Side);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, row.Grade);
        Assert.Equal(new[] { ChatConsistencyEndpointIds.ProviderErrorRate }, row.Endpoints);
        Assert.StartsWith("More provider errors observed in GnollBench runs", row.Evidence);
        Assert.DoesNotContain(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleUndetermined);
    }

    [Fact]
    public void AProviderErrorRateChange_WithAnEventAndNoSeparatingControl_OrAcrossAnAccountingChange_IsNoAvailabilityChange()
    {
        var p6 = Endpoint(ChatConsistencyEndpointIds.ProviderErrorRate, ConsistencyVerdict.ChangedDegraded);

        var withEvent = ChatConsistencyAttribution.Attribute(Quiet(p6) with { Events = new[] { ToolGuidesEvent } });
        var accounting = ChatConsistencyAttribution.Attribute(Quiet(p6) with { ProviderErrorAccountingChangedInSpan = true });
        var separated = ChatConsistencyAttribution.Attribute(Quiet(p6) with
        {
            Events = new[] { ToolGuidesEvent },
            ControlEffects = new[] { Control(ChatConsistencyEndpointIds.ProviderErrorRate, false, false, true) }
        });

        Assert.DoesNotContain(withEvent.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleProviderAvailability);
        var notAttributable = Single(withEvent, ChatConsistencyAttribution.RuleNotAttributable);
        Assert.StartsWith("Provider error rate: more provider errors observed in GnollBench runs.", notAttributable.Evidence);
        Assert.DoesNotContain(accounting.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleProviderAvailability);
        Assert.Contains(accounting.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleUndetermined);
        Single(separated, ChatConsistencyAttribution.RuleProviderAvailability);
    }

    [Fact]
    public void UnderV2_TheLoadRow_ReadsTheAttributedProviderRates()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.TimeToFirstAnswerText, ConsistencyVerdict.ChangedDegraded)) with
        {
            RateLimitOrServerErrorIncrease = true,
            ProviderErrorRatesAttributed = true
        };

        var row = Single(ChatConsistencyAttribution.Attribute(input), ChatConsistencyAttribution.RuleLoadRelated);

        Assert.Contains("rate of provider errors observed in GnollBench attempts rose", row.Evidence);
        Assert.DoesNotContain("429", row.Evidence);
    }

    // --- R11 ---------------------------------------------------------------------------------

    [Fact]
    public void EveryEndpointInconclusiveIsUndetermined()
    {
        var input = Quiet(ChatConsistencyEndpointIds.All.Select(id => Endpoint(id, ConsistencyVerdict.Inconclusive)).ToArray());

        var outcome = ChatConsistencyAttribution.Attribute(input);

        var row = Assert.Single(outcome.Attributions);
        Assert.Equal(ChatConsistencyAttribution.RuleUndetermined, row.Rule);
        Assert.Equal("Undetermined", row.Label);
        Assert.Equal(ChatConsistencyAttribution.SideUndetermined, row.Side);
        Assert.Equal(ChatConsistencyEvidenceGrade.NotEstablished, row.Grade);
    }

    [Fact]
    public void RowsOfSeveralSidesFittingOneEndpointAreMultipleCauses()
    {
        var input = Quiet(Endpoint(ChatConsistencyEndpointIds.TimeToFirstAnswerText, ConsistencyVerdict.ChangedDegraded)) with
        {
            TimeToFirstAnswerTextStrataDiffer = true,
            OwnWaitShareMovedMaterially = true
        };

        var outcome = ChatConsistencyAttribution.Attribute(input);

        Assert.Contains(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleLoadRelated);
        Assert.Contains(outcome.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleInfrastructure);
        var row = Single(outcome, ChatConsistencyAttribution.RuleUndetermined);
        Assert.Equal("Multiple causes", row.Label);
        Assert.Equal(new[] { ChatConsistencyEndpointIds.TimeToFirstAnswerText }, row.Endpoints);
    }

    // --- Vocabulary ----------------------------------------------------------------------------

    [Fact]
    public void NoLabelOrEvidenceNamesAnIntentOrAMechanism()
    {
        var served = new ChatConsistencyServedModels
        {
            Baseline = new[] { new ChatConsistencyServedModelCount("gpt-test-2026-09-01", 48) },
            Comparison = new[] { new ChatConsistencyServedModelCount("gpt-test-2026-10-01", 48) },
            Changed = true,
            BaselineCalls = 48,
            ComparisonCalls = 48,
            ComparisonTierMismatchCalls = 3,
            ComparisonFallbackCalls = 1,
            ComparisonServedSpeeds = new[] { "fast" },
            ServedConfigurationDiffers = true
        };

        var scenarios = new List<ChatConsistencyAttributionInput>
        {
            Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedDegraded)) with
            {
                Events = new[] { ToolGuidesEvent },
                ControlEffects = new[] { Control(ChatConsistencyEndpointIds.Quality, true, true, false) }
            },
            Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedDegraded)) with { Events = new[] { ToolGuidesEvent } },
            Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedImproved)) with { ServedModels = served },
            Quiet(Endpoint(ChatConsistencyEndpointIds.Work, ConsistencyVerdict.ChangedDegraded)) with { ReasoningTokensChanged = true, ToolMixChanged = true },
            Quiet(Endpoint(ChatConsistencyEndpointIds.TimeToFirstAnswerText, ConsistencyVerdict.ChangedDegraded)) with
            {
                TimeToFirstAnswerTextStrataDiffer = true,
                TimeToFirstAnswerTextHighRunVariance = true,
                RateLimitOrServerErrorIncrease = true,
                OwnWaitShareMovedMaterially = true,
                RetriesAccountForSpeedChange = true
            },
            Quiet(Endpoint(ChatConsistencyEndpointIds.StreamingRate, ConsistencyVerdict.ChangedDegraded)) with
            {
                StreamingChangedAtEveryDecile = true,
                StreamingRateStrataSameSign = true,
                UsBusinessHoursCovered = true,
                OutsideBusinessHoursCovered = true
            },
            Quiet() with { GrossTimeToFirstAnswerTextVerdict = ConsistencyVerdict.ChangedDegraded },
            Quiet(Endpoint(ChatConsistencyEndpointIds.Quality, ConsistencyVerdict.ChangedDegraded)) with
            {
                ControlEffects = new[] { Control(ChatConsistencyEndpointIds.Quality, false, false, true) }
            },
            Quiet(ChatConsistencyEndpointIds.All.Select(id => Endpoint(id, ConsistencyVerdict.Inconclusive)).ToArray()),
            Quiet(Endpoint(ChatConsistencyEndpointIds.Cost, ConsistencyVerdict.ChangedDegraded)),
            Quiet(Endpoint(ChatConsistencyEndpointIds.ProviderErrorRate, ConsistencyVerdict.ChangedDegraded)),
            Quiet(Endpoint(ChatConsistencyEndpointIds.ProviderErrorRate, ConsistencyVerdict.ChangedImproved)) with { Events = new[] { ToolGuidesEvent } }
        };

        var texts = scenarios
            .SelectMany(s => ChatConsistencyAttribution.Attribute(s).Attributions)
            .SelectMany(a => new[] { a.Label, a.Evidence })
            .ToList();

        Assert.NotEmpty(texts);
        foreach (var text in texts)
        {
            foreach (var word in BenchmarkReportPackValidator.ChatIntentWords.Concat(BenchmarkReportPackValidator.ChatMechanismWords))
            {
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"\"{word}\" appears in: {text}");
            }
        }
    }
}
