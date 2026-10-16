namespace Overseer.Tests.UnitTests;

using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Sentence case for GnollBench documents: the first word of the text and of every sentence is capitalized
/// when it is a plain lowercase word; abbreviations, decimals, identifiers and code spans are left as written.
/// </summary>
public class BenchmarkReportSentenceCaseTests
{
    [Theory]
    [InlineData("none recorded.", "None recorded.")]
    [InlineData("the model was faster. it was also cheaper! was it? yes.", "The model was faster. It was also cheaper! Was it? Yes.")]
    [InlineData("First paragraph\n\nsecond paragraph.", "First paragraph\n\nSecond paragraph.")]
    [InlineData("It ended (as planned.) then it stopped.", "It ended (as planned.) Then it stopped.")]
    [InlineData("**Speed.** the model was slow.", "**Speed.** The model was slow.")]
    [InlineData("It's done. it's well-known.", "It's done. It's well-known.")]
    public void Apply_CapitalizesTheFirstWordOfEverySentence(string text, string expected)
        => Assert.Equal(expected, BenchmarkReportSentenceCase.Apply(text));

    [Theory]
    [InlineData("Some tools, e.g. wiki_search, ran.")]
    [InlineData("Several checks, i.e. three, ran.")]
    [InlineData("Wiki, source etc. were read.")]
    [InlineData("Model A vs. model B.")]
    [InlineData("See the cost, cf. the price cards.")]
    [InlineData("It took approx. ten seconds.")]
    [InlineData("All roles, incl. graders, were paid.")]
    [InlineData("A and B, resp. faster and slower.")]
    public void Apply_LeavesTheWordAfterAnAbbreviationAlone(string text)
        => Assert.Equal(text, BenchmarkReportSentenceCase.Apply(text));

    [Fact]
    public void Apply_ReadsNoDecimalPointAsASentenceEnd()
    {
        Assert.Equal("From Model I at 8.9 s to Model B at 60.3 s.", BenchmarkReportSentenceCase.Apply("from Model I at 8.9 s to Model B at 60.3 s."));
        Assert.Equal("It cost $0.19. That is cheap.", BenchmarkReportSentenceCase.Apply("It cost $0.19. that is cheap."));
    }

    [Theory]
    [InlineData("gpt-6 answered first.")]
    [InlineData("verboseMode was on.")]
    [InlineData("wiki_search returned nothing.")]
    [InlineData("e.g. this one.")]
    [InlineData("p-value below 0.05.")]
    [InlineData("o3 answered.")]
    [InlineData("`src/x.c` holds it.")]
    public void Apply_LeavesIdentifiersAbbreviationsAndCodeAlone(string text)
        => Assert.Equal(text, BenchmarkReportSentenceCase.Apply(text));

    [Fact]
    public void Apply_NeverReadsInsideACodeSpan()
    {
        const string text = "It read `a. b` and ``x. `y` z``. then it stopped.";

        Assert.Equal("It read `a. b` and ``x. `y` z``. Then it stopped.", BenchmarkReportSentenceCase.Apply(text));
    }

    [Theory]
    [InlineData("*none* recorded.", "*None* recorded.")]
    [InlineData("**not established**", "**Not established**")]
    [InlineData("\"none\" was the answer.", "\"None\" was the answer.")]
    [InlineData("“quoted” first.", "“Quoted” first.")]
    [InlineData("(see Robustness.)", "(See Robustness.)")]
    [InlineData("[none]", "[None]")]
    public void Apply_SkipsEmphasisQuotesAndBracketsToReachTheWord(string text, string expected)
        => Assert.Equal(expected, BenchmarkReportSentenceCase.Apply(text));

    [Fact]
    public void Apply_WithoutASentenceStart_LeavesTheFirstWordButCapitalizesLaterSentences()
    {
        Assert.Equal("would settle it. Then it ends.", BenchmarkReportSentenceCase.Apply("would settle it. then it ends.", sentenceStart: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmptyText_ReturnsAsIs(string? text)
    {
        Assert.Equal(text, BenchmarkReportSentenceCase.Apply(text!));
        Assert.Equal(text, BenchmarkReportSentenceCase.UpperFirst(text!));
    }

    [Theory]
    [InlineData("none", "None")]
    [InlineData("not established", "Not established")]
    [InlineData(" volume", " Volume")]
    [InlineData("run 1", "Run 1")]
    [InlineData("within GPT-6 Luna (high)'s own questions", "Within GPT-6 Luna (high)'s own questions")]
    [InlineData("none recorded. see Robustness.", "None recorded. see Robustness.")]
    [InlineData("gpt-6", "gpt-6")]
    [InlineData("—", "—")]
    [InlineData("12 of 20", "12 of 20")]
    public void UpperFirst_CapitalizesOnlyTheFirstWordOfAFragment(string text, string expected)
    {
        Assert.Equal(expected, BenchmarkReportSentenceCase.UpperFirst(text));
        Assert.Equal(expected, BenchmarkReportSentenceCase.UpperFirst(BenchmarkReportSentenceCase.UpperFirst(text)));
    }

    [Fact]
    public void Abbreviations_AreLowercaseWithTheirFinalDot()
    {
        Assert.Equal(new[] { "e.g.", "i.e.", "etc.", "vs.", "cf.", "approx.", "incl.", "resp." }, BenchmarkReportSentenceCase.Abbreviations);
    }
}
