using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ChatTokenEstimatorTests
{
    private static string LargePng(int width, int height)
    {
        var bytes = new byte[1024 * 1024];
        ImageDimensionReaderTests.Png(width, height).CopyTo(bytes, 0);
        return Convert.ToBase64String(bytes);
    }

    [Theory]
    // 1000 / 28 rounds up to 36, 500 / 28 to 18.
    [InlineData("Anthropic", 1000, 500, 648)]
    // The long edge fits to 2,576, and the patch count hits the 4,784 cap.
    [InlineData("Anthropic", 8000, 8000, 4784)]
    [InlineData("anthropic", 28, 28, 1)]
    // 32 x 32 patches x 1.2, rounded up.
    [InlineData("OpenAI", 1024, 1024, 1229)]
    [InlineData("OpenAI", 16000, 16000, 36000)]
    // A small image is one 258-token tile, under the 1,120 default.
    [InlineData("Google", 300, 300, 1120)]
    // 11 x 11 tiles of 258.
    [InlineData("Google", 8000, 8000, 31218)]
    [InlineData("SomeoneElse", 1000, 1000, ChatTokenEstimator.FallbackImageTokens)]
    [InlineData(null, 1000, 1000, ChatTokenEstimator.FallbackImageTokens)]
    [InlineData("Anthropic", 0, 1000, ChatTokenEstimator.FallbackImageTokens)]
    public void EstimateImage_FollowsEachProvidersRule(string? provider, int width, int height, int expected)
        => Assert.Equal(expected, ChatTokenEstimator.EstimateImage(provider, width, height));

    [Fact]
    public void PlainContent_IsItsLengthOverFour()
    {
        var estimator = new ChatTokenEstimator("OpenAI");

        Assert.Equal(new ChatMessageEstimate(10, 0), estimator.EstimateMessage(new { role = "user", content = new string('x', 40) }));
        Assert.Equal(5, estimator.Estimate(new string('x', 20)));
    }

    [Fact]
    public void AnAnthropicImage_CountsByItsDimensions_NotItsBase64()
    {
        var estimator = new ChatTokenEstimator("Anthropic");
        var message = new
        {
            role = "user",
            content = new object[]
            {
                new { type = "text", text = new string('x', 400) },
                new { type = "image", source = new { type = "base64", media_type = "image/png", data = LargePng(1000, 500) } }
            }
        };

        var estimate = estimator.EstimateMessage(message);

        Assert.Equal(648, estimate.ImageTokens);
        Assert.InRange(estimate.TextTokens, 100, 120);
    }

    [Fact]
    public void AGoogleImage_CountsByItsDimensions()
    {
        var estimator = new ChatTokenEstimator("Google");
        var message = new
        {
            role = "user",
            parts = new object[]
            {
                new { text = "Look" },
                new { inlineData = new { mimeType = "image/png", data = LargePng(1000, 500) } }
            }
        };

        Assert.Equal(1120, estimator.EstimateMessage(message).ImageTokens);
    }

    [Fact]
    public void AnOpenAiImage_CountsByItsDimensions()
    {
        var estimator = new ChatTokenEstimator("OpenAI");
        var message = new
        {
            role = "user",
            content = new object[]
            {
                new { type = "input_text", text = "Look" },
                new { type = "input_image", image_url = "data:image/png;base64," + LargePng(1024, 1024) }
            }
        };

        var estimate = estimator.EstimateMessage(message);

        Assert.Equal(1229, estimate.ImageTokens);
        Assert.True(estimate.Total < 5000, "A 1 MB image must estimate at thousands of tokens, not hundreds of thousands.");
    }

    [Fact]
    public void AnUnreadableImage_UsesTheFlatEstimate()
    {
        var estimator = new ChatTokenEstimator("Anthropic");
        var garbled = new { role = "user", content = new object[] { new { type = "image", source = new { data = "not base64 at all!" } } } };
        var unknownType = new { role = "user", content = new object[] { new { type = "image", source = new { data = Convert.ToBase64String(new byte[64]) } } } };

        Assert.Equal(ChatTokenEstimator.FallbackImageTokens, estimator.EstimateMessage(garbled).ImageTokens);
        Assert.Equal(ChatTokenEstimator.FallbackImageTokens, estimator.EstimateMessage(unknownType).ImageTokens);
    }

    [Fact]
    public void AnUnrecognizedShape_CountsAsText()
    {
        var estimator = new ChatTokenEstimator("Anthropic");
        var message = new { role = "user", content = new object[] { new { somethingNew = new string('x', 4000) } } };

        var estimate = estimator.EstimateMessage(message);

        Assert.Equal(0, estimate.ImageTokens);
        Assert.InRange(estimate.TextTokens, 1000, 1010);
    }

    [Fact]
    public void TheSameObject_IsCachedForTheTurn()
    {
        var estimator = new ChatTokenEstimator("Anthropic");
        var parts = new List<object> { new { text = new string('x', 400) } };
        var message = new { role = "user", parts };

        int first = estimator.Estimate(message);
        parts.Add(new { text = new string('x', 4000) });

        Assert.Equal(first, estimator.Estimate(message));
    }
}
