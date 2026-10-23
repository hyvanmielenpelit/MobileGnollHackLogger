using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackSourcesAndResolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelSource",
                table: "OverseerFeedbackReports",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SystemAiConfigurationId",
                table: "OverseerFeedbackReports",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FixedInVersion",
                table: "OverseerFeedbackIssues",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                table: "OverseerFeedbackIssues",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BenchmarkFeedbackTriages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TriageNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    FixedInVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TriagedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TriagedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkFeedbackTriages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkFeedbackTriages_Fingerprint",
                table: "BenchmarkFeedbackTriages",
                column: "Fingerprint",
                unique: true);

            // Backfill the model source from the chat message that owns the report's tool call. The
            // tool-call row carries no session id, so the message is matched on the report's session.
            // A report whose tool-call row cannot be found stays null (unknown).
            migrationBuilder.Sql(@"
                UPDATE r
                   SET r.ModelSource = CASE WHEN m.SystemAiConfigurationIdUsed IS NULL THEN 'user' ELSE 'system' END,
                       r.SystemAiConfigurationId = m.SystemAiConfigurationIdUsed
                  FROM OverseerFeedbackReports r
                  CROSS APPLY (
                        SELECT TOP (1) cm.SystemAiConfigurationIdUsed
                          FROM ChatMessageToolCall tc
                          JOIN ChatMessage cm ON cm.Id = tc.ChatMessageId
                         WHERE tc.ToolCallId = r.ToolCallId
                           AND cm.ChatSessionId = r.ChatSessionId
                         ORDER BY cm.Id DESC
                  ) m
                 WHERE r.ToolCallId IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkFeedbackTriages");

            migrationBuilder.DropColumn(
                name: "ModelSource",
                table: "OverseerFeedbackReports");

            migrationBuilder.DropColumn(
                name: "SystemAiConfigurationId",
                table: "OverseerFeedbackReports");

            migrationBuilder.DropColumn(
                name: "FixedInVersion",
                table: "OverseerFeedbackIssues");

            migrationBuilder.DropColumn(
                name: "Resolution",
                table: "OverseerFeedbackIssues");
        }
    }
}
