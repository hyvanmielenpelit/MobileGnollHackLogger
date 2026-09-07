using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkRunBudgetSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuestionTimeoutSecondsJson",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolIterationCapsJson",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotalModelCallCapsJson",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuestionTimeoutSecondsJson",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ToolIterationCapsJson",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalModelCallCapsJson",
                table: "BenchmarkRuns");
        }
    }
}
