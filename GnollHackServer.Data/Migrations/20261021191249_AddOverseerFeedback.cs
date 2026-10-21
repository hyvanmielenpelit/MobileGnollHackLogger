using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOverseerFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BenchmarkRunAnswerSelfReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkRunAnswerId = table.Column<long>(type: "bigint", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IterationIndex = table.Column<int>(type: "int", nullable: true),
                    Accepted = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SearchedFor = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExpectedLocation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RelatedToolsJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NoticedVia = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SuspectedCause = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SuspectedCauseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Truncated = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRunAnswerSelfReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkRunAnswerSelfReports_BenchmarkRunAnswers_BenchmarkRunAnswerId",
                        column: x => x.BenchmarkRunAnswerId,
                        principalTable: "BenchmarkRunAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OverseerFeedbackIssues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    RegressionCount = table.Column<int>(type: "int", nullable: false),
                    LastRegressionUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TriageNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TriagedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TriagedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverseerFeedbackIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OverseerFeedbackSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SetByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverseerFeedbackSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OverseerFeedbackReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IssueId = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SearchedFor = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExpectedLocation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RelatedToolsJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NoticedVia = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SuspectedCause = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SuspectedCauseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Truncated = table.Column<bool>(type: "bit", nullable: false),
                    Redacted = table.Column<bool>(type: "bit", nullable: false),
                    ChatSessionId = table.Column<long>(type: "bigint", nullable: false),
                    TurnUserMessageId = table.Column<long>(type: "bigint", nullable: true),
                    ToolCallId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AgentName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ModelId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AppVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverseerFeedbackReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OverseerFeedbackReports_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OverseerFeedbackReports_OverseerFeedbackIssues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "OverseerFeedbackIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswerSelfReports_BenchmarkRunAnswerId_SortOrder",
                table: "BenchmarkRunAnswerSelfReports",
                columns: new[] { "BenchmarkRunAnswerId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswerSelfReports_Fingerprint",
                table: "BenchmarkRunAnswerSelfReports",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_OverseerFeedbackIssues_Fingerprint",
                table: "OverseerFeedbackIssues",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OverseerFeedbackIssues_LastSeenUtc",
                table: "OverseerFeedbackIssues",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OverseerFeedbackIssues_Status_LastSeenUtc",
                table: "OverseerFeedbackIssues",
                columns: new[] { "Status", "LastSeenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OverseerFeedbackReports_AspNetUserId_CreatedUtc",
                table: "OverseerFeedbackReports",
                columns: new[] { "AspNetUserId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OverseerFeedbackReports_ChatSessionId_Fingerprint",
                table: "OverseerFeedbackReports",
                columns: new[] { "ChatSessionId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OverseerFeedbackReports_CreatedUtc",
                table: "OverseerFeedbackReports",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OverseerFeedbackReports_IssueId_CreatedUtc",
                table: "OverseerFeedbackReports",
                columns: new[] { "IssueId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkRunAnswerSelfReports");

            migrationBuilder.DropTable(
                name: "OverseerFeedbackReports");

            migrationBuilder.DropTable(
                name: "OverseerFeedbackSettings");

            migrationBuilder.DropTable(
                name: "OverseerFeedbackIssues");
        }
    }
}
