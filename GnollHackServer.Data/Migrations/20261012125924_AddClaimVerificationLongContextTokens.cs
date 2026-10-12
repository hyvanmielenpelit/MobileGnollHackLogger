using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimVerificationLongContextTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationLongContextCacheCreationTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationLongContextCacheReadTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationLongContextInputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationLongContextOutputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationLongContextCacheCreationTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationLongContextCacheReadTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationLongContextInputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationLongContextOutputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationLongContextCacheCreationTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationLongContextCacheReadTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationLongContextInputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationLongContextOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationLongContextCacheCreationTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationLongContextCacheReadTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationLongContextInputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationLongContextOutputTokens",
                table: "BenchmarkRunAnswers");
        }
    }
}
