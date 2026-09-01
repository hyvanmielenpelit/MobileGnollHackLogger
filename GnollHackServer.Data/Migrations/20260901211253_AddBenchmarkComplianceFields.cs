using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkComplianceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PurposeStatementUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SameProviderAcknowledged",
                table: "BenchmarkRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_StartedAtUtc",
                table: "BenchmarkRuns",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_StartedAtUtc",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "PurposeStatementUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SameProviderAcknowledged",
                table: "BenchmarkRuns");
        }
    }
}
