namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkDifficultyParserTests
{
    [Fact]
    public void Parse_FencedJsonWithQuestionsWrapper_ParsesCorrectly()
    {
        var raw = """
            Here is the difficulty assessment:
            ```json
            {
              "questions": [
                { "id": 101, "difficulty": 45 },
                { "id": 102, "difficulty": 80 }
              ]
            }
            ```
            Hope this helps!
            """;

        var result = BenchmarkDifficultyParser.Parse(raw);

        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(101, result.Items[0].Id);
        Assert.Equal(45, result.Items[0].Difficulty);
        Assert.Equal(102, result.Items[1].Id);
        Assert.Equal(80, result.Items[1].Difficulty);
        Assert.False(result.Salvaged);
    }

    [Fact]
    public void Parse_TopLevelArray_ParsesCorrectly()
    {
        var raw = """
            [
              { "id": 1, "difficulty": 25 },
              { "id": 2, "difficulty": 90 }
            ]
            """;

        var result = BenchmarkDifficultyParser.Parse(raw);

        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Items[0].Id);
        Assert.Equal(25, result.Items[0].Difficulty);
        Assert.Equal(2, result.Items[1].Id);
        Assert.Equal(90, result.Items[1].Difficulty);
    }

    [Fact]
    public void Parse_AlternativeWrapperKeys_ParsesRatingsAndItems()
    {
        var raw1 = """{ "ratings": [{ "id": 10, "difficulty": 60 }] }""";
        var res1 = BenchmarkDifficultyParser.Parse(raw1);
        Assert.True(res1.Success);
        Assert.Single(res1.Items);
        Assert.Equal(60, res1.Items[0].Difficulty);

        var raw2 = """{ "assessments": [{ "id": 20, "difficulty": 70 }] }""";
        var res2 = BenchmarkDifficultyParser.Parse(raw2);
        Assert.True(res2.Success);
        Assert.Single(res2.Items);
        Assert.Equal(70, res2.Items[0].Difficulty);

        var raw3 = """{ "difficulties": [{ "id": 30, "difficulty": 80 }] }""";
        var res3 = BenchmarkDifficultyParser.Parse(raw3);
        Assert.True(res3.Success);
        Assert.Single(res3.Items);
        Assert.Equal(80, res3.Items[0].Difficulty);
    }

    [Fact]
    public void Parse_SingleRootLevelObject_ParsesSingleItem()
    {
        var raw = """{ "id": 42, "difficulty": 77 }""";

        var result = BenchmarkDifficultyParser.Parse(raw);

        Assert.True(result.Success);
        Assert.Single(result.Items);
        Assert.Equal(42, result.Items[0].Id);
        Assert.Equal(77, result.Items[0].Difficulty);
    }

    [Fact]
    public void Parse_ClampsDifficultyBetween1And100()
    {
        var raw = """
            [
              { "id": 1, "difficulty": -10 },
              { "id": 2, "difficulty": 0 },
              { "id": 3, "difficulty": 100 },
              { "id": 4, "difficulty": 250 }
            ]
            """;

        var result = BenchmarkDifficultyParser.Parse(raw);

        Assert.True(result.Success);
        Assert.Equal(4, result.Items.Count);
        Assert.Equal(1, result.Items[0].Difficulty);
        Assert.Equal(1, result.Items[1].Difficulty);
        Assert.Equal(100, result.Items[2].Difficulty);
        Assert.Equal(100, result.Items[3].Difficulty);
    }

    [Fact]
    public void Parse_StringIdAndDifficulty_ParsesNumbers()
    {
        var raw = """
            {
              "questions": [
                { "id": "15", "difficulty": "55" }
              ]
            }
            """;

        var result = BenchmarkDifficultyParser.Parse(raw);

        Assert.True(result.Success);
        Assert.Single(result.Items);
        Assert.Equal(15, result.Items[0].Id);
        Assert.Equal(55, result.Items[0].Difficulty);
    }

    [Fact]
    public void Parse_TruncatedJson_RecoversViaSalvageLayer()
    {
        // Malformed/truncated JSON where second item is incomplete
        var raw = """
            [
              { "id": 101, "difficulty": 50 },
              { "id": 102, "diffic
            """;

        var result = BenchmarkDifficultyParser.Parse(raw);

        Assert.True(result.Success);
        Assert.True(result.Salvaged);
        Assert.Single(result.Items);
        Assert.Equal(101, result.Items[0].Id);
        Assert.Equal(50, result.Items[0].Difficulty);
    }

    [Fact]
    public void Parse_CompletelyInvalidText_ReturnsFailure()
    {
        var raw = "I am an AI assistant and I cannot evaluate this without more info.";

        var result = BenchmarkDifficultyParser.Parse(raw);

        Assert.False(result.Success);
        Assert.Empty(result.Items);
        Assert.Equal(raw, result.RawResponse);
    }
}
