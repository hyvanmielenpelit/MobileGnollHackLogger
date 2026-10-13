using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class OverseerAccessControlAndSecurityEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrunedSecurityEventCount",
                table: "MaintenanceRunLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OverseerBlockReason",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverseerBlockedByUserId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverseerBlockedUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountDeletionRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UserNameSnapshot = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailSnapshot = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    HoldReasons = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DecidedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    LastReviewEmailUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountDeletionRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountHolds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PlacedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PlacedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReleasedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountHolds_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastOccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<short>(type: "smallint", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityEvents_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSystemModelOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSystemModelOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSystemModelOrders_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountDeletionRequests_AspNetUserId_Status",
                table: "AccountDeletionRequests",
                columns: new[] { "AspNetUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountDeletionRequests_Status_RequestedUtc",
                table: "AccountDeletionRequests",
                columns: new[] { "Status", "RequestedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountHolds_AspNetUserId_ReleasedUtc",
                table: "AccountHolds",
                columns: new[] { "AspNetUserId", "ReleasedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_AspNetUserId_OccurredUtc",
                table: "SecurityEvents",
                columns: new[] { "AspNetUserId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_Kind_OccurredUtc",
                table: "SecurityEvents",
                columns: new[] { "Kind", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_OccurredUtc",
                table: "SecurityEvents",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserSystemModelOrders_AspNetUserId_SystemAiApiConfigurationId",
                table: "UserSystemModelOrders",
                columns: new[] { "AspNetUserId", "SystemAiApiConfigurationId" },
                unique: true);

            // Users keep the order they set through their assignments.
            migrationBuilder.Sql(@"
INSERT INTO UserSystemModelOrders (AspNetUserId, SystemAiApiConfigurationId, OrderIndex)
SELECT AspNetUserId, SystemAiApiConfigurationId, MIN(OrderIndex)
FROM UserSystemAiApiConfigurations
WHERE OrderIndex IS NOT NULL
GROUP BY AspNetUserId, SystemAiApiConfigurationId;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountDeletionRequests");

            migrationBuilder.DropTable(
                name: "AccountHolds");

            migrationBuilder.DropTable(
                name: "SecurityEvents");

            migrationBuilder.DropTable(
                name: "UserSystemModelOrders");

            migrationBuilder.DropColumn(
                name: "PrunedSecurityEventCount",
                table: "MaintenanceRunLogs");

            migrationBuilder.DropColumn(
                name: "OverseerBlockReason",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OverseerBlockedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OverseerBlockedUtc",
                table: "AspNetUsers");
        }
    }
}
