using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemAiUsageLogTimestampIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SystemAiUsageLogs_TimestampUtc",
                table: "SystemAiUsageLogs",
                column: "TimestampUtc")
                .Annotation("SqlServer:Include", new[] { "Provider", "ModelId", "RoleContext", "ThinkingLevel", "AspNetUserId", "InputTokens", "OutputTokens", "CacheReadInputTokens", "CacheCreationInputTokens", "EstimatedCost", "TotalDurationMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemAiUsageLogs_TimestampUtc",
                table: "SystemAiUsageLogs");
        }
    }
}
