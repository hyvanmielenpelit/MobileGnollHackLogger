namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Fixtures shared by the report-pack tests: one fixed stored document for the renderer and golden
/// tests, and small run and comparison builders for the fact-sheet and content tests.
///
/// <para>The document is built by hand, not by <see cref="BenchmarkReportFacts"/>, so the golden
/// files depend on the renderer alone. Changing any value here changes the golden files.</para>
/// </summary>
internal static class BenchmarkReportPackFixture
{
    public static readonly DateTime CreatedAt = new(2026, 9, 28, 10, 42, 0, DateTimeKind.Utc);

    public const string PurposeStatement =
        "Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.";

    /// <summary>Set to <c>1</c> to write every golden file from the renderer's output instead of comparing against it.</summary>
    public const string UpdateGoldensVariable = "OVERSEER_UPDATE_GOLDENS";

    // ---------------------------------------------------------------------------------------------
    // The stored document
    // ---------------------------------------------------------------------------------------------

    public static BenchmarkReportDocument Document(BenchmarkReportAudience audience)
        => Document(audience, Sheet(), Writer(), Notes(), BenchmarkReportDocumentStatus.CompletedWithWarnings,
            BenchmarkReportDocumentOrigin.ReportPack, "{\"runIds\":[12,13,14],\"groupIds\":[],\"pricingBasis\":1}");

    /// <summary>A run-completion document: the same run with no peers, stored without warnings.</summary>
    public static BenchmarkReportDocument StandaloneDocument(BenchmarkReportAudience audience)
        => Document(audience, StandaloneSheet(), StandaloneWriter(), new List<BenchmarkReportValidationNote>(),
            BenchmarkReportDocumentStatus.Completed, BenchmarkReportDocumentOrigin.RunCompletion,
            "{\"runIds\":[12],\"groupIds\":[],\"pricingBasis\":1}");

    private static BenchmarkReportDocument Document(
        BenchmarkReportAudience audience,
        BenchmarkReportFactSheet sheet,
        BenchmarkReportWriterOutput writer,
        List<BenchmarkReportValidationNote> notes,
        BenchmarkReportDocumentStatus status,
        BenchmarkReportDocumentOrigin origin,
        string comparisonRequestJson)
    {
        return new BenchmarkReportDocument
        {
            Id = 101,
            PackId = new Guid("3f2b8c1e-0000-4000-8000-000000000101"),
            Audience = audience,
            Origin = origin,
            SubjectKey = "run:12",
            SubjectLabel = sheet.SubjectLabel,
            SubjectRunIdsJson = "[12]",
            ComparisonRequestJson = comparisonRequestJson,
            SuiteId = 5,
            SuiteName = "GnollHack Core Suite",
            WriterConfigId = 7,
            WriterDisplayName = "Claude Opus 5.5",
            WriterProvider = "Anthropic",
            WriterModelId = "claude-opus-5-5",
            WriterThinkingLevel = "high",
            SameProviderAcknowledged = false,
            ReportFormatVersion = BenchmarkReportPackRenderer.ReportFormatVersion,
            WriterPromptSha256 = new string('a', 64),
            AnswerExcerptChars = 120,
            FactsJson = BenchmarkReportJson.Serialize(sheet),
            ContentJson = BenchmarkReportJson.Serialize(Content()),
            WriterOutputJson = BenchmarkReportJson.Serialize(writer),
            ValidationNotesJson = BenchmarkReportJson.Serialize(notes),
            Title = BenchmarkReportPackRenderer.BuildTitle(audience, sheet),
            Status = status,
            CreatedAtUtc = CreatedAt,
            CreatedByUserId = "user-1",
            InputTokens = 12000,
            OutputTokens = 3000,
            DurationMs = 45000,
            CostUsd = 0.12m,
            PricingSource = "catalog",
            Runs = new List<BenchmarkReportDocumentRun>
            {
                new()
                {
                    RunId = 12,
                    FinalScore = 80,
                    QualityIndex = 80,
                    SpeedIndex = 90,
                    ScoringMethodVersion = 12,
                    SynthesisSha256 = "0123456789abcdef"
                }
            }
        };
    }

    public static BenchmarkReportFactSheet Sheet() => new()
    {
        SubjectKey = "run:12",
        SubjectKind = "Run",
        SubjectLabel = "GPT-5.6 Luna",
        SubjectDisplayName = "GPT-5.6 Luna",
        SubjectProvider = "OpenAI",
        SubjectModelId = "gpt-5.6-luna",
        SubjectThinkingLevel = "high",
        SubjectRunIds = new List<long> { 12 },
        SubjectState = "Comparable",
        SubjectExplanation = "Comparable: measured under the baseline condition.",
        SuiteId = 5,
        SuiteName = "GnollHack Core Suite",
        Peers = new List<BenchmarkReportPeer>
        {
            new()
            {
                Letter = "A",
                EntryKey = "run:14",
                Label = "Grok 5",
                DisplayName = "Grok 5",
                Provider = "xAI",
                ModelId = "grok-5",
                ThinkingLevel = "high",
                RunIds = new List<long> { 14 },
                State = "Comparable",
                Explanation = "Comparable: measured under the baseline condition."
            },
            new()
            {
                Letter = "B",
                EntryKey = "run:13",
                Label = "Mistral Large 4",
                DisplayName = "Mistral Large 4",
                Provider = "Mistral",
                ModelId = "mistral-large-4",
                RunIds = new List<long> { 13 },
                State = "Degraded",
                SpeedDegraded = true,
                Explanation = "Degraded: speed was measured with parallel execution disabled."
            }
        },
        Graders = new List<BenchmarkReportGrader>
        {
            new() { Role = "Panel member A", Label = "Gemini 3.8 Flash", Provider = "Google", ModelId = "gemini-3.8-flash", ThinkingLevel = "medium" },
            new() { Role = "Panel member B", Label = "Claude Haiku 5", Provider = "Anthropic", ModelId = "claude-haiku-5" },
            new() { Role = "Claim verifier", Label = "Gemini 3.8 Flash", Provider = "Google", ModelId = "gemini-3.8-flash", ThinkingLevel = "medium" }
        },
        Facts = Facts(),
        Questions = new List<BenchmarkReportQuestion>
        {
            Question(1, "101", 2, "Simple", 90, 85, false, 0, 2, 8100),
            Question(2, "102", 1, "Intermediate", 72, 70, false, 0, 3, 11000),
            Question(3, "103", 1, "Intermediate", 25, 68, true, 1, 5, 15200),
            Question(4, "104", 3, "Advanced", 87, 91, false, 0, 4, 13400)
        },
        Rows = new List<BenchmarkReportFindingRow>
        {
            new()
            {
                Id = "R1", Kind = "weakness", Category = "critical_error", Questions = new List<int> { 3 },
                Status = "Convergent", SupportLabel = BenchmarkReportFacts.SupportBothGraders,
                MemberAText = "States that a thrown gem always shatters.", MemberBText = "Claims the gem is always destroyed."
            },
            new()
            {
                Id = "R2", Kind = "strength", Category = "accuracy", Questions = new List<int> { 1 },
                Status = "MemberAOnly", SupportLabel = BenchmarkReportFacts.SupportOneGraderDifferentFamily,
                MemberAText = "Precise on the unicorn throwing rules."
            },
            new()
            {
                Id = "R3", Kind = "strength", Category = "conciseness", Questions = new List<int> { 2 },
                Status = "Conflicting", SupportLabel = BenchmarkReportFacts.SupportGradersDisagree,
                MemberAText = "Admirably brief.", MemberBText = "Too terse to be useful."
            }
        },
        KnownNames = new List<string>
        {
            "Anthropic", "Claude Haiku 5", "GPT-5.6 Luna", "Gemini 3.8 Flash", "GnollHack Core Suite", "Google", "Grok 5",
            "Mistral", "Mistral Large 4", "OpenAI", "claude-haiku-5", "gemini-3.8-flash", "gpt-5.6-luna", "grok-5",
            "mistral-large-4", "xAI"
        },
        NoSignificanceSummary = "Testing every pair among these 3 models at once would flag chance differences as significant, so this view tests none.",
        NoSignificanceInstead = "Put each model's runs in an analysis group, open one in the Multi-Run Analysis tab and choose the other under Compare with group.",
        PurposeStatements = new List<string> { PurposeStatement },
        Entries = new List<BenchmarkReportEntryFigures>
        {
            new()
            {
                EntryKey = "run:12", IsSubject = true, QualityIndex = 80.4, QualityLower = 77.1, QualityUpper = 83.2, QualityRank = 2,
                ModelTimeP50Ms = 12300, SpeedRank = 2, CostPerQuestionUsd = 0.036, CostRank = 2, RunCount = 1
            },
            new()
            {
                EntryKey = "run:14", PeerLetter = "A", QualityIndex = 85.2, QualityLower = 81.0, QualityUpper = 89.4, QualityRank = 1,
                ModelTimeP50Ms = 9800, SpeedRank = 1, CostPerQuestionUsd = 0.052, CostRank = 3, RunCount = 1
            },
            new()
            {
                EntryKey = "run:13", PeerLetter = "B", QualityIndex = 78.3, QualityLower = 74.1, QualityUpper = 82.5, QualityRank = 3,
                SpeedDegraded = true, CostPerQuestionUsd = 0.021, CostRank = 1, RunCount = 1
            }
        }
    };

    private static List<BenchmarkReportFact> Facts()
    {
        var facts = new List<BenchmarkReportFact>
        {
            Fact("band.advanced.difference", "-4"),
            Fact("band.advanced.peerMean", "91"),
            Fact("band.advanced.questions", "1"),
            Fact("band.advanced.score", "87"),
            Fact("band.intermediate.difference", "-21"),
            Fact("band.intermediate.peerMean", "69"),
            Fact("band.intermediate.questions", "2"),
            Fact("band.intermediate.score", "49"),
            Fact("band.simple.difference", "+5"),
            Fact("band.simple.peerMean", "85"),
            Fact("band.simple.questions", "1"),
            Fact("band.simple.score", "90"),
            Fact("comparison.pricingBasis", "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent."),
            Fact("comparison.signature", "sig-7f3a91"),
            Fact("config.chat", "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed"),
            Fact("cost.perQuestion", "$0.036"),
            Fact("cost.pricingAsOf", "2026-09-01"),
            Fact("cost.rank", "2nd of 3"),
            Fact("dimension.accuracy", "84"),
            Fact("dimension.accuracy.difference", "+2"),
            Fact("dimension.accuracy.peerMean", "82"),
            Fact("dimension.completeness", "70"),
            Fact("dimension.completeness.difference", "-8"),
            Fact("dimension.completeness.peerMean", "78"),
            Fact("dimension.conciseness", "88"),
            Fact("dimension.conciseness.difference", "+3"),
            Fact("dimension.conciseness.peerMean", "85"),
            Fact("dimension.readability", "90"),
            Fact("dimension.readability.difference", "+1"),
            Fact("dimension.readability.peerMean", "89"),
            Fact("errors.critical", "1 of 4 answers", JsonValue.Create(1)),
            Fact("panel.disagreements", "1 of 4 answers"),
            Fact("panel.icc", "0.82"),
            new BenchmarkReportFact
            {
                Key = "panel.judgeDependentPairs",
                Display = BenchmarkReportFacts.NotAvailable,
                Available = false,
                UnavailableReason = "The compared runs were not all graded by the same panel."
            },
            Fact("panel.meanAbsDelta", "6.5 points"),
            Fact("panel.memberAAlone", "81 / 100"),
            Fact("panel.memberBAlone", "79 / 100"),
            Fact("quality.index", "80 ± 3 / 100", JsonValue.Create(80.4)),
            Fact("quality.interval", "77–83"),
            Fact("quality.intervalBasis", "Item sampling only. Below 3 runs there is no reproducibility estimate, so this interval covers one source of variation rather than two."),
            Fact("quality.intervalOverlap", "its 95 % interval overlaps those of Models A and B"),
            Fact("quality.rank", "2nd of 3"),
            Fact("run.dates", "2026-09-20"),
            Fact("run.harnessVersion", "41"),
            Fact("run.ids", "12"),
            Fact("run.promptSha256", "e9b3e9a7c4d1"),
            Fact("run.toolGuidesSha256", "f59d8b30a1c7"),
            Fact("scoring.criticalErrorCap", "25"),
            Fact("scoring.levels", "1, 15, 35, 55, 72, 87, 100"),
            Fact("scoring.methodVersion", "12"),
            Fact("scoring.weights", "Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %"),
            Fact("speed.modelTimeP50", "12.3 s"),
            Fact("speed.rank", "2nd of 2"),
            Fact("style.responseStyleConflict", "no"),
            Fact("suite.questions", "4"),
            Fact("tools.callsPerQuestion", "3.5"),
            Fact("tools.callsPerQuestion.peerMean", "2.8"),
            Fact("tools.failed", "0"),
            Fact("tools.refusedByBudget", "0"),
            Fact("tools.share.knowledgeBase", "0 %"),
            Fact("tools.share.other", "0 %"),
            Fact("tools.share.sourceCode", "57 %"),
            Fact("tools.share.structuredLookup", "14 %"),
            Fact("tools.share.wiki", "29 %"),
            Fact("tools.zeroKnowledgeBaseAnswers", "4 of 4")
        };

        return facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
    }

    public static BenchmarkReportContentSnapshot Content() => new()
    {
        AnswerExcerptChars = 120,
        Runs = new List<BenchmarkReportContentRun>
        {
            new()
            {
                RunId = 12,
                Questions = new List<BenchmarkReportContentQuestion>
                {
                    new()
                    {
                        Number = 1, QuestionKey = "101", ItemRevisionUsed = 2, OrderIndex = 1, Band = "Simple",
                        QuestionText = "What happens if I throw a gem at a co-aligned unicorn?",
                        ExpectedPoints = "- The unicorn catches the gem.\n- A valuable gem raises Luck; worthless glass does not.",
                        ExpectedPointsRecorded = true,
                        AnswerExcerpt = "The unicorn catches it. A real gem of your alignment raises your Luck; glass does nothing.",
                        Graders = new List<BenchmarkReportContentGrader>
                        {
                            new() { Role = "Panel member A", Label = "Gemini 3.8 Flash", Score = 92, Comment = "Accurate and brief.", Evidence = new List<string> { "Accuracy: Matches rubric." } },
                            new() { Role = "Panel member B", Label = "Claude Haiku 5", Score = 88, Comment = "Correct." }
                        }
                    },
                    new()
                    {
                        Number = 2, QuestionKey = "102", ItemRevisionUsed = 1, OrderIndex = 2, Band = "Intermediate",
                        QuestionText = "How long is the prayer timeout after a successful prayer?",
                        ExpectedPoints = "- The timeout is reset to a random value around 350.",
                        ExpectedPointsRecorded = true,
                        AnswerExcerpt = "About 50 to 1000 turns, typically near 350.",
                        Graders = new List<BenchmarkReportContentGrader>
                        {
                            new() { Role = "Panel member A", Label = "Gemini 3.8 Flash", Score = 75, Comment = "Terse.", Evidence = new List<string> { "Completeness: Omits the Luck adjustment." } },
                            new() { Role = "Panel member B", Label = "Claude Haiku 5", Score = 69 }
                        },
                        ClaimRulings = new List<BenchmarkReportContentClaimRuling>
                        {
                            new() { Claim = "The timeout is typically near 350.", Verdict = "supported", Rationale = "Matches the prayer code (pray.c)." }
                        }
                    },
                    new()
                    {
                        Number = 3, QuestionKey = "103", ItemRevisionUsed = 1, OrderIndex = 3, Band = "Intermediate",
                        QuestionText = "Will my gem break if I throw it at a unicorn?",
                        ExpectedPoints = null,
                        ExpectedPointsRecorded = false,
                        AnswerExcerpt = "Yes. A thrown gem always shatters on impact, so never throw your valuable gems at a unicorn; keep them for…",
                        AnswerExcerptCut = true,
                        Graders = new List<BenchmarkReportContentGrader>
                        {
                            new()
                            {
                                Role = "Panel member A", Label = "Gemini 3.8 Flash", Score = 25, Comment = "Critical error.",
                                Evidence = new List<string> { "Accuracy: The gem does not always shatter.", "Critical error: \"A thrown gem always shatters on impact\"" }
                            },
                            new() { Role = "Panel member B", Label = "Claude Haiku 5", Score = 25, Comment = "Fabricated breakage." }
                        },
                        ClaimRulings = new List<BenchmarkReportContentClaimRuling>
                        {
                            new() { Claim = "A thrown gem always shatters on impact.", Verdict = "refuted", Rationale = "Gems are caught, not broken (dothrow.c)." }
                        }
                    },
                    new()
                    {
                        Number = 4, QuestionKey = "104", ItemRevisionUsed = 3, OrderIndex = 4, Band = "Advanced",
                        QuestionText = "How many wishes can I get from a wand of wishing?",
                        ExpectedPoints = "- 1 to 3 charges.\n- Wresting gives one more.",
                        ExpectedPointsRecorded = true,
                        AnswerExcerpt = "A new wand has 1 to 3 charges, and you can wrest one more.",
                        Graders = new List<BenchmarkReportContentGrader>
                        {
                            new() { Role = "Panel member A", Label = "Gemini 3.8 Flash", Score = 88, Comment = "Good." },
                            new() { Role = "Panel member B", Label = "Claude Haiku 5", Score = 86 }
                        }
                    }
                }
            }
        }
    };

    public static BenchmarkReportWriterOutput Writer() => new()
    {
        Headline = "{{subject}} answers everyday GnollHack questions well but made one confident false claim about item destruction.",
        Sections = new Dictionary<string, string>
        {
            [BenchmarkReportSlots.Meaning] = "{{subject}} is a capable assistant for everyday play, but its answers on item destruction need a source check.",
            [BenchmarkReportSlots.Confidence] = "The result rests on {{suite.questions}} questions; {{quality.intervalOverlap}}.",
            [BenchmarkReportSlots.Abstract] = "{{subject}} scored {{quality.index}} on {{suite.questions}} questions, ranking {{quality.rank}} against {{peer:A}} and {{peer:B}}.",
            [BenchmarkReportSlots.WhyItScored] = "One critical error on Q3 capped that answer at {{scoring.criticalErrorCap}}.\n\nCompleteness was {{dimension.completeness}} against a peer mean of {{dimension.completeness.peerMean}}.",
            [BenchmarkReportSlots.WhatWorked] = "Short, accurate answers on simple questions (R2).",
            [BenchmarkReportSlots.OverseerChat] = "Most tool calls went to the source code ({{tools.share.sourceCode}}).",
            [BenchmarkReportSlots.BenchmarkSystem] = "Both panel members flagged the Q3 error (R1).",
            [BenchmarkReportSlots.ModelResult] = "{{subject}} ranks {{quality.rank}} at {{cost.perQuestion}} per question."
        },
        Strengths = new List<BenchmarkReportWriterItem>
        {
            new() { Text = "Answers simple questions precisely and briefly.", Questions = new List<int> { 1 }, Evidence = new List<string> { "R2" } }
        },
        Weaknesses = new List<BenchmarkReportWriterItem>
        {
            new() { Text = "Asserted a false outcome on Q3, where {{peer:A}} scored well.", Questions = new List<int> { 3 }, Evidence = new List<string> { "R1", "Q3" } },
            new() { Text = "Scored lowest on intermediate questions ({{band.intermediate.score}}).", Questions = new List<int> { 2, 3 }, Evidence = new List<string> { "band.intermediate.score" } }
        },
        Recommendations = new List<BenchmarkReportWriterRecommendation>
        {
            new() { For = BenchmarkReportSlots.TargetModelDevelopers, Text = "Verify object-destruction rules before asserting them.", Evidence = new List<string> { "R1" } },
            new() { For = BenchmarkReportSlots.TargetOverseerChat, Text = "Send item-destruction questions to the source code first.", Evidence = new List<string> { "tools.share.sourceCode" } },
            new() { For = BenchmarkReportSlots.TargetBenchmark, Text = "Clarify the conciseness anchor that split the graders on Q2.", Evidence = new List<string> { "R3" } }
        },
        QuestionTopics = new List<BenchmarkReportQuestionTopic>
        {
            new() { Question = 1, Topic = "Throwing gems at unicorns" },
            new() { Question = 2, Topic = "Prayer timeout" },
            new() { Question = 3, Topic = "Breaking a thrown gem" },
            new() { Question = 4, Topic = "Wand of wishing charges" }
        },
        QuestionNotes = new List<BenchmarkReportQuestionNote>
        {
            new() { Question = 3, Note = "Claimed a thrown gem always shatters; the rubric says it can survive." },
            new() { Question = 4, Note = "Slightly below the peers." }
        },
        Leads = new List<BenchmarkReportLead>
        {
            new() { Triage = "suite", Text = "The Q3 rubric may understate how often a thrown gem survives.", Evidence = new List<string> { "R1" } }
        }
    };

    /// <summary>
    /// <see cref="Sheet"/> as a stand-alone run report builds it: no peers, every peer fact unavailable
    /// with the stand-alone reason, no peer figures per question and the subject's figures alone.
    /// </summary>
    public static BenchmarkReportFactSheet StandaloneSheet()
    {
        var sheet = Sheet();
        sheet.Peers.Clear();
        sheet.NoSignificanceSummary = string.Empty;
        sheet.NoSignificanceInstead = string.Empty;
        sheet.Facts = sheet.Facts
            .Select(f => BenchmarkReportFacts.IsPeerFact(f.Key)
                ? new BenchmarkReportFact
                {
                    Key = f.Key,
                    Display = BenchmarkReportFacts.NotAvailable,
                    Available = false,
                    UnavailableReason = BenchmarkReportFacts.StandaloneReason
                }
                : f)
            .ToList();
        foreach (var q in sheet.Questions)
        {
            q.PeerMean = null;
            q.Difference = null;
            q.PeerCount = 0;
        }
        sheet.Entries = sheet.Entries.Where(e => e.IsSubject).ToList();
        sheet.KnownNames = sheet.KnownNames
            .Where(n => n is not ("Grok 5" or "grok-5" or "xAI" or "Mistral" or "Mistral Large 4" or "mistral-large-4"))
            .ToList();
        return sheet;
    }

    /// <summary>A writer output for <see cref="StandaloneSheet"/>: no peer token and no comparison.</summary>
    public static BenchmarkReportWriterOutput StandaloneWriter()
    {
        var writer = Writer();
        writer.Headline = "{{subject}} answers everyday GnollHack questions well but made one confident false claim about item destruction.";
        writer.Sections[BenchmarkReportSlots.Confidence] = "The result rests on {{suite.questions}} questions, so it should be read with care.";
        writer.Sections[BenchmarkReportSlots.Abstract] = "{{subject}} scored {{quality.index}} on {{suite.questions}} questions, with one critical error on Q3.";
        writer.Sections[BenchmarkReportSlots.WhyItScored] = "One critical error on Q3 capped that answer at {{scoring.criticalErrorCap}}.\n\nCompleteness was its lowest dimension at {{dimension.completeness}}.";
        writer.Weaknesses[0] = new BenchmarkReportWriterItem
        {
            Text = "Asserted a false outcome on Q3.",
            Questions = new List<int> { 3 },
            Evidence = new List<string> { "R1", "Q3" }
        };
        writer.Recommendations = new List<BenchmarkReportWriterRecommendation>
        {
            new()
            {
                For = BenchmarkReportSlots.TargetModelDevelopers,
                Text = "Asserting a destruction rule without checking it cost Q3; verify object-destruction rules before stating them.",
                Questions = new List<int> { 3 },
                Evidence = new List<string> { "R1", "Q3" }
            }
        };
        writer.QuestionNotes = new List<BenchmarkReportQuestionNote>
        {
            new() { Question = 3, Note = "Claimed a thrown gem always shatters; the rubric says it can survive." }
        };
        writer.Leads.Clear();
        return writer;
    }

    public static List<BenchmarkReportValidationNote> Notes() => new()
    {
        new() { Rule = 4, Location = "weaknesses[2]", Message = "Cited R9, which does not exist.", Dropped = true },
        new() { Rule = 2, Location = "sections.abstract", Message = "Abstract exceeded 150 words; trimmed by the writer on repair.", Dropped = false }
    };

    private static BenchmarkReportFact Fact(string key, string display, JsonNode? value = null)
        => new() { Key = key, Display = display, Value = value };

    private static BenchmarkReportQuestion Question(
        int number, string key, int revision, string band, double score, double peerMean, bool critical, int refuted, double tools, double modelTimeMs)
        => new()
        {
            Number = number,
            QuestionKey = key,
            ItemRevisionUsed = revision,
            OrderIndex = number,
            Band = band,
            Score = score,
            PeerMean = peerMean,
            Difference = score - peerMean,
            PeerCount = 2,
            CriticalError = critical,
            RefutedClaims = refuted,
            ToolCalls = tools,
            ModelTimeMs = modelTimeMs,
            RunCount = 1
        };

    /// <summary>Every audience, disclosure and naming the renderer accepts, with its golden file name.</summary>
    public static IEnumerable<object[]> AllowedCombinations()
    {
        var audiences = new[]
        {
            (BenchmarkReportAudience.ExecutiveSummary, "exec"),
            (BenchmarkReportAudience.TechnicalReport, "technical"),
            (BenchmarkReportAudience.InternalBrief, "internal")
        };

        foreach (var (audience, prefix) in audiences)
        {
            foreach (var disclosure in BenchmarkReportPackRenderer.AllowedDisclosures(audience))
            {
                foreach (var naming in new[] { BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized })
                {
                    string file = prefix + "_" + disclosure.ToString().ToLowerInvariant() + "_" + naming.ToString().ToLowerInvariant() + ".md";
                    yield return new object[] { audience, disclosure, naming, file };
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Golden files
    // ---------------------------------------------------------------------------------------------

    /// <summary>True when <see cref="UpdateGoldensVariable"/> is <c>1</c>: golden tests write their files instead of comparing.</summary>
    public static bool UpdateGoldens
        => string.Equals(Environment.GetEnvironmentVariable(UpdateGoldensVariable), "1", StringComparison.Ordinal);

    /// <summary>Writes a golden file in the repository: UTF-8 without a BOM, the renderer's <c>\n</c> line breaks kept.</summary>
    public static void WriteGolden(string fileName, string text)
    {
        File.WriteAllBytes(GoldenPath(fileName), new System.Text.UTF8Encoding(false).GetBytes(text));
    }

    /// <summary>The expected output, read from Overseer.Tests/UnitTests/Golden/ReportPack/ in the repository.</summary>
    public static string ReadGolden(string fileName)
    {
        string path = GoldenPath(fileName);
        Assert.True(File.Exists(path), "Golden file not found: " + path);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(Array.IndexOf(bytes, (byte)'\r') >= 0,"golden file has CRLF line endings; see .gitattributes (" + fileName + ")");

        return new System.Text.UTF8Encoding(false).GetString(bytes);
    }

    private static string GoldenPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MobileGnollHackLogger.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir != null, "Repository root (MobileGnollHackLogger.slnx) not found above " + AppContext.BaseDirectory);

        return Path.Combine(dir!.FullName, "Overseer.Tests", "UnitTests", "Golden", "ReportPack", fileName);
    }

    // ---------------------------------------------------------------------------------------------
    // Runs and comparisons for the fact-sheet and content tests
    // ---------------------------------------------------------------------------------------------

    /// <summary>One answer's figures: question id, order index, item revision, published quality and assessed difficulty.</summary>
    public sealed record AnswerSpec(long QuestionId, int Order, int Revision, int Quality, int Difficulty = 50);

    public static BenchmarkRun Run(long id, string provider, string modelId, params AnswerSpec[] answers)
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Core Suite",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc).AddHours(id),
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: provider, modelId: modelId, displayName: modelId),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.8-flash", displayName: "Gemini 3.8 Flash"),
            HarnessVersion = "41",
            ScoringMethodVersion = 12,
            CandidatePromptOptionsJson = "{\"verboseMode\":false}",
            CandidateSystemPromptSha256 = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8",
            ToolGuidesSha256 = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7"
        };

        foreach (var spec in answers)
        {
            run.Answers.Add(Answer(run, spec));
        }

        return run;
    }

    public static BenchmarkRunAnswer Answer(BenchmarkRun run, AnswerSpec spec) => new()
    {
        Id = run.Id * 100 + spec.Order,
        BenchmarkRunId = run.Id,
        BenchmarkQuestionId = spec.QuestionId,
        BenchmarkQuestionIdUsed = spec.QuestionId,
        ItemRevisionUsed = spec.Revision,
        OrderIndex = spec.Order,
        QuestionText = "Question " + spec.QuestionId,
        ExpectedPointsUsed = "- Rubric " + spec.QuestionId,
        ExpectedPointsRecorded = true,
        AnswerText = "Answer " + spec.QuestionId,
        Status = BenchmarkAnswerStatus.Ok,
        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
        QualityScore = spec.Quality,
        PanelQualityScore = spec.Quality,
        AccuracyScore = spec.Quality,
        CompletenessScore = spec.Quality,
        ConcisenessScore = spec.Quality,
        ReadabilityScore = spec.Quality,
        AccuracyLevel = 5,
        CompletenessLevel = 5,
        ConcisenessLevel = 5,
        ReadabilityLevel = 5,
        AssessedDifficulty = spec.Difficulty,
        DurationMs = 10000,
        ToolTimeMs = 0
    };

    public static BenchmarkModelComparisonEntryDto Entry(
        string key,
        IReadOnlyList<long> runIds,
        string label,
        string provider,
        double? quality,
        double? lower = null,
        double? upper = null,
        double? modelTimeP50Ms = 10000,
        double? costPerQuestion = 0.03,
        string state = "Comparable",
        bool speedDegraded = false,
        bool costDegraded = false,
        string explanation = "Comparable: measured under the baseline condition.")
    {
        bool excluded = state == "Excluded";
        return new BenchmarkModelComparisonEntryDto
        {
            Key = key,
            SourceKind = key.StartsWith("group:", StringComparison.Ordinal) ? "Group" : "Run",
            RunIds = runIds.ToList(),
            RunCount = runIds.Count,
            SuiteId = 5,
            SuiteName = "GnollHack Core Suite",
            Provider = provider,
            ModelId = label.ToLowerInvariant().Replace(' ', '-'),
            ModelDisplayName = label,
            Label = label,
            State = state,
            Comparable = state == "Comparable",
            Excluded = excluded,
            SpeedDegraded = speedDegraded,
            CostDegraded = costDegraded,
            Explanation = explanation,
            Quality = excluded || quality == null ? null : new BenchmarkModelComparisonQualityDto
            {
                PointEstimate = quality.Value,
                ItemCount = runIds.Count,
                ExamItemCount = runIds.Count,
                IntervalLower = lower,
                IntervalUpper = upper,
                IntervalHalfWidth = lower.HasValue && upper.HasValue ? (upper.Value - lower.Value) / 2 : null,
                IntervalBasis = "Item sampling only."
            },
            Speed = excluded ? null : new BenchmarkModelComparisonSpeedDto { ModelTimeP50Ms = modelTimeP50Ms },
            Cost = excluded ? null : new BenchmarkModelComparisonCostDto
            {
                CandidateCostPerQuestionUsd = costPerQuestion,
                Basis = "Current",
                PricingResolved = costPerQuestion.HasValue
            }
        };
    }

    public static BenchmarkModelComparisonDto Comparison(params BenchmarkModelComparisonEntryDto[] entries) => new()
    {
        PricingBasis = "Current",
        PricingBasisLabel = "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.",
        BaselineSignature = "sig-7f3a91",
        Entries = entries.ToList(),
        ComparableCount = entries.Count(e => !e.Excluded),
        ExcludedCount = entries.Count(e => e.Excluded),
        ExcludedMeasures = BenchmarkModelComparison.ExcludedMeasures(entries.Count(e => !e.Excluded)).ToList()
    };

    public static BenchmarkReportFactsInput Input(BenchmarkModelComparisonDto comparison, string subjectKey, params BenchmarkRun[] runs)
        => new()
        {
            Comparison = comparison,
            SubjectKey = subjectKey,
            Runs = runs.ToDictionary(r => r.Id)
        };

    /// <summary>A synthesis JSON with one finding per tuple.</summary>
    public static string Synthesis(params (string Kind, string Category, int[] Questions, string Text)[] findings)
    {
        var array = new JsonArray();
        foreach (var f in findings)
        {
            array.Add(new JsonObject
            {
                ["kind"] = f.Kind,
                ["category"] = f.Category,
                ["questions"] = new JsonArray(f.Questions.Select(q => (JsonNode?)JsonValue.Create(q)).ToArray()),
                ["text"] = f.Text
            });
        }

        return new JsonObject { ["finalScore"] = 80, ["findings"] = array }.ToJsonString();
    }
}
