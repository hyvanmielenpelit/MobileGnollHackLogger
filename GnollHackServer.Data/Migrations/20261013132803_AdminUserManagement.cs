using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminUserManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminLockoutByUserId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AdminLockoutEndUtc",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminLockoutReason",
                table: "AspNetUsers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BanReason",
                table: "AspNetUsers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BannedByUserId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BannedUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CreatedUtcIsEstimate",
                table: "AspNetUsers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSignInUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminBulkOperations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActorUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<short>(type: "smallint", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CriteriaJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MatchedCount = table.Column<int>(type: "int", nullable: false),
                    EligibleCount = table.Column<int>(type: "int", nullable: false),
                    RefusedCount = table.Column<int>(type: "int", nullable: false),
                    ChangedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    IncludedAdvanced = table.Column<bool>(type: "bit", nullable: false),
                    RefusedSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminBulkOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminBulkOperationItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsAdvanced = table.Column<bool>(type: "bit", nullable: false),
                    Excluded = table.Column<bool>(type: "bit", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    BeforeStatus = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AfterStatus = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProcessedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminBulkOperationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminBulkOperationItems_AdminBulkOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "AdminBulkOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminUserActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActorUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubjectUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SubjectUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<short>(type: "smallint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    BulkOperationId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUserActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminUserActions_AdminBulkOperations_BulkOperationId",
                        column: x => x.BulkOperationId,
                        principalTable: "AdminBulkOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminBulkOperationItems_OperationId_Outcome",
                table: "AdminBulkOperationItems",
                columns: new[] { "OperationId", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminBulkOperations_Status_CreatedUtc",
                table: "AdminBulkOperations",
                columns: new[] { "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserActions_BulkOperationId",
                table: "AdminUserActions",
                column: "BulkOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserActions_OccurredUtc",
                table: "AdminUserActions",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserActions_SubjectUserId_OccurredUtc",
                table: "AdminUserActions",
                columns: new[] { "SubjectUserId", "OccurredUtc" });

            // Every candidate is an upper bound of the true registration date, so an age criterion
            // is never satisfied earlier than the true date would satisfy it.
            migrationBuilder.Sql(@"
UPDATE u
SET u.CreatedUtc = (
        SELECT MIN(v) FROM (VALUES
            ((SELECT MIN(g.CreatedDate) FROM GameLog g WHERE g.AspNetUserId = u.Id)),
            ((SELECT MIN(c.CreatedUtc) FROM ChatSession c WHERE c.AspNetUserId = u.Id)),
            (SYSUTCDATETIME())
        ) AS candidates(v)),
    u.CreatedUtcIsEstimate = 1
FROM AspNetUsers u
WHERE u.CreatedUtc IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminBulkOperationItems");

            migrationBuilder.DropTable(
                name: "AdminUserActions");

            migrationBuilder.DropTable(
                name: "AdminBulkOperations");

            migrationBuilder.DropColumn(
                name: "AdminLockoutByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AdminLockoutEndUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AdminLockoutReason",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BanReason",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BannedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BannedUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CreatedUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CreatedUtcIsEstimate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastSignInUtc",
                table: "AspNetUsers");
        }
    }
}
