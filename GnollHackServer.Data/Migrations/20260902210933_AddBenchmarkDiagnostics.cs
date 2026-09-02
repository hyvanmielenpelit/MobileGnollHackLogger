using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DegradedAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HarnessVersion",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxToolCallsPerQuestionUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToolStarvedAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AnswerFlags",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ModelCallCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RawQualityScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminationReason",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ToolBudgetExhausted",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ToolCallCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DegradedAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "HarnessVersion",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "MaxToolCallsPerQuestionUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ToolStarvedAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AnswerFlags",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ModelCallCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "RawQualityScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "TerminationReason",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ToolBudgetExhausted",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ToolCallCount",
                table: "BenchmarkRunAnswers");
        }
    }
}
