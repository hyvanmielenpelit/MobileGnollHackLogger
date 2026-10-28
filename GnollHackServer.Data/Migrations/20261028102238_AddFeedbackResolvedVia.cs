using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackResolvedVia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolvedVia",
                table: "OverseerFeedbackIssues",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedVia",
                table: "BenchmarkFeedbackTriages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolvedVia",
                table: "OverseerFeedbackIssues");

            migrationBuilder.DropColumn(
                name: "ResolvedVia",
                table: "BenchmarkFeedbackTriages");
        }
    }
}
