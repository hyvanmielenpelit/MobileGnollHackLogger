using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackReportModelSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReasoningMode",
                table: "OverseerFeedbackReports",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThinkingLevel",
                table: "OverseerFeedbackReports",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            // Existing reports: the settings of the reply that followed the report's turn, when it still exists.
            migrationBuilder.Sql(@"
UPDATE r SET r.ThinkingLevel = m.ThinkingLevelUsed, r.ReasoningMode = m.ReasoningModeUsed
FROM OverseerFeedbackReports r
CROSS APPLY (
    SELECT TOP 1 cm.ThinkingLevelUsed, cm.ReasoningModeUsed
    FROM ChatMessage cm
    WHERE cm.ChatSessionId = r.ChatSessionId AND cm.Role = 'assistant'
      AND cm.Id > r.TurnUserMessageId AND cm.ModelUsed = r.ModelId
    ORDER BY cm.Id) m
WHERE r.TurnUserMessageId IS NOT NULL AND r.ThinkingLevel IS NULL AND r.ReasoningMode IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReasoningMode",
                table: "OverseerFeedbackReports");

            migrationBuilder.DropColumn(
                name: "ThinkingLevel",
                table: "OverseerFeedbackReports");
        }
    }
}
