using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkAssessmentUsageAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecoveredAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "TotalAssessmentDurationMs",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalAssessmentInputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalAssessmentOutputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AssessmentDurationMs",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessmentEvidenceJson",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessmentInputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessmentOutputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CriticalErrorQuote",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecondOpinionCriticalError",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecondOpinionDisagreed",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionJson",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionQualityScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecoveredAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalAssessmentDurationMs",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalAssessmentInputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalAssessmentOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessmentDurationMs",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessmentEvidenceJson",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessmentInputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessmentOutputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CriticalErrorQuote",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionCriticalError",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionDisagreed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionJson",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionQualityScore",
                table: "BenchmarkRunAnswers");
        }
    }
}
