using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeAnalyticsQuery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemAiUsageLogs_SystemAiApiConfigurationId",
                table: "SystemAiUsageLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiUsageLogs_SystemAiApiConfigurationId_TimestampUtc",
                table: "SystemAiUsageLogs",
                columns: new[] { "SystemAiApiConfigurationId", "TimestampUtc" })
                .Annotation("SqlServer:Include", new[] { "AspNetUserId", "RoleContext", "InputTokens", "OutputTokens" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemAiUsageLogs_SystemAiApiConfigurationId_TimestampUtc",
                table: "SystemAiUsageLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiUsageLogs_SystemAiApiConfigurationId",
                table: "SystemAiUsageLogs",
                column: "SystemAiApiConfigurationId");
        }
    }
}
