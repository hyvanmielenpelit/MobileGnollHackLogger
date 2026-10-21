using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkEvidenceConditionsAndVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorpusCondition",
                table: "BenchmarkRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructionVariant",
                table: "BenchmarkRuns",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "BenchmarkQuestions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorpusCondition",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "InstructionVariant",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "BenchmarkQuestions");
        }
    }
}
