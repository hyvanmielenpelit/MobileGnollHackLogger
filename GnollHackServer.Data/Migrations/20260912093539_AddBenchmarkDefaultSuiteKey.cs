using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkDefaultSuiteKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultSuiteKey",
                table: "BenchmarkSuites",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultSuiteVersion",
                table: "BenchmarkSuites",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultSuiteKeyUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultSuiteKey",
                table: "BenchmarkSuites");

            migrationBuilder.DropColumn(
                name: "DefaultSuiteVersion",
                table: "BenchmarkSuites");

            migrationBuilder.DropColumn(
                name: "DefaultSuiteKeyUsed",
                table: "BenchmarkRuns");
        }
    }
}
