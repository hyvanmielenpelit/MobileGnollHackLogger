using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiTelemetryCostUsersAndProviderLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemAiUsageLogs_AspNetUserId",
                table: "SystemAiUsageLogs");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                table: "SystemAiUsageLogs",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingSource",
                table: "SystemAiUsageLogs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThinkingLevel",
                table: "SystemAiUsageLogs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrunedChatProviderAttemptCount",
                table: "MaintenanceRunLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChatProviderAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleContext = table.Column<int>(type: "int", nullable: false),
                    AgentDepth = table.Column<byte>(type: "tinyint", nullable: false),
                    KeySource = table.Column<byte>(type: "tinyint", nullable: false),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ThinkingLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EndpointKind = table.Column<byte>(type: "tinyint", nullable: false),
                    CallIndex = table.Column<int>(type: "int", nullable: false),
                    AttemptIndex = table.Column<byte>(type: "tinyint", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    HttpStatus = table.Column<short>(type: "smallint", nullable: true),
                    ProviderErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Retried = table.Column<bool>(type: "bit", nullable: false),
                    RetryAfterMs = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatProviderAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatProviderAttempts_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OverseerDailyActivities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChatsCreated = table.Column<int>(type: "int", nullable: false),
                    ConfidentialChatsCreated = table.Column<int>(type: "int", nullable: false),
                    GnollHackChatsCreated = table.Column<int>(type: "int", nullable: false),
                    UserMessages = table.Column<int>(type: "int", nullable: false),
                    AssistantMessages = table.Column<int>(type: "int", nullable: false),
                    IncognitoChatsCreated = table.Column<int>(type: "int", nullable: false),
                    IncognitoUserMessages = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverseerDailyActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OverseerDailyActivities_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserKeyAiUsageLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserAiModelId = table.Column<long>(type: "bigint", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModelDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ThinkingLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    CacheReadInputTokens = table.Column<int>(type: "int", nullable: true),
                    CacheCreationInputTokens = table.Column<int>(type: "int", nullable: true),
                    TotalDurationMs = table.Column<int>(type: "int", nullable: true),
                    RoleContext = table.Column<int>(type: "int", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    PricingSource = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserKeyAiUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserKeyAiUsageLogs_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiUsageLogs_AspNetUserId_TimestampUtc",
                table: "SystemAiUsageLogs",
                columns: new[] { "AspNetUserId", "TimestampUtc" })
                .Annotation("SqlServer:Include", new[] { "SystemAiApiConfigurationId", "RoleContext", "InputTokens", "OutputTokens", "EstimatedCost" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatProviderAttempts_AspNetUserId",
                table: "ChatProviderAttempts",
                column: "AspNetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatProviderAttempts_Provider_RequestedModelId_StartedAtUtc",
                table: "ChatProviderAttempts",
                columns: new[] { "Provider", "RequestedModelId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatProviderAttempts_StartedAtUtc",
                table: "ChatProviderAttempts",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OverseerDailyActivities_AspNetUserId",
                table: "OverseerDailyActivities",
                column: "AspNetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OverseerDailyActivities_DateUtc_AspNetUserId",
                table: "OverseerDailyActivities",
                columns: new[] { "DateUtc", "AspNetUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserKeyAiUsageLogs_AspNetUserId_TimestampUtc",
                table: "UserKeyAiUsageLogs",
                columns: new[] { "AspNetUserId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserKeyAiUsageLogs_TimestampUtc",
                table: "UserKeyAiUsageLogs",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatProviderAttempts");

            migrationBuilder.DropTable(
                name: "OverseerDailyActivities");

            migrationBuilder.DropTable(
                name: "UserKeyAiUsageLogs");

            migrationBuilder.DropIndex(
                name: "IX_SystemAiUsageLogs_AspNetUserId_TimestampUtc",
                table: "SystemAiUsageLogs");

            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                table: "SystemAiUsageLogs");

            migrationBuilder.DropColumn(
                name: "PricingSource",
                table: "SystemAiUsageLogs");

            migrationBuilder.DropColumn(
                name: "ThinkingLevel",
                table: "SystemAiUsageLogs");

            migrationBuilder.DropColumn(
                name: "PrunedChatProviderAttemptCount",
                table: "MaintenanceRunLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiUsageLogs_AspNetUserId",
                table: "SystemAiUsageLogs",
                column: "AspNetUserId");
        }
    }
}
