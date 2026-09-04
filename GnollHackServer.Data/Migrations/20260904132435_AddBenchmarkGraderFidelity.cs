using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkGraderFidelity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SecondOpinionBlind",
                table: "BenchmarkScoringProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BudgetSaturatedAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OmissionAsAccuracyAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "QualityIndexStandardError",
                table: "BenchmarkRuns",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecondOpinionBlindUsed",
                table: "BenchmarkRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ToolCallsBlocked",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecondOpinionBlind",
                table: "BenchmarkScoringProfiles");

            migrationBuilder.DropColumn(
                name: "BudgetSaturatedAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "OmissionAsAccuracyAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "QualityIndexStandardError",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionBlindUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ToolCallsBlocked",
                table: "BenchmarkRunAnswers");
        }
    }
}
