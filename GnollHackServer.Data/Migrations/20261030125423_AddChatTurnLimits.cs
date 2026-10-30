using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatTurnLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrunedChatTurnLimitEventCount",
                table: "MaintenanceRunLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TurnNoticeCode",
                table: "ChatMessage",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TurnNoticeFactsJson",
                table: "ChatMessage",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EndedWithoutFinalAnswer",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UnfinishedAnswerRecovered",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikiContextArticles",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatTurnLimitEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChatSessionId = table.Column<long>(type: "bigint", nullable: true),
                    UserMessageId = table.Column<long>(type: "bigint", nullable: true),
                    AssistantMessageId = table.Column<long>(type: "bigint", nullable: true),
                    WasConfidential = table.Column<bool>(type: "bit", nullable: false),
                    WasEphemeral = table.Column<bool>(type: "bit", nullable: false),
                    KeySource = table.Column<byte>(type: "tinyint", nullable: false),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RequestedModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ModelDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ThinkingLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LimitValue = table.Column<int>(type: "int", nullable: true),
                    ObservedValue = table.Column<int>(type: "int", nullable: true),
                    IterationsUsed = table.Column<int>(type: "int", nullable: false),
                    ModelCallCount = table.Column<int>(type: "int", nullable: false),
                    ToolCallCount = table.Column<int>(type: "int", nullable: false),
                    ToolCallsBlocked = table.Column<int>(type: "int", nullable: false),
                    TotalDurationMs = table.Column<int>(type: "int", nullable: true),
                    ToolTimeMs = table.Column<long>(type: "bigint", nullable: false),
                    PermitWaitMs = table.Column<long>(type: "bigint", nullable: false),
                    BackoffWaitMs = table.Column<long>(type: "bigint", nullable: false),
                    RetryAttemptCount = table.Column<int>(type: "int", nullable: false),
                    TotalPromptTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    ProviderFinishReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    AssistantPhase = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatTurnLimitEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatTurnLimitEvents_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatTurnLimitEvents_AspNetUserId_OccurredUtc",
                table: "ChatTurnLimitEvents",
                columns: new[] { "AspNetUserId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatTurnLimitEvents_Code_OccurredUtc",
                table: "ChatTurnLimitEvents",
                columns: new[] { "Code", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatTurnLimitEvents_OccurredUtc",
                table: "ChatTurnLimitEvents",
                column: "OccurredUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatTurnLimitEvents");

            migrationBuilder.DropColumn(
                name: "PrunedChatTurnLimitEventCount",
                table: "MaintenanceRunLogs");

            migrationBuilder.DropColumn(
                name: "TurnNoticeCode",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "TurnNoticeFactsJson",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "EndedWithoutFinalAnswer",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "UnfinishedAnswerRecovered",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "WikiContextArticles",
                table: "BenchmarkRunAnswers");
        }
    }
}
