using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkTerminalFailureColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TerminalFailureAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderErrorDetail",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerminalFailureAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ProviderErrorDetail",
                table: "BenchmarkRunAnswers");
        }
    }
}
