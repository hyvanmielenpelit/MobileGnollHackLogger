using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GnollHackServer.Data.Legal;
using Xunit;

namespace Overseer.Tests.Legal;

/// <summary>Reading a Terms document from the wiki folder: its version line, the Git fallback, its hash and its links.</summary>
public class LegalDocumentReaderTests : IDisposable
{
    private readonly TempWiki _wiki = new();

    public void Dispose() => _wiki.Dispose();

    private LegalDocument Read(string content, LegalVersionSource source = LegalVersionSource.DocumentDate)
    {
        _wiki.Write(content);
        var document = LegalTestSupport.Reader(_wiki.Path, source).Get(LegalDocumentKeys.OverseerTerms);
        Assert.NotNull(document);
        return document!;
    }

    // ── The Last updated line ───────────────────────────────────────────────────

    [Fact]
    public void TheWikisWrittenOutDateGivesTheIsoVersionAndKeepsItsWordingAsTheLabel()
    {
        var document = Read(LegalTestSupport.RealFirstLines);

        Assert.Equal("2026-10-18", document.Version);
        Assert.Equal("18 October 2026", document.VersionLabel);
        Assert.Equal("document", document.VersionOrigin);
    }

    [Theory]
    [InlineData("**Last updated:** 2026-10-18", "2026-10-18", "2026-10-18")]
    [InlineData("Last updated: 18 October 2026", "2026-10-18", "18 October 2026")]
    [InlineData("**Last updated:** 1 May 2027", "2027-05-01", "1 May 2027")]
    [InlineData("**Last updated: 18 October 2026**", "2026-10-18", "18 October 2026")]
    [InlineData("**last updated:** 18 october 2026", "2026-10-18", "18 october 2026")]
    public void OtherAcceptedFormsParse(string line, string version, string label)
    {
        var document = Read(line + "\n\nText.\n");

        Assert.Equal(version, document.Version);
        Assert.Equal(label, document.VersionLabel);
    }

    [Theory]
    [InlineData("**Last updated:** 18 October 2026 (revision 2)", "2026-10-18.2", "18 October 2026 (revision 2)")]
    [InlineData("**Last updated:** 18 October 2026 (revision 1)", "2026-10-18", "18 October 2026 (revision 1)")]
    [InlineData("**Last updated:** 2026-10-18 (revision 10)", "2026-10-18.10", "2026-10-18 (revision 10)")]
    public void ARevisionIsAppendedFromTwoUp(string line, string version, string label)
    {
        var document = Read(line + "\n");

        Assert.Equal(version, document.Version);
        Assert.Equal(label, document.VersionLabel);
    }

    [Theory]
    [InlineData("**Last updated:** 18 Oct 2026")]
    [InlineData("**Last updated:** October 18, 2026")]
    public void AnUnparsableDateFallsBackToGit(string line)
    {
        // A plain folder, not a repository: the Git fallback runs and finds nothing.
        var document = Read(line + "\n\nText.\n");

        Assert.Null(document.Version);
        Assert.Null(document.VersionLabel);
        Assert.Null(document.VersionOrigin);
    }

    [Fact]
    public void ParsingDoesNotDependOnTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            CultureInfo.CurrentUICulture = new CultureInfo("fi-FI");

            var document = Read(LegalTestSupport.RealFirstLines);

            Assert.Equal("2026-10-18", document.Version);
            Assert.Equal("18 October 2026", LegalVersion.FormatLabel("2026-10-18"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void OnlyTheFirstTwentyLinesCount()
    {
        string filler = string.Concat(Enumerable.Repeat("Line.\n", LegalDocumentReader.VersionLineSearchLimit));
        var document = Read(filler + "**Last updated:** 18 October 2026\n");

        Assert.Null(document.Version);

        var withinLimit = Read(string.Concat(Enumerable.Repeat("Line.\n", LegalDocumentReader.VersionLineSearchLimit - 1))
            + "**Last updated:** 18 October 2026\n");
        Assert.Equal("2026-10-18", withinLimit.Version);
    }

    // ── LegalVersion ────────────────────────────────────────────────────────────

    [Fact]
    public void VersionsCompareByDateThenByRevision()
    {
        Assert.True(LegalVersion.Compare("2026-10-18.10", "2026-10-18.2") > 0);
        Assert.True(LegalVersion.Compare("2026-10-18.2", "2026-10-18") > 0);
        Assert.True(LegalVersion.Compare("2026-10-18", "2026-10-19") < 0);
        Assert.True(LegalVersion.Compare("2026-10-18.9", "2026-10-19") < 0);
        Assert.Equal(0, LegalVersion.Compare("2026-10-18", "2026-10-18"));
        // Ordinal comparison would get this one wrong.
        Assert.True(string.CompareOrdinal("2026-10-18.10", "2026-10-18.2") < 0);
    }

    [Fact]
    public void VersionsFormatAsTheDocumentsWriteThem()
    {
        Assert.Equal("18 October 2026", LegalVersion.FormatLabel("2026-10-18"));
        Assert.Equal("18 October 2026 (revision 2)", LegalVersion.FormatLabel("2026-10-18.2"));
        Assert.Equal("1 May 2027", LegalVersion.FormatLabel("2027-05-01"));
        Assert.Equal("2026-10-18.2", LegalVersion.Format(new DateOnly(2026, 10, 18), 2));
        Assert.Equal("2026-10-18", LegalVersion.Format(new DateOnly(2026, 10, 18), 1));
    }

    // ── The Git fallback ────────────────────────────────────────────────────────

    private void InitRepositoryWithOneCommit(string content)
    {
        _wiki.Write(content);
        LegalTestSupport.Git(_wiki.Path, "init", "--quiet");
        LegalTestSupport.Git(_wiki.Path, "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
            "-c", "commit.gpgsign=false", "add", "--all");
        LegalTestSupport.Git(_wiki.Path, "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Terms");
    }

    [Fact]
    public void WithoutALineTheVersionIsTheGitCommitDate()
    {
        Assert.SkipUnless(LegalTestSupport.GitAvailable(), "git is not installed.");
        InitRepositoryWithOneCommit("These Terms have no Last updated line.\n");

        var document = LegalTestSupport.Reader(_wiki.Path).Get(LegalDocumentKeys.OverseerTerms);

        Assert.NotNull(document);
        Assert.Equal("git", document!.VersionOrigin);
        Assert.True(LegalVersion.TryParse(document.Version, out var date, out int revision));
        Assert.Equal(1, revision);
        Assert.InRange(date.DayNumber, DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - 1, DateOnly.FromDateTime(DateTime.UtcNow).DayNumber + 1);
        Assert.Equal(LegalVersion.FormatLabel(document.Version), document.VersionLabel);
    }

    [Fact]
    public void GitCommitDateModeIgnoresTheLine()
    {
        Assert.SkipUnless(LegalTestSupport.GitAvailable(), "git is not installed.");
        InitRepositoryWithOneCommit("**Last updated:** 1 January 2020\n");

        var document = LegalTestSupport.Reader(_wiki.Path, LegalVersionSource.GitCommitDate).Get(LegalDocumentKeys.OverseerTerms);

        Assert.NotNull(document);
        Assert.Equal("git", document!.VersionOrigin);
        Assert.NotEqual("2020-01-01", document.Version);
    }

    [Fact]
    public void AMissingGitExecutableGivesNoVersion()
    {
        _wiki.Write("No line.\n");

        var document = LegalTestSupport.Reader(_wiki.Path, gitExecutable: "git-that-does-not-exist-" + Guid.NewGuid().ToString("N"))
            .Get(LegalDocumentKeys.OverseerTerms);

        Assert.NotNull(document);
        Assert.Null(document!.Version);
    }

    // ── Hash, links, missing and edited files ───────────────────────────────────

    [Fact]
    public void TheHashIsOfTheRawBytes()
    {
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(LegalTestSupport.RealFirstLines)).ToArray();
        File.WriteAllBytes(Path.Combine(_wiki.Path, LegalTestSupport.TermsFile), bytes);

        var document = LegalTestSupport.Reader(_wiki.Path).Get(LegalDocumentKeys.OverseerTerms)!;

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), document.ContentSha256);
        Assert.Equal(64, document.ContentSha256.Length);
        // The BOM is hashed but not rendered.
        Assert.StartsWith("**Last updated:**", document.Markdown);
    }

    [Fact]
    public void GollumLinksAreRewrittenToAbsoluteLinks()
    {
        var document = Read("**Last updated:** 18 October 2026\n\nSee [[Gnoll Overseer Privacy Policy]] and [[the providers|Overseer AI Providers]]. [Plain](https://example.com) stays.\n");

        Assert.Contains("[Gnoll Overseer Privacy Policy](https://wiki.gnollhack.com/Gnoll%20Overseer%20Privacy%20Policy.md)", document.Markdown);
        Assert.Contains("[the providers](https://wiki.gnollhack.com/Overseer%20AI%20Providers.md)", document.Markdown);
        Assert.Contains("[Plain](https://example.com) stays.", document.Markdown);
        Assert.DoesNotContain("[[", document.Markdown);
    }

    [Fact]
    public void TheDocumentCarriesItsTitleAndUrls()
    {
        var document = Read(LegalTestSupport.RealFirstLines);

        Assert.Equal(LegalTestSupport.TermsPage, document.Title);
        Assert.Equal("https://wiki.gnollhack.com/Gnoll%20Overseer%20Terms%20of%20Service.md", document.WikiUrl);
        Assert.Equal("https://wiki.gnollhack.com/Gnoll%20Overseer%20Privacy%20Policy.md", document.PrivacyPolicyUrl);
    }

    [Fact]
    public void AMissingFileGivesNull()
    {
        var reader = LegalTestSupport.Reader(_wiki.Path);

        Assert.Null(reader.Get(LegalDocumentKeys.OverseerTerms));
        Assert.Null(reader.Get("unknown-document"));
        Assert.Null(LegalTestSupport.Reader(wikiPath: null).Get(LegalDocumentKeys.OverseerTerms));
    }

    [Fact]
    public void AnEditedFileIsReadAgain()
    {
        string file = _wiki.Write("**Last updated:** 18 October 2026\n");
        var reader = LegalTestSupport.Reader(_wiki.Path);
        Assert.Equal("2026-10-18", reader.Get(LegalDocumentKeys.OverseerTerms)!.Version);

        File.WriteAllText(file, "**Last updated:** 18 October 2026 (revision 2)\n", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal("2026-10-18.2", reader.Get(LegalDocumentKeys.OverseerTerms)!.Version);
    }
}
