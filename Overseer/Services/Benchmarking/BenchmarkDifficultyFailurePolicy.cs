namespace Overseer.Services.Benchmarking;

/// <summary>
/// What the difficulty assessment loop should do about a model call that ended in a
/// terminal provider error rather than in a model response.
/// </summary>
public enum BenchmarkDifficultyFailureAction
{
    /// <summary>No terminal error — hand the response text to the parser as usual.</summary>
    ParseResponse,

    /// <summary>
    /// A transient provider error. Fail this batch's questions and move on; do not send a
    /// repair prompt and do not split the batch, because neither addresses the cause.
    /// </summary>
    FailBatch,

    /// <summary>
    /// The request itself was rejected. Every remaining batch would be rejected the same
    /// way, so stop the whole job immediately.
    /// </summary>
    AbortJob
}

/// <summary>
/// Decides how the difficulty assessment loop reacts to a terminal provider error.
/// <para>
/// This exists as a pure function because <c>BenchmarkService.RunDifficultyAssessmentAsync</c>
/// needs a database context, a scope factory and a live agent loop, and so cannot be unit
/// tested — while the decision itself is exactly the part that was wrong. Before this
/// policy existed, a hard <c>400</c> reached the loop as response *text*
/// (<c>AgentLoopRunner</c> appends "**Error:** …" to the response buffer), the parser
/// correctly found no ratings in it, and the loop then escalated through repair prompt,
/// batch split and per-question re-queue — burning the entire model-call budget on a
/// request that could never succeed.
/// </para>
/// </summary>
public static class BenchmarkDifficultyFailurePolicy
{
    public static BenchmarkDifficultyFailureAction Decide(string? terminalError)
    {
        if (string.IsNullOrWhiteSpace(terminalError))
        {
            return BenchmarkDifficultyFailureAction.ParseResponse;
        }

        var classification = BenchmarkProviderErrorClassifier.Classify(terminalError);

        // 429 / 500 / 502 / 503 / 504 / 529 / 408. The agent loop has already exhausted its
        // own retry ladder for these, so retrying here adds calls without adding a chance of
        // success — but the next batch may well succeed, so the job continues.
        if (classification.IsProviderError)
        {
            return BenchmarkDifficultyFailureAction.FailBatch;
        }

        // Everything else, which is where 400 / 401 / 403 / 404 land: a malformed or
        // unauthorized request. Retrying cannot help and neither can a smaller batch.
        return BenchmarkDifficultyFailureAction.AbortJob;
    }
}
