using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkPerRoleCostTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TotalAssessmentCacheCreationTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalAssessmentCacheReadTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationCacheCreationTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationCacheReadTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSecondOpinionCacheCreationTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSecondOpinionCacheReadTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSecondOpinionDurationMs",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSecondOpinionInputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSecondOpinionOutputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSynthesisCacheCreationTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSynthesisCacheReadTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSynthesisDurationMs",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSynthesisInputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalSynthesisOutputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "AssessmentCacheCreationTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessmentCacheReadTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationCacheCreationTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationCacheReadTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionCacheCreationTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionCacheReadTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SecondOpinionDurationMs",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionInputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionOutputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalAssessmentCacheCreationTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalAssessmentCacheReadTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationCacheCreationTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationCacheReadTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSecondOpinionCacheCreationTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSecondOpinionCacheReadTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSecondOpinionDurationMs",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSecondOpinionInputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSecondOpinionOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSynthesisCacheCreationTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSynthesisCacheReadTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSynthesisDurationMs",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSynthesisInputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalSynthesisOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessmentCacheCreationTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessmentCacheReadTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationCacheCreationTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationCacheReadTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionCacheCreationTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionCacheReadTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionDurationMs",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionInputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionOutputTokens",
                table: "BenchmarkRunAnswers");
        }
    }
}
