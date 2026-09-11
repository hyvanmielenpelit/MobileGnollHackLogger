using Overseer.Services.Agents;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ProviderErrorRetryPolicyTests
{
    [Theory]
    [InlineData("OpenAI stream error: [server_error] Our servers are currently overloaded. Please try again later.")]
    [InlineData("Our servers are currently overloaded. Please try again later.")]
    [InlineData("Anthropic stream error: [overloaded_error] Overloaded")]
    [InlineData("Anthropic stream error: [rate_limit_error] Number of request tokens has exceeded your per-minute rate limit")]
    [InlineData("Anthropic stream error: [api_error] Internal server error")]
    [InlineData("OpenAI stream error: [rate_limit_exceeded] Rate limit reached for gpt-5.")]
    [InlineData("Google stream error: [overloaded] The model is overloaded.")]
    [InlineData("API Error: 529 - overloaded")]
    [InlineData("API Error: 503 - Service Unavailable")]
    [InlineData("API Error: 502 - Bad Gateway")]
    [InlineData("API Error: 504 - Gateway Timeout")]
    [InlineData("The upstream gateway reported Service Unavailable")]
    [InlineData("You have hit the rate limit for this model")]
    public void IsRetryable_TransientProviderFailures_ReturnsTrue(string errorData)
    {
        Assert.True(ProviderErrorRetryPolicy.IsRetryable(errorData));
    }

    [Theory]
    [InlineData("OpenAI stream error: [invalid_request_error] Unknown parameter: 'reasoning.effort'.")]
    [InlineData("OpenAI stream error: [insufficient_quota] You exceeded your current quota.")]
    [InlineData("OpenAI stream error: [context_length_exceeded] This model's maximum context length is 400000 tokens.")]
    [InlineData("OpenAI stream error: [authentication_error] Incorrect API key provided.")]
    public void IsRetryable_PermanentProviderFailures_ReturnsFalse(string errorData)
    {
        Assert.False(ProviderErrorRetryPolicy.IsRetryable(errorData));
    }

    [Fact]
    public void IsRetryable_DenyTokenAlongsideAllowToken_DenyWins()
    {
        var mixed = "OpenAI stream error: [invalid_request_error] You have hit the rate limit configuration for this request.";
        Assert.False(ProviderErrorRetryPolicy.IsRetryable(mixed));
    }

    [Fact]
    public void IsRetryable_MatchesCaseInsensitively()
    {
        Assert.True(ProviderErrorRetryPolicy.IsRetryable("The model is OVERLOADED right now."));
        Assert.True(ProviderErrorRetryPolicy.IsRetryable("Service Unavailable"));
        Assert.False(ProviderErrorRetryPolicy.IsRetryable("INVALID_REQUEST_ERROR: bad tool schema, please retry"));
    }

    [Fact]
    public void IsRetryable_UnrelatedFailure_ReturnsFalse()
    {
        Assert.False(ProviderErrorRetryPolicy.IsRetryable("OpenAI stream error: [response.failed] Unknown error"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsRetryable_NullOrEmpty_ReturnsFalse(string? errorData)
    {
        Assert.False(ProviderErrorRetryPolicy.IsRetryable(errorData));
    }
}
