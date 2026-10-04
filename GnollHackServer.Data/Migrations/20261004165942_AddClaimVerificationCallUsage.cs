using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimVerificationCallUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimVerificationCallUsageJson",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationModelCallCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerificationServiceTierUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimVerificationCallUsageJson",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationModelCallCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationServiceTierUsed",
                table: "BenchmarkRunAnswers");
        }
    }
}
