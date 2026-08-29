using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiTokenUsageDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CacheCreationInputTokens",
                table: "SystemAiUsageLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadInputTokens",
                table: "SystemAiUsageLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalDurationMs",
                table: "SystemAiUsageLogs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheCreationInputTokens",
                table: "SystemAiUsageLogs");

            migrationBuilder.DropColumn(
                name: "CacheReadInputTokens",
                table: "SystemAiUsageLogs");

            migrationBuilder.DropColumn(
                name: "TotalDurationMs",
                table: "SystemAiUsageLogs");
        }
    }
}
