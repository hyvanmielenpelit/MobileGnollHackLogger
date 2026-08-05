using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemAiConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemAiApiConfigurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ThinkingLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MaxInputTokens = table.Column<int>(type: "int", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "int", nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ApiKeyNonce = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ApiKeyTag = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IsSystemWide = table.Column<bool>(type: "bit", nullable: false),
                    MaxDailyRequests = table.Column<int>(type: "int", nullable: true),
                    MaxMonthlyRequests = table.Column<int>(type: "int", nullable: true),
                    MaxTotalRequests = table.Column<int>(type: "int", nullable: true),
                    DailyRequestsCount = table.Column<int>(type: "int", nullable: false),
                    MonthlyRequestsCount = table.Column<int>(type: "int", nullable: false),
                    TotalRequestsCount = table.Column<int>(type: "int", nullable: false),
                    LastDailyReset = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastMonthlyReset = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastBudgetNotificationSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsBudgetExhausted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAiApiConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGroups",
                columns: table => new
                {
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroups", x => new { x.AspNetUserId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_UserGroups_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGroups_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupSystemAiApiConfigurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<long>(type: "bigint", nullable: false),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    MaxDailyRequests = table.Column<int>(type: "int", nullable: true),
                    MaxMonthlyRequests = table.Column<int>(type: "int", nullable: true),
                    MaxTotalRequests = table.Column<int>(type: "int", nullable: true),
                    DailyRequestsCount = table.Column<int>(type: "int", nullable: false),
                    MonthlyRequestsCount = table.Column<int>(type: "int", nullable: false),
                    TotalRequestsCount = table.Column<int>(type: "int", nullable: false),
                    LastDailyReset = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastMonthlyReset = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupSystemAiApiConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupSystemAiApiConfigurations_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupSystemAiApiConfigurations_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                        column: x => x.SystemAiApiConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemAiErrorLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDismissed = table.Column<bool>(type: "bit", nullable: false),
                    DismissedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAiErrorLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemAiErrorLogs_AspNetUsers_DismissedByUserId",
                        column: x => x.DismissedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SystemAiErrorLogs_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                        column: x => x.SystemAiApiConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemAiUsageLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAiUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemAiUsageLogs_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SystemAiUsageLogs_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                        column: x => x.SystemAiApiConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSystemAiApiConfigurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    MaxDailyRequests = table.Column<int>(type: "int", nullable: true),
                    MaxMonthlyRequests = table.Column<int>(type: "int", nullable: true),
                    MaxTotalRequests = table.Column<int>(type: "int", nullable: true),
                    DailyRequestsCount = table.Column<int>(type: "int", nullable: false),
                    MonthlyRequestsCount = table.Column<int>(type: "int", nullable: false),
                    TotalRequestsCount = table.Column<int>(type: "int", nullable: false),
                    LastDailyReset = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastMonthlyReset = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSystemAiApiConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSystemAiApiConfigurations_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSystemAiApiConfigurations_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                        column: x => x.SystemAiApiConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupSystemAiApiConfigurations_GroupId",
                table: "GroupSystemAiApiConfigurations",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupSystemAiApiConfigurations_SystemAiApiConfigurationId",
                table: "GroupSystemAiApiConfigurations",
                column: "SystemAiApiConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiErrorLogs_DismissedByUserId",
                table: "SystemAiErrorLogs",
                column: "DismissedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiErrorLogs_SystemAiApiConfigurationId",
                table: "SystemAiErrorLogs",
                column: "SystemAiApiConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiUsageLogs_AspNetUserId",
                table: "SystemAiUsageLogs",
                column: "AspNetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiUsageLogs_SystemAiApiConfigurationId",
                table: "SystemAiUsageLogs",
                column: "SystemAiApiConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_GroupId",
                table: "UserGroups",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSystemAiApiConfigurations_AspNetUserId",
                table: "UserSystemAiApiConfigurations",
                column: "AspNetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSystemAiApiConfigurations_SystemAiApiConfigurationId",
                table: "UserSystemAiApiConfigurations",
                column: "SystemAiApiConfigurationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropTable(
                name: "SystemAiErrorLogs");

            migrationBuilder.DropTable(
                name: "SystemAiUsageLogs");

            migrationBuilder.DropTable(
                name: "UserGroups");

            migrationBuilder.DropTable(
                name: "UserSystemAiApiConfigurations");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "SystemAiApiConfigurations");
        }
    }
}
