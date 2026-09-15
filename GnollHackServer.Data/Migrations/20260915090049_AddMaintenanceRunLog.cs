using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceRunLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenanceRunLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Trigger = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsDryRun = table.Column<bool>(type: "bit", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ElapsedMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    SoftDeletedCount = table.Column<int>(type: "int", nullable: false),
                    PurgedSessionCount = table.Column<int>(type: "int", nullable: false),
                    PurgedMessageCount = table.Column<int>(type: "int", nullable: false),
                    PurgedToolCallCount = table.Column<int>(type: "int", nullable: false),
                    PrunedToolResultCount = table.Column<int>(type: "int", nullable: false),
                    PrunedBenchmarkToolResultCount = table.Column<int>(type: "int", nullable: false),
                    PrunedAuditLogCount = table.Column<int>(type: "int", nullable: false),
                    PrunedAiErrorLogCount = table.Column<int>(type: "int", nullable: false),
                    DeletedDiskFolderCount = table.Column<int>(type: "int", nullable: false),
                    DeletedDiskFileCount = table.Column<int>(type: "int", nullable: false),
                    SweptOrphanFolderCount = table.Column<int>(type: "int", nullable: false),
                    ReclaimedDiskBytes = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    LogText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRunLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRunLogs_StartedUtc",
                table: "MaintenanceRunLogs",
                column: "StartedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceRunLogs");
        }
    }
}
