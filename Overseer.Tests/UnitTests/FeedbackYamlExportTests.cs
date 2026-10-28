using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overseer.Services.Feedback;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The YAML issues export: header and keys for both sources, determinism and line endings, and the emitter's safety,
/// read back with YamlDotNet so that every text, hostile ones included, parses to exactly what was written and opens
/// no key or document of its own.
/// </summary>
public sealed class FeedbackYamlExportTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExportedAt = new(2026, 10, 23, 12, 34, 56, DateTimeKind.Utc);
    private const string Version = "1.4.0+0a814be0";

    private static string Fp(int n) => n.ToString("x64");

    private static readonly string[] OverseerIssueFacts =
    {
        "category", "status", "firstSeenUtc", "lastSeenUtc", "occurrences", "retained", "distinctUsers", "regressions",
        "lastRegressionUtc", "models", "providers", "modelSources", "systemConfigurations", "overseerVersions",
        "relatedTools", "suspectedCauses", "triagedUtc", "fixedInVersion", "resolvedVia", "marks"
    };

    private static readonly string[] GnollBenchIssueFacts =
    {
        "category", "status", "firstSeenUtc", "lastSeenUtc", "selfReports", "runs", "runIds", "models", "providers",
        "suites", "conditions", "variants", "corpora", "harnessVersions", "relatedTools", "suspectedCauses", "triagedUtc",
        "fixedInVersion", "resolvedVia", "marks"
    };

    private static readonly string[] OverseerOccurrenceFacts =
    {
        "occurredUtc", "provider", "model", "modelSource", "systemConfiguration", "overseerVersion", "noticedVia",
        "suspectedCause", "relatedTools", "marks"
    };

    private static readonly string[] GnollBenchOccurrenceFacts =
    {
        "occurredUtc", "provider", "model", "run", "suite", "questionId", "condition", "variant", "corpus", "harness",
        "noticedVia", "suspectedCause", "relatedTools", "marks"
    };

    // --- Documents ---

    private static FeedbackExportIssue Issue(string subject = "Gnoll lord speed", string summary = "The wiki has no speed.",
        string? details = "Searched the wiki.", bool gnollBench = false) => new()
    {
        Id = gnollBench ? null : 42,
        Fingerprint = Fp(42),
        Category = "missing_information",
        Status = "resolved",
        FirstSeenUtc = T0,
        LastSeenUtc = T0.AddDays(2),
        OccurrenceCount = 3,
        RetainedCount = gnollBench ? null : 2,
        DistinctUserCount = gnollBench ? null : 2,
        RegressionCount = gnollBench ? null : 1,
        LastRegressionUtc = gnollBench ? null : T0.AddDays(1),
        TriagedUtc = T0.AddDays(3),
        FixedInVersion = "1.4.1",
        ResolvedVia = new[] { FeedbackResolvedVia.Wiki, FeedbackResolvedVia.OverseerRelease },
        Models = new[] { "model-a", "model-b" },
        Providers = new[] { "OpenAI" },
        ModelSources = gnollBench ? Array.Empty<string>() : new[] { "system 2" },
        Configurations = gnollBench ? Array.Empty<string>() : new[] { "Config Alpha" },
        Versions = gnollBench ? Array.Empty<string>() : new[] { "1.3.9" },
        RelatedTools = new[] { "wiki_search" },
        SuspectedCauses = new[] { "search_strategy" },
        RunCount = gnollBench ? 2 : null,
        RunIds = gnollBench ? new long[] { 12, 11 } : Array.Empty<long>(),
        Suites = gnollBench ? new[] { "Suite One" } : Array.Empty<string>(),
        Conditions = gnollBench ? new[] { "production" } : Array.Empty<string>(),
        Variants = gnollBench ? new[] { "none" } : Array.Empty<string>(),
        Corpora = gnollBench ? new[] { "live" } : Array.Empty<string>(),
        Harnesses = gnollBench ? new[] { "60" } : Array.Empty<string>(),
        Subject = subject,
        Summary = summary,
        TriageNote = "Checked.",
        Resolution = "Feedback #42: Add the speed\n\nResolved via: GnollHack wiki",
        Occurrences = new[]
        {
            new FeedbackExportOccurrence
            {
                OccurredUtc = T0.AddDays(2),
                Provider = "OpenAI",
                Model = "model-a",
                ModelSource = gnollBench ? null : "system",
                Configuration = gnollBench ? null : "Config Alpha",
                Version = gnollBench ? null : "1.3.9",
                RunId = gnollBench ? 12 : null,
                Suite = gnollBench ? "Suite One" : null,
                QuestionId = gnollBench ? 5 : null,
                Condition = gnollBench ? "production" : null,
                Variant = gnollBench ? "none" : null,
                Corpus = gnollBench ? "live" : null,
                Harness = gnollBench ? "60" : null,
                NoticedVia = "tool_result",
                SuspectedCause = "search_strategy",
                RelatedTools = new[] { "wiki_search" },
                Truncated = true,
                Redacted = !gnollBench,
                Subject = subject,
                Summary = "Own wording.",
                Details = details,
                SearchedFor = "gnoll lord",
                QuestionText = gnollBench ? "How fast is a gnoll lord?" : null
            }
        }
    };

    private static FeedbackExportDocument Document(string source, params FeedbackExportIssue[] issues) => new()
    {
        Source = source,
        ExportedAtUtc = ExportedAt,
        OverseerVersion = Version,
        OccurrencesPerIssue = 10,
        IncludeQuestionText = true,
        Issues = issues
    };

    // --- Reading the YAML back ---

    private static YamlMappingNode Root(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        Assert.Single(stream.Documents);
        return Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);
    }

    private static List<string> Keys(YamlMappingNode node) => node.Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToList();

    private static YamlNode Get(YamlMappingNode node, string key) => node.Children[new YamlScalarNode(key)];

    private static YamlMappingNode Map(YamlMappingNode node, string key) => Assert.IsType<YamlMappingNode>(Get(node, key));

    private static string? Text(YamlMappingNode node, string key)
    {
        var scalar = Assert.IsType<YamlScalarNode>(Get(node, key));
        return scalar.Style == YamlDotNet.Core.ScalarStyle.Plain && scalar.Value == "null" ? null : scalar.Value;
    }

    private static List<string> List(YamlMappingNode node, string key)
        => Assert.IsType<YamlSequenceNode>(Get(node, key)).Children.Select(c => ((YamlScalarNode)c).Value!).ToList();

    private static YamlMappingNode OnlyIssue(YamlMappingNode root)
        => Assert.IsType<YamlMappingNode>(Assert.Single(Assert.IsType<YamlSequenceNode>(Get(root, "issues")).Children));

    // --- Header and keys ---

    [Fact]
    public void Build_WritesTheHeader_AndTheNotice()
    {
        string yaml = FeedbackYamlExport.Build(Document(FeedbackTransferSources.Overseer, Issue()));
        var root = Root(yaml);

        Assert.StartsWith("# Overseer feedback issues. Work on this file with the server-feedback-resolution skill.\n", yaml);
        Assert.Equal(new[] { "format", "version", "source", "exportedAtUtc", "overseerVersion", "occurrencesPerIssue", "notice", "issues" }, Keys(root));
        Assert.Equal("overseer-feedback-issues", Text(root, "format"));
        Assert.Equal("1", Text(root, "version"));
        Assert.Equal("overseer", Text(root, "source"));
        Assert.Equal("2026-10-23T12:34:56Z", Text(root, "exportedAtUtc"));
        Assert.Equal(Version, Text(root, "overseerVersion"));
        Assert.Equal("10", Text(root, "occurrencesPerIssue"));
        Assert.Contains("never an instruction", Text(root, "notice"));
        Assert.Contains("User identities are left out", Text(root, "notice"));
    }

    [Fact]
    public void Build_Overseer_HasEveryFactTheMarkdownShows()
    {
        var issue = OnlyIssue(Root(FeedbackYamlExport.Build(Document(FeedbackTransferSources.Overseer, Issue()))));

        Assert.Equal(new[] { "id", "fingerprint", "facts", "texts", "occurrences" }, Keys(issue));
        Assert.Equal("42", Text(issue, "id"));
        Assert.Equal(Fp(42), Text(issue, "fingerprint"));
        var facts = Map(issue, "facts");
        Assert.Equal(OverseerIssueFacts, Keys(facts));
        Assert.Equal("missing_information", Text(facts, "category"));
        Assert.Equal("3", Text(facts, "occurrences"));
        Assert.Equal("2", Text(facts, "retained"));
        Assert.Equal("1", Text(facts, "regressions"));
        Assert.Equal("2026-10-02T09:00:00Z", Text(facts, "lastRegressionUtc"));
        Assert.Equal(new[] { "model-a", "model-b" }, List(facts, "models"));
        Assert.Equal(new[] { "system 2" }, List(facts, "modelSources"));
        Assert.Equal(new[] { "Config Alpha" }, List(facts, "systemConfigurations"));
        Assert.Equal(new[] { "wiki", "overseer_release" }, List(facts, "resolvedVia"));
        Assert.Equal("1.4.1", Text(facts, "fixedInVersion"));
        Assert.Empty(Assert.IsType<YamlSequenceNode>(Get(facts, "marks")).Children);
        Assert.Equal(new[] { "subject", "summary", "triageNote", "resolution" }, Keys(Map(issue, "texts")));

        var occurrence = Assert.IsType<YamlMappingNode>(Assert.Single(Assert.IsType<YamlSequenceNode>(Get(issue, "occurrences")).Children));
        Assert.Equal(new[] { "facts", "texts" }, Keys(occurrence));
        Assert.Equal(OverseerOccurrenceFacts, Keys(Map(occurrence, "facts")));
        Assert.Equal(new[] { "truncated", "redacted" }, List(Map(occurrence, "facts"), "marks"));
        // The occurrence repeats the issue's subject, so only its own summary is written.
        Assert.Equal(new[] { "summary", "details", "searchedFor", "expectedLocation", "suspectedCauseNote" }, Keys(Map(occurrence, "texts")));
        Assert.Null(Text(Map(occurrence, "texts"), "expectedLocation"));
    }

    [Fact]
    public void Build_GnollBench_HasEveryFactTheMarkdownShows_AndTheQuestionWhenIncluded()
    {
        var issueNode = OnlyIssue(Root(FeedbackYamlExport.Build(Document(FeedbackTransferSources.GnollBench, Issue(gnollBench: true)))));

        Assert.Equal(new[] { "fingerprint", "facts", "texts", "occurrences" }, Keys(issueNode));
        var facts = Map(issueNode, "facts");
        Assert.Equal(GnollBenchIssueFacts, Keys(facts));
        Assert.Equal(new[] { "12", "11" }, List(facts, "runIds"));
        Assert.Equal("2", Text(facts, "runs"));
        var occurrence = Assert.IsType<YamlMappingNode>(Assert.Single(Assert.IsType<YamlSequenceNode>(Get(issueNode, "occurrences")).Children));
        Assert.Equal(GnollBenchOccurrenceFacts, Keys(Map(occurrence, "facts")));
        Assert.Equal("How fast is a gnoll lord?", Text(Map(occurrence, "texts"), "question"));

        var root = Root(FeedbackYamlExport.Build(new FeedbackExportDocument
        {
            Source = FeedbackTransferSources.GnollBench,
            ExportedAtUtc = ExportedAt,
            OverseerVersion = Version,
            OccurrencesPerIssue = 5,
            IncludeQuestionText = false,
            Issues = new[] { Issue(gnollBench: true) }
        }));
        Assert.Equal("false", Text(root, "includeQuestionText"));
        var bare = Assert.IsType<YamlMappingNode>(Assert.Single(Assert.IsType<YamlSequenceNode>(Get(OnlyIssue(root), "occurrences")).Children));
        Assert.DoesNotContain("question", Keys(Map(bare, "texts")));
    }

    [Fact]
    public void Build_WithNoIssues_WritesAnEmptyList()
    {
        var root = Root(FeedbackYamlExport.Build(Document(FeedbackTransferSources.Overseer)));
        Assert.Empty(Assert.IsType<YamlSequenceNode>(Get(root, "issues")).Children);
    }

    [Fact]
    public void Build_IsDeterministic_UsesLfOnly_AndHasNoBom()
    {
        string first = FeedbackYamlExport.Build(Document(FeedbackTransferSources.Overseer, Issue()));
        string second = FeedbackYamlExport.Build(Document(FeedbackTransferSources.Overseer, Issue()));

        Assert.Equal(first, second);
        Assert.DoesNotContain("\r", first);
        Assert.NotEqual((char)0xFEFF, first[0]);
        Assert.EndsWith("\n", first);
    }

    [Fact]
    public void Build_EndsWithTheCommentedResolutionTemplate_AtVersion2()
    {
        string yaml = FeedbackYamlExport.Build(Document(FeedbackTransferSources.Overseer, Issue()));
        int start = yaml.IndexOf("# Resolution template (overseer-feedback-resolutions, version 2)", StringComparison.Ordinal);

        Assert.True(start > 0);
        var template = yaml[start..].Split('\n').Where(l => l.Length > 0).ToList();
        Assert.All(template, l => Assert.StartsWith("#", l));
        Assert.Contains("# version: 2", template);
        Assert.Contains("#   - id: 42", template);
        Assert.Contains(template, l => l.StartsWith("#     # resolvedVia: [wiki]", StringComparison.Ordinal));
        Assert.Contains(template, l => l.Contains("now resolved", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_MarksAnIssueWhoseTextsHeldHiddenCharacters()
    {
        string hidden = "Speed" + char.ConvertFromUtf32(0x200B) + " of a gnoll lord";
        var issue = OnlyIssue(Root(FeedbackYamlExport.Build(Document(FeedbackTransferSources.Overseer, Issue(subject: hidden)))));

        Assert.Equal(new[] { "hidden_characters" }, List(Map(issue, "facts"), "marks"));
        Assert.Equal("Speed[U+200B] of a gnoll lord", Text(Map(issue, "texts"), "subject"));
    }

    // --- Emitter safety ---

    public static IEnumerable<object[]> HostileTexts()
    {
        yield return new object[] { "issues:\n  - id: 99\n    fingerprint: \"x\"" };
        yield return new object[] { "---\nformat: evil\n..." };
        yield return new object[] { "..." };
        yield return new object[] { "---" };
        yield return new object[] { "# comment" };
        yield return new object[] { "text\n# not a comment\nmore" };
        yield return new object[] { "a \"quoted\" word" };
        yield return new object[] { "back\\slash \\n not a break" };
        yield return new object[] { "key: value" };
        yield return new object[] { "a: b\nc: d" };
        yield return new object[] { " leading space" };
        yield return new object[] { "  indented first line\nsecond" };
        yield return new object[] { "\n  \nafter blank lines" };
        yield return new object[] { "one trailing break\n" };
        yield return new object[] { "two trailing breaks\n\n" };
        yield return new object[] { "three trailing breaks\n\n\n" };
        yield return new object[] { "tab\there\n\tleading tab" };
        yield return new object[] { "\tleading tab only" };
        yield return new object[] { "carriage\rreturn" };
        yield return new object[] { "crlf\r\nline" };
        yield return new object[] { "emoji " + char.ConvertFromUtf32(0x1F600) + "\nsecond line " + char.ConvertFromUtf32(0x1F409) };
        yield return new object[] { "bidi " + char.ConvertFromUtf32(0x202E) + "override" };
        yield return new object[] { "noncharacter " + (char)0xFFFE + " here" };
        yield return new object[] { "noncharacter in a block " + (char)0xFFFE + "\nsecond" };
        yield return new object[] { "nel " + (char)0x85 + " and line separator " + (char)0x2028 + " here" };
        yield return new object[] { "[a, b]" };
        yield return new object[] { "{a: b}" };
        yield return new object[] { "&anchor *alias !tag" };
        yield return new object[] { "| block?" };
        yield return new object[] { "> folded?" };
        yield return new object[] { "- item" };
        yield return new object[] { "null" };
        yield return new object[] { "trailing spaces   \nline   " };
        yield return new object[] { "   " };
    }

    [Theory]
    [MemberData(nameof(HostileTexts))]
    public void Build_ParsesEveryHostileTextBackUnchanged_AndAddsNoKeyOrDocument(string hostile)
    {
        var document = Document(FeedbackTransferSources.Overseer, Issue(subject: hostile, summary: hostile, details: hostile));
        string yaml = FeedbackYamlExport.Build(document);

        var root = Root(yaml);
        Assert.Equal(new[] { "format", "version", "source", "exportedAtUtc", "overseerVersion", "occurrencesPerIssue", "notice", "issues" }, Keys(root));
        Assert.Equal("overseer-feedback-issues", Text(root, "format"));
        var issue = OnlyIssue(root);
        Assert.Equal(new[] { "id", "fingerprint", "facts", "texts", "occurrences" }, Keys(issue));
        Assert.Equal(OverseerIssueFacts, Keys(Map(issue, "facts")));

        string expected = FeedbackMarkdownExport.RevealHidden(hostile, out _);
        var texts = Map(issue, "texts");
        Assert.Equal(new[] { "subject", "summary", "triageNote", "resolution" }, Keys(texts));
        Assert.Equal(expected, Text(texts, "subject"));
        Assert.Equal(expected, Text(texts, "summary"));

        var occurrence = Assert.IsType<YamlMappingNode>(Assert.Single(Assert.IsType<YamlSequenceNode>(Get(issue, "occurrences")).Children));
        var occurrenceTexts = Map(occurrence, "texts");
        Assert.Equal(new[] { "summary", "details", "searchedFor", "expectedLocation", "suspectedCauseNote" }, Keys(occurrenceTexts));
        // A whitespace-only details text is left out as blank, like the Markdown export does.
        Assert.Equal(string.IsNullOrWhiteSpace(hostile) ? null : expected, Text(occurrenceTexts, "details"));
    }

    [Fact]
    public void Build_RecoversEveryTextExactly_AfterRevealingHiddenCharacters()
    {
        string hidden = "a" + char.ConvertFromUtf32(0xE0041) + "b" + (char)0x07 + "c" + (char)0xD800 + "d";
        var issue = OnlyIssue(Root(FeedbackYamlExport.Build(Document(FeedbackTransferSources.Overseer, Issue(details: hidden)))));
        var occurrence = Assert.IsType<YamlMappingNode>(Assert.Single(Assert.IsType<YamlSequenceNode>(Get(issue, "occurrences")).Children));

        Assert.Equal("a[U+E0041]b[U+0007]c[U+D800]d", Text(Map(occurrence, "texts"), "details"));
        Assert.Contains("hidden_characters", List(Map(occurrence, "facts"), "marks"));
        Assert.Equal("Feedback #42: Add the speed\n\nResolved via: GnollHack wiki", Text(Map(issue, "texts"), "resolution"));
    }

    [Theory]
    [InlineData("a\nb", "|-")]
    [InlineData("a\nb\n", "|")]
    [InlineData("a\nb\n\n", "|+")]
    [InlineData(" a\nb", "|2-")]
    [InlineData("\na\nb", "|-")]
    [InlineData("\n a\nb", "|2-")]
    [InlineData("  \na", "|2-")]
    public void LiteralBlock_ChoosesChompingAndIndentationIndicator(string text, string header)
    {
        Assert.True(FeedbackYamlWriter.TryLiteralBlock(text, out string actual, out _));
        Assert.Equal(header, actual);
    }

    [Theory]
    [InlineData("single line")]
    [InlineData("carriage\r\nreturn")]
    [InlineData("\n\n")]
    public void LiteralBlock_IsRefusedForTextsThatMustBeQuoted(string text)
    {
        Assert.False(FeedbackYamlWriter.TryLiteralBlock(text, out _, out _));
    }
}
