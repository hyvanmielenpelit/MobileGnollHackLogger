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
    public void Extract_HandlesEmptyOrWhitespace()
    {
        Assert.Equal(string.Empty, BenchmarkJsonExtractor.Extract(string.Empty));
        Assert.Equal(string.Empty, BenchmarkJsonExtractor.Extract("   \r\n\t  "));
    }
}
