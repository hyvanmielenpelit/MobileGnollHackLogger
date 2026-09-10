namespace Overseer.Tests.UnitTests;

using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkJsonExtractorTests
{
    [Fact]
    public void Extract_ReturnsBareJson_WhenNoFencesPresent()
    {
        string input = """
            Here is the result:
            {"accuracyLevel": 4, "comment": "good"}
            Hope this helps!
            """;

        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.Equal("""{"accuracyLevel": 4, "comment": "good"}""", extracted);
    }

    [Fact]
    public void Extract_ReturnsFencedJson_WhenSingleFencePresent()
    {
        string input = """
            ```json
            {
              "accuracyLevel": 4
            }
            ```
            """;

        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.Equal("{\n  \"accuracyLevel\": 4\n}", extracted.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Extract_SkipsNonJsonFirstFence_AndExtractsSubsequentJsonFence()
    {
        string input = """
            Here is the citation:
            ```c
            /* from src/zap.c:359 */
            if (rn2(10) > 5) return;
            ```

            And here is the verdict:
            ```json
            {
              "verdict": "Refuted",
              "citation": "src/zap.c:359-364"
            }
            ```
            """;

        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.StartsWith("{", extracted);
        Assert.Contains("\"verdict\": \"Refuted\"", extracted);
    }

    [Fact]
    public void Extract_ReturnsBareArray_WhenArrayPrecedesObject()
    {
        string input = """
            [
              {"claim": "foo"},
              {"claim": "bar"}
            ]
            """;

        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.StartsWith("[", extracted);
        Assert.EndsWith("]", extracted);
    }

    [Fact]
    public void Extract_ReturnsTrimmedInput_WhenNoJsonFound()
    {
        string input = "  Just plain text with no json objects or arrays.  ";
        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.Equal("Just plain text with no json objects or arrays.", extracted);
    }

    [Fact]
    public void Extract_SkipsABracketedWordInProse_AndReturnsTheObjectAfterIt()
    {
        string input = "See [Rubric 2] first.\n{\"accuracyLevel\": 5}";

        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.Equal("{\"accuracyLevel\": 5}", extracted);
    }

    [Fact]
    public void Extract_DropsTrailingTextAfterTheObject()
    {
        string input = "{\"accuracyLevel\": 5}\n` That is the verdict; the {braces} above are the schema.";

        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.Equal("{\"accuracyLevel\": 5}", extracted);
    }

    [Fact]
    public void Extract_KeepsTheWholeObject_WhenStringValuesContainClosingBrackets()
    {
        string input = "Verdict follows.\n{\"comment\": \"ends with ] and } inside\", \"accuracyLevel\": 4}\nDone.";

        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.Equal("{\"comment\": \"ends with ] and } inside\", \"accuracyLevel\": 4}", extracted);
    }

    [Fact]
    public void Extract_ReturnsTheSpan_WhenNoCandidateIsACompleteValue()
    {
        string input = "{ \"a\": 1, .comment }";

        string extracted = BenchmarkJsonExtractor.Extract(input);
        Assert.Equal("{ \"a\": 1, .comment }", extracted);
    }

    [Fact]
    public void Extract_HandlesEmptyOrWhitespace()
    {
        Assert.Equal(string.Empty, BenchmarkJsonExtractor.Extract(string.Empty));
        Assert.Equal(string.Empty, BenchmarkJsonExtractor.Extract("   \r\n\t  "));
    }
}
