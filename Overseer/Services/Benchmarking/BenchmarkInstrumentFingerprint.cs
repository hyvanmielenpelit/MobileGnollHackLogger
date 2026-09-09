namespace Overseer.Services.Benchmarking;

/// <summary>
/// The set of instrument and corpus hashes stamped on a benchmark run: the candidate system
/// prompt, the tool guides, and the Git HEAD of each corpus the run could read.
///
/// <para>One shape serves both ends of the series resume guard — the values written onto a run,
/// and the same values recomputed for comparison against those a series recorded for its first
/// member. Five named fields rather than five tuple positions, so that both sides name the hash
/// they are reading.</para>
///
/// <para>Any element may be null, and null means <b>"not recorded"</b> — never a claim that the
/// corpus was absent.</para>
/// </summary>
public sealed record BenchmarkInstrumentFingerprint(
    string? CandidateSystemPromptSha256,
    string? ToolGuidesSha256,
    string? KnowledgeBaseHeadSha,
    string? WikiHeadSha,
    string? SourceCodeHeadSha);
