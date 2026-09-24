using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;

namespace Overseer.Tests.Helpers
{
    /// <summary>
    /// Builds <see cref="SystemAiConfigurationSnapshot"/> fixtures. A fixture that cares about model
    /// identity sets a snapshot with <see cref="Model"/>; one that does not calls <c>Attach</c>, which
    /// fills every unset navigation a real row would carry with a deterministic default.
    /// </summary>
    public static class BenchmarkModelSnapshots
    {
        public const string DefaultCandidateProvider = "OpenAI";
        public const string DefaultCandidateModelId = "test-candidate";
        public const string DefaultAssessorProvider = "Anthropic";
        public const string DefaultAssessorModelId = "test-assessor";

        public static SystemAiConfigurationSnapshot Model(
            string provider = DefaultCandidateProvider,
            string modelId = DefaultCandidateModelId,
            string? displayName = null,
            string? thinkingLevel = null,
            string? reasoningMode = null,
            string? reasoningSummary = null,
            string? serviceTier = null,
            int? maxOutputTokens = null,
            MobileGnollHackLogger.Data.ParallelExecutionMode? parallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled,
            string? baseUrl = null,
            string? apiVersion = null,
            string? customHeadersJson = null,
            bool isComplete = true)
        {
            var snapshot = new SystemAiConfigurationSnapshot
            {
                Provider = provider,
                ModelId = modelId,
                DisplayName = displayName,
                ThinkingLevel = thinkingLevel,
                ReasoningMode = reasoningMode,
                ReasoningSummary = reasoningSummary,
                ServiceTier = serviceTier,
                MaxOutputTokens = maxOutputTokens,
                ParallelExecutionMode = parallelExecutionMode,
                BaseUrl = baseUrl,
                ApiVersion = apiVersion,
                CustomHeadersJson = customHeadersJson,
                IsComplete = isComplete,
            };
            snapshot.Sha256 = SystemAiConfigurationSnapshotStore.ComputeSha256(snapshot);
            return snapshot;
        }

        /// <summary>The default candidate model.</summary>
        public static SystemAiConfigurationSnapshot Candidate() => Model();

        /// <summary>The default assessor model.</summary>
        public static SystemAiConfigurationSnapshot Assessor() => Model(DefaultAssessorProvider, DefaultAssessorModelId);

        /// <summary>
        /// Fills the run's required candidate and assessor snapshots when unset, and the second-opinion
        /// and claim-verifier snapshots when the run names those roles. Returns the run.
        /// </summary>
        public static BenchmarkRun Attach(BenchmarkRun run)
        {
            run.TestedModelSnapshot ??= Candidate();
            run.AssessorModelSnapshot ??= Assessor();
            if (run.SecondOpinionAssessorModelConfigurationId.HasValue)
                run.SecondOpinionAssessorModelSnapshot ??= Model("Google", "test-second-opinion");
            if (run.ClaimVerifierModelConfigurationId.HasValue)
                run.ClaimVerifierModelSnapshot ??= Model("Google", "test-claim-verifier");
            return run;
        }

        /// <summary>Fills the answer's grader snapshot when the answer names its assessor.</summary>
        public static BenchmarkRunAnswer Attach(BenchmarkRunAnswer answer)
        {
            if (answer.AssessedByModelConfigurationId.HasValue)
                answer.AssessedByModelSnapshot ??= Assessor();
            return answer;
        }

        /// <summary>Fills the question's difficulty-assessor snapshot when it names one.</summary>
        public static BenchmarkQuestion Attach(BenchmarkQuestion question)
        {
            if (question.AssessedDifficultyModelConfigurationId.HasValue)
                question.AssessedDifficultyModelSnapshot ??= Assessor();
            return question;
        }

        public static BenchmarkAssessorCalibration Attach(BenchmarkAssessorCalibration calibration)
        {
            if (calibration.AssessorModelConfigurationId.HasValue)
                calibration.AssessorModelSnapshot ??= Assessor();
            return calibration;
        }

        public static BenchmarkRubricAdditionAcceptance Attach(BenchmarkRubricAdditionAcceptance acceptance)
        {
            if (acceptance.AuthorModelConfigurationId.HasValue)
                acceptance.AuthorModelSnapshot ??= Assessor();
            return acceptance;
        }
    }
}
