using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkAssessorPanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssessorOnlyQualityIndex",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessorEffectiveMaxOutputTokens",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessorFinalScore",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CoAssessorModelConfigurationId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CoAssessorModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessorOnlyQualityIndex",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoAssessorSynthesisJson",
                table: "BenchmarkRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CoAssessorSynthesisParseFailed",
                table: "BenchmarkRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CoAssessorSynthesisText",
                table: "BenchmarkRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PanelCriticalErrorSplitCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PanelDisagreementCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PanelGradedAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PanelIntraclassCorrelation",
                table: "BenchmarkRuns",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PanelMeanAbsDelta",
                table: "BenchmarkRuns",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PanelMeanSignedDelta",
                table: "BenchmarkRuns",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoAssessmentCacheCreationTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoAssessmentCacheReadTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoAssessmentDurationMs",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoAssessmentInputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoAssessmentOutputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoSynthesisCacheCreationTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoSynthesisCacheReadTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoSynthesisDurationMs",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoSynthesisInputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCoSynthesisOutputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "CoAssessedAtUtc",
                table: "BenchmarkRunAnswers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CoAssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessmentCacheCreationTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessmentCacheReadTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CoAssessmentCriticalError",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CoAssessmentDurationMs",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoAssessmentError",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessmentInputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoAssessmentJson",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessmentOutputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessmentQualityScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessmentRawQualityScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoAssessmentRawText",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessmentStatus",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoAssessorBoardChars",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PanelDisagreed",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PanelQualityScore",
                table: "BenchmarkRunAnswers",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComparedAgainst",
                table: "BenchmarkAssessorCalibrations",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_CoAssessorModelSnapshotId",
                table: "BenchmarkRuns",
                column: "CoAssessorModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_CoAssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "CoAssessedByModelSnapshotId");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_CoAssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "CoAssessedByModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_CoAssessorModelSnapshotId",
                table: "BenchmarkRuns",
                column: "CoAssessorModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_CoAssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_CoAssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_CoAssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_CoAssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessorOnlyQualityIndex",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessorEffectiveMaxOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessorFinalScore",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessorModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessorOnlyQualityIndex",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessorSynthesisJson",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessorSynthesisParseFailed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessorSynthesisText",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "PanelCriticalErrorSplitCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "PanelDisagreementCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "PanelGradedAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "PanelIntraclassCorrelation",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "PanelMeanAbsDelta",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "PanelMeanSignedDelta",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoAssessmentCacheCreationTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoAssessmentCacheReadTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoAssessmentDurationMs",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoAssessmentInputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoAssessmentOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoSynthesisCacheCreationTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoSynthesisCacheReadTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoSynthesisDurationMs",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoSynthesisInputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalCoSynthesisOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CoAssessedAtUtc",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentCacheCreationTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentCacheReadTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentCriticalError",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentDurationMs",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentError",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentInputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentJson",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentOutputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentQualityScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentRawQualityScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentRawText",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessmentStatus",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CoAssessorBoardChars",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "PanelDisagreed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "PanelQualityScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ComparedAgainst",
                table: "BenchmarkAssessorCalibrations");
        }
    }
}
