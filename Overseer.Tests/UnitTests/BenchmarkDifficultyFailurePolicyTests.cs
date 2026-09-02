using Overseer.Services.Benchmarking;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class BenchmarkDifficultyFailurePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decide_WithNoTerminalError_ParsesTheResponse(string? terminalError)
    {
        Assert.Equal(
            BenchmarkDifficultyFailureAction.ParseResponse,
            BenchmarkDifficultyFailurePolicy.Decide(terminalError));
    }

    [Theory]
    // The failure actually reported: the request body was malformed for Gemini. Retrying,
    // repairing, or splitting the batch cannot change the outcome for any later batch.
    [InlineData("API Error: 400 - {\"error\":{\"code\":400,\"message\":\"Invalid JSON payload received. Unknown name \\\"content\\\" at 'contents[0]': Cannot find field.\"}}")]
    [InlineData("API Error: 401 - invalid api key")]
    [InlineData("API Error: 403 - permission denied")]
    [InlineData("API Error: 404 - model not found")]
    [InlineData("The system provider budget has been exhausted. Please contact the administrator.")]
    public void Decide_WithNonRetryableProviderRejection_AbortsTheJob(string terminalError)
    {
        Assert.Equal(
            BenchmarkDifficultyFailureAction.AbortJob,
            BenchmarkDifficultyFailurePolicy.Decide(terminalError));
    }

    [Theory]
    // Transient: the agent loop has already exhausted its own retry ladder, but the next
    // batch may still succeed, so the job continues with this batch marked failed.
    [InlineData("API Error: 429 - rate limit exceeded")]
    [InlineData("429 Rate Limited. Max retries (3) exceeded. Please try again later.")]
    [InlineData("503 Unavailable. Max retries exceeded.")]
    [InlineData("API Error: 500 - internal server error")]
    [InlineData("API Error: 502 - bad gateway")]
    [InlineData("API Error: 504 - gateway timeout")]
    [InlineData("API Error: 529 - overloaded_error")]
    [InlineData("The request timed out.")]
    public void Decide_WithTransientProviderError_FailsOnlyTheBatch(string terminalError)
    {
        Assert.Equal(
            BenchmarkDifficultyFailureAction.FailBatch,
            BenchmarkDifficultyFailurePolicy.Decide(terminalError));
    }
}
