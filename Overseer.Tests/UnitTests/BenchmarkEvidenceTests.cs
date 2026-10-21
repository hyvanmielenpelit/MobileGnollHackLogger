using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Pins the gold-evidence unit model: its validation messages, its canonical JSON and the
/// suite-level evidence fingerprint.
/// </summary>
public class BenchmarkEvidenceTests
{
    private static BenchmarkEvidenceUnit Wiki(string path, bool personal = false)
        => new() { Kind = BenchmarkEvidenceUnit.WikiKind, Path = path, Personal = personal };

    private static BenchmarkEvidenceUnit Source(string path, string? lines = null, string? function = null)
        => new() { Kind = BenchmarkEvidenceUnit.SourceKind, Path = path, Lines = lines, Function = function };

    [Fact]
    public void Validate_AcceptsNullAndValidUnits()
    {
        Assert.Null(BenchmarkEvidence.Validate(null));
        Assert.Null(BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit>
        {
            Wiki("Races/Human.md", personal: true),
            new() { Kind = BenchmarkEvidenceUnit.WikiKind, Path = "Artifacts/The Orb of Fate.md", Section = "Invoking" },
            Source("src/artifact.c", lines: "4194-4231"),
            Source("src/artifact.c", function: "arti_invoke"),
            new() { Kind = BenchmarkEvidenceUnit.KnowledgeBaseKind, Path = "Praying" }
        }));
    }

    [Fact]
    public void Validate_RejectsAnUnknownKind()
    {
        string? error = BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit>
        {
            Wiki("Races/Human.md"),
            new() { Kind = "web", Path = "https://example.com" }
        });

        Assert.NotNull(error);
        Assert.StartsWith("Evidence unit 2: unknown kind 'web'", error);
    }

    [Fact]
    public void Validate_RejectsAnEmptyPath()
    {
        string? error = BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit> { Wiki("  ") });

        Assert.Equal("Evidence unit 1: path is empty.", error);
    }

    [Theory]
    [InlineData("Races\\Human.md")]
    [InlineData("/Races/Human.md")]
    [InlineData("C:/wiki/Races/Human.md")]
    [InlineData("Races/../Human.md")]
    public void Validate_RejectsAPathThatIsNotRepositoryRelative(string path)
    {
        string? error = BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit> { Wiki(path) });

        Assert.Equal($"Evidence unit 1: path '{path}' must be repository-relative with forward slashes.", error);
    }

    [Fact]
    public void Validate_LetsAKnowledgeBaseTopicCarryAnyCharacters()
    {
        Assert.Null(BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit>
        {
            new() { Kind = BenchmarkEvidenceUnit.KnowledgeBaseKind, Path = "Altars: identify B/U/C" }
        }));
    }

    [Theory]
    [InlineData("10-5")]
    [InlineData("0-3")]
    [InlineData("abc")]
    [InlineData("12")]
    [InlineData("L10-L20")]
    public void Validate_RejectsBadLines(string lines)
    {
        string? error = BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit> { Source("src/artifact.c", lines: lines) });

        Assert.Equal($"Evidence unit 1: lines '{lines}' must read a-b with 1 <= a <= b.", error);
    }

    [Fact]
    public void Validate_RejectsLinesOnAWikiUnit()
    {
        var unit = Wiki("Races/Human.md");
        unit.Lines = "1-10";

        Assert.Equal("Evidence unit 1: lines apply to source units only.",
            BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit> { unit }));
    }

    [Fact]
    public void Validate_RejectsAFunctionOnAWikiUnitAndASectionOnASourceUnit()
    {
        var wiki = Wiki("Races/Human.md");
        wiki.Function = "u_init";
        var source = Source("src/u_init.c");
        source.Section = "Starting inventory";

        Assert.Equal("Evidence unit 1: function applies to source units only.",
            BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit> { wiki }));
        Assert.Equal("Evidence unit 1: section applies to wiki units only.",
            BenchmarkEvidence.Validate(new List<BenchmarkEvidenceUnit> { source }));
    }

    [Theory]
    [InlineData("4194-4231", true, 4194, 4231)]
    [InlineData(" 7-7 ", true, 7, 7)]
    [InlineData("5-4", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    public void TryParseLines_ReadsAnInclusiveRange(string? lines, bool ok, int start, int end)
    {
        bool parsed = BenchmarkEvidence.TryParseLines(lines, out int a, out int b);

        Assert.Equal(ok, parsed);
        if (ok)
        {
            Assert.Equal(start, a);
            Assert.Equal(end, b);
        }
    }

    [Fact]
    public void ToJson_OmitsNullFieldsAndAFalsePersonalFlag()
    {
        string? json = BenchmarkEvidence.ToJson(new List<BenchmarkEvidenceUnit>
        {
            Wiki("Races/Human.md", personal: true),
            Wiki("Races/Orc.md"),
            Source("src/artifact.c", lines: "4194-4231")
        });

        Assert.Equal(
            "[{\"kind\":\"wiki\",\"path\":\"Races/Human.md\",\"personal\":true}," +
            "{\"kind\":\"wiki\",\"path\":\"Races/Orc.md\"}," +
            "{\"kind\":\"source\",\"path\":\"src/artifact.c\",\"lines\":\"4194-4231\"}]",
            json);
    }

    [Fact]
    public void ToJson_IsNullForNullOrEmpty()
    {
        Assert.Null(BenchmarkEvidence.ToJson(null));
        Assert.Null(BenchmarkEvidence.ToJson(new List<BenchmarkEvidenceUnit>()));
    }

    [Fact]
    public void FromJson_RoundTripsEveryField()
    {
        var units = new List<BenchmarkEvidenceUnit>
        {
            new() { Kind = BenchmarkEvidenceUnit.WikiKind, Path = "Roles/Healer.md", Section = "Starting inventory", Personal = true },
            Source("src/artifact.c", lines: "4194-4231", function: "arti_invoke"),
            new() { Kind = BenchmarkEvidenceUnit.KnowledgeBaseKind, Path = "Praying" }
        };

        string? json = BenchmarkEvidence.ToJson(units);
        var back = BenchmarkEvidence.FromJson(json);

        Assert.NotNull(back);
        Assert.Equal(3, back!.Count);
        Assert.Equal("wiki", back[0].Kind);
        Assert.Equal("Roles/Healer.md", back[0].Path);
        Assert.Equal("Starting inventory", back[0].Section);
        Assert.True(back[0].Personal);
        Assert.Equal("4194-4231", back[1].Lines);
        Assert.Equal("arti_invoke", back[1].Function);
        Assert.False(back[1].Personal);
        Assert.Equal("kb", back[2].Kind);
        Assert.Null(back[2].Section);
        Assert.Null(back[2].Lines);
        Assert.Null(back[2].Function);
        Assert.Equal(json, BenchmarkEvidence.ToJson(back));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"kind\":\"wiki\"}")]
    public void FromJson_IsNullWhenAbsentEmptyOrUnreadable(string? json)
    {
        Assert.Null(BenchmarkEvidence.FromJson(json));
    }

    [Fact]
    public void SuiteEvidenceSha_IsStableUnderQuestionOrder()
    {
        var a = new BenchmarkQuestion { Id = 11, QuestionText = "a", EvidenceJson = "[{\"kind\":\"wiki\",\"path\":\"Races/Human.md\"}]" };
        var b = new BenchmarkQuestion { Id = 9, QuestionText = "b", EvidenceJson = "[{\"kind\":\"kb\",\"path\":\"Praying\"}]" };
        var none = new BenchmarkQuestion { Id = 12, QuestionText = "c" };

        string? forward = BenchmarkEvidence.SuiteEvidenceSha(new[] { a, b, none });
        string? reverse = BenchmarkEvidence.SuiteEvidenceSha(new[] { none, b, a });

        Assert.NotNull(forward);
        Assert.Equal(64, forward!.Length);
        Assert.Equal(forward.ToLowerInvariant(), forward);
        Assert.Equal(forward, reverse);
        Assert.Equal(forward[..12], BenchmarkEvidence.SuiteEvidenceShaPrefix(new[] { b, a }));
    }

    [Fact]
    public void SuiteEvidenceSha_ReadsTheCanonicalFormOfStoredJson()
    {
        var spaced = new BenchmarkQuestion { Id = 3, QuestionText = "a", EvidenceJson = "[ { \"path\": \"Praying\", \"kind\": \"kb\" } ]" };
        var canonical = new BenchmarkQuestion { Id = 3, QuestionText = "a", EvidenceJson = "[{\"kind\":\"kb\",\"path\":\"Praying\"}]" };

        Assert.Equal(BenchmarkEvidence.SuiteEvidenceSha(new[] { canonical }), BenchmarkEvidence.SuiteEvidenceSha(new[] { spaced }));
    }

    [Fact]
    public void SuiteEvidenceSha_ChangesWhenAQuestionsEvidenceChanges()
    {
        var before = new BenchmarkQuestion { Id = 3, QuestionText = "a", EvidenceJson = "[{\"kind\":\"kb\",\"path\":\"Praying\"}]" };
        var after = new BenchmarkQuestion { Id = 3, QuestionText = "a", EvidenceJson = "[{\"kind\":\"kb\",\"path\":\"Prayer timeout\"}]" };

        Assert.NotEqual(BenchmarkEvidence.SuiteEvidenceSha(new[] { before }), BenchmarkEvidence.SuiteEvidenceSha(new[] { after }));
    }

    [Fact]
    public void SuiteEvidenceSha_IsNullWhenNoQuestionHasEvidence()
    {
        var questions = new[]
        {
            new BenchmarkQuestion { Id = 1, QuestionText = "a" },
            new BenchmarkQuestion { Id = 2, QuestionText = "b", EvidenceJson = "[]" }
        };

        Assert.Null(BenchmarkEvidence.SuiteEvidenceSha(questions));
        Assert.Null(BenchmarkEvidence.SuiteEvidenceShaPrefix(questions));
        Assert.Null(BenchmarkEvidence.SuiteEvidenceSha(new BenchmarkQuestion[0]));
    }
}
