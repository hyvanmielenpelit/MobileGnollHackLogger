using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkQuestionAssessorSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssessedDifficultyMaxOutputTokensUsed",
                table: "BenchmarkQuestions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions",
                type: "bigint",
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

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkQuestions_AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions",
                column: "AssessedDifficultyModelConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkQuestions_SystemAiApiConfigurations_AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions",
                column: "AssessedDifficultyModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            // Clear assessments that are stale against modified question content under the new clear-on-edit rule.
            migrationBuilder.Sql(@"
                UPDATE BenchmarkQuestions
                SET AssessedDifficulty = NULL,
                    AssessedDifficultyModel = NULL,
                    AssessedDifficultyAtUtc = NULL
                WHERE AssessedDifficultyAtUtc IS NOT NULL
                  AND AssessedDifficultyAtUtc < ModifiedAtUtc;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkQuestions_SystemAiApiConfigurations_AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkQuestions_AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyMaxOutputTokensUsed",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyModelConfigurationId",
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
        }
    }
}
