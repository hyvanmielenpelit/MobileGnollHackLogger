using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropInlineModelSettingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssessorModelDisplayNameUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelIdUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelMaxOutputTokensUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelParallelExecutionModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelProviderUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelReasoningModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelReasoningSummaryUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelServiceTierUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelThinkingLevelUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierDisplayNameUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierModelIdUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierProviderUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierReasoningModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierThinkingLevelUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelDisplayNameUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelIdUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelProviderUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelReasoningModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelThinkingLevelUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelDisplayNameUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelIdUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelMaxOutputTokensUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelParallelExecutionModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelProviderUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelReasoningModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelReasoningSummaryUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelServiceTierUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelThinkingLevelUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessedByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedByModelIdUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedByModelProviderUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ReassessedByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AuthorModelDisplayName",
                table: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropColumn(
                name: "AuthorModelIdUsed",
                table: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropColumn(
                name: "AuthorProviderUsed",
                table: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyMaxOutputTokensUsed",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyModel",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyModelIdUsed",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyProviderUsed",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyReasoningModeUsed",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyReasoningSummaryUsed",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyServiceTierUsed",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyThinkingLevelUsed",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessorDisplayNameUsed",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropColumn(
                name: "AssessorMaxOutputTokensUsed",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropColumn(
                name: "AssessorModelIdUsed",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropColumn(
                name: "AssessorProviderUsed",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropColumn(
                name: "AssessorReasoningModeUsed",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropColumn(
                name: "AssessorServiceTierUsed",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropColumn(
                name: "AssessorThinkingLevelUsed",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.AlterColumn<long>(
                name: "TestedModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "AssessorModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "TestedModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "AssessorModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "AssessorModelDisplayNameUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AssessorModelIdUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AssessorModelMaxOutputTokensUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessorModelParallelExecutionModeUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AssessorModelProviderUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AssessorModelReasoningModeUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorModelReasoningSummaryUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorModelServiceTierUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorModelThinkingLevelUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierDisplayNameUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierModelIdUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierProviderUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierReasoningModeUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierThinkingLevelUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelDisplayNameUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelIdUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelProviderUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelReasoningModeUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelThinkingLevelUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestedModelDisplayNameUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TestedModelIdUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TestedModelMaxOutputTokensUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TestedModelParallelExecutionModeUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TestedModelProviderUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TestedModelReasoningModeUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestedModelReasoningSummaryUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestedModelServiceTierUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestedModelThinkingLevelUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedByModelIdUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedByModelProviderUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerificationByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReassessedByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorModelDisplayName",
                table: "BenchmarkRubricAdditionAcceptances",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorModelIdUsed",
                table: "BenchmarkRubricAdditionAcceptances",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorProviderUsed",
                table: "BenchmarkRubricAdditionAcceptances",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessedDifficultyMaxOutputTokensUsed",
                table: "BenchmarkQuestions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedDifficultyModel",
                table: "BenchmarkQuestions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedDifficultyModelIdUsed",
                table: "BenchmarkQuestions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedDifficultyProviderUsed",
                table: "BenchmarkQuestions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedDifficultyReasoningModeUsed",
                table: "BenchmarkQuestions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedDifficultyReasoningSummaryUsed",
                table: "BenchmarkQuestions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedDifficultyServiceTierUsed",
                table: "BenchmarkQuestions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedDifficultyThinkingLevelUsed",
                table: "BenchmarkQuestions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorDisplayNameUsed",
                table: "BenchmarkAssessorCalibrations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessorMaxOutputTokensUsed",
                table: "BenchmarkAssessorCalibrations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorModelIdUsed",
                table: "BenchmarkAssessorCalibrations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorProviderUsed",
                table: "BenchmarkAssessorCalibrations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorReasoningModeUsed",
                table: "BenchmarkAssessorCalibrations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorServiceTierUsed",
                table: "BenchmarkAssessorCalibrations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessorThinkingLevelUsed",
                table: "BenchmarkAssessorCalibrations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            // Restores the copies from the snapshots every row still references.
            migrationBuilder.Sql(@"
UPDATE r SET
    TestedModelProviderUsed = s.Provider, TestedModelIdUsed = s.ModelId, TestedModelDisplayNameUsed = ISNULL(s.DisplayName, N''),
    TestedModelThinkingLevelUsed = s.ThinkingLevel, TestedModelReasoningModeUsed = s.ReasoningMode,
    TestedModelReasoningSummaryUsed = s.ReasoningSummary, TestedModelServiceTierUsed = s.ServiceTier,
    TestedModelMaxOutputTokensUsed = s.MaxOutputTokens, TestedModelParallelExecutionModeUsed = ISNULL(s.ParallelExecutionMode, 2)
FROM BenchmarkRuns r JOIN SystemAiConfigurationSnapshots s ON s.Id = r.TestedModelSnapshotId;
UPDATE r SET
    AssessorModelProviderUsed = s.Provider, AssessorModelIdUsed = s.ModelId, AssessorModelDisplayNameUsed = ISNULL(s.DisplayName, N''),
    AssessorModelThinkingLevelUsed = s.ThinkingLevel, AssessorModelReasoningModeUsed = s.ReasoningMode,
    AssessorModelReasoningSummaryUsed = s.ReasoningSummary, AssessorModelServiceTierUsed = s.ServiceTier,
    AssessorModelMaxOutputTokensUsed = s.MaxOutputTokens, AssessorModelParallelExecutionModeUsed = ISNULL(s.ParallelExecutionMode, 2)
FROM BenchmarkRuns r JOIN SystemAiConfigurationSnapshots s ON s.Id = r.AssessorModelSnapshotId;
UPDATE r SET
    SecondOpinionAssessorModelProviderUsed = s.Provider, SecondOpinionAssessorModelIdUsed = s.ModelId,
    SecondOpinionAssessorModelDisplayNameUsed = s.DisplayName, SecondOpinionAssessorModelThinkingLevelUsed = s.ThinkingLevel,
    SecondOpinionAssessorModelReasoningModeUsed = s.ReasoningMode
FROM BenchmarkRuns r JOIN SystemAiConfigurationSnapshots s ON s.Id = r.SecondOpinionAssessorModelSnapshotId;
UPDATE r SET
    ClaimVerifierProviderUsed = s.Provider, ClaimVerifierModelIdUsed = s.ModelId, ClaimVerifierDisplayNameUsed = s.DisplayName,
    ClaimVerifierThinkingLevelUsed = s.ThinkingLevel, ClaimVerifierReasoningModeUsed = s.ReasoningMode
FROM BenchmarkRuns r JOIN SystemAiConfigurationSnapshots s ON s.Id = r.ClaimVerifierModelSnapshotId;
UPDATE a SET AssessedByModelProviderUsed = s.Provider, AssessedByModelIdUsed = s.ModelId, AssessedByModelDisplayNameUsed = s.DisplayName
FROM BenchmarkRunAnswers a JOIN SystemAiConfigurationSnapshots s ON s.Id = a.AssessedByModelSnapshotId;
UPDATE a SET SecondOpinionByModelDisplayNameUsed = ISNULL(s.DisplayName, s.ModelId)
FROM BenchmarkRunAnswers a JOIN SystemAiConfigurationSnapshots s ON s.Id = a.SecondOpinionByModelSnapshotId;
UPDATE a SET ClaimVerificationByModelDisplayNameUsed = ISNULL(s.DisplayName, s.ModelId)
FROM BenchmarkRunAnswers a JOIN SystemAiConfigurationSnapshots s ON s.Id = a.ClaimVerificationByModelSnapshotId;
UPDATE a SET ReassessedByModelDisplayNameUsed = ISNULL(s.DisplayName, s.ModelId)
FROM BenchmarkRunAnswers a JOIN SystemAiConfigurationSnapshots s ON s.Id = a.ReassessedByModelSnapshotId;
UPDATE q SET
    AssessedDifficultyModel = ISNULL(s.DisplayName, s.ModelId), AssessedDifficultyProviderUsed = s.Provider,
    AssessedDifficultyModelIdUsed = s.ModelId, AssessedDifficultyThinkingLevelUsed = s.ThinkingLevel,
    AssessedDifficultyReasoningModeUsed = s.ReasoningMode, AssessedDifficultyReasoningSummaryUsed = s.ReasoningSummary,
    AssessedDifficultyServiceTierUsed = s.ServiceTier, AssessedDifficultyMaxOutputTokensUsed = s.MaxOutputTokens
FROM BenchmarkQuestions q JOIN SystemAiConfigurationSnapshots s ON s.Id = q.AssessedDifficultyModelSnapshotId;
UPDATE c SET
    AssessorDisplayNameUsed = ISNULL(s.DisplayName, s.ModelId), AssessorProviderUsed = s.Provider, AssessorModelIdUsed = s.ModelId,
    AssessorThinkingLevelUsed = s.ThinkingLevel, AssessorReasoningModeUsed = s.ReasoningMode,
    AssessorServiceTierUsed = s.ServiceTier, AssessorMaxOutputTokensUsed = s.MaxOutputTokens
FROM BenchmarkAssessorCalibrations c JOIN SystemAiConfigurationSnapshots s ON s.Id = c.AssessorModelSnapshotId;
UPDATE x SET AuthorProviderUsed = s.Provider, AuthorModelIdUsed = s.ModelId, AuthorModelDisplayName = s.DisplayName
FROM BenchmarkRubricAdditionAcceptances x JOIN SystemAiConfigurationSnapshots s ON s.Id = x.AuthorModelSnapshotId;
");
        }
    }
}