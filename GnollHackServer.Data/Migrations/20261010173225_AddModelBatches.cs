using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BenchmarkModelBatchRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TargetKind = table.Column<int>(type: "int", nullable: false),
                    BenchmarkSuiteId = table.Column<long>(type: "bigint", nullable: true),
                    BenchmarkBatteryId = table.Column<long>(type: "bigint", nullable: true),
                    TargetName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    BatteryRevision = table.Column<int>(type: "int", nullable: true),
                    BatteryDefinitionSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RunsPerModel = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    OrderSeed = table.Column<int>(type: "int", nullable: true),
                    StartRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowCapWait = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StopReason = table.Column<int>(type: "int", nullable: true),
                    StopDetail = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AcknowledgedFindingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdviceAtStartJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstMemberCandidateSystemPromptSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FirstMemberToolGuidesSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FirstMemberKnowledgeBaseHeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FirstMemberWikiHeadSha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FirstMemberSourceCodeHeadSha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FirstMemberHarnessVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FirstMemberScoringMethodVersion = table.Column<int>(type: "int", nullable: true),
                    InstrumentChangeAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    RequestedMemberCount = table.Column<int>(type: "int", nullable: false),
                    CompletedMemberCount = table.Column<int>(type: "int", nullable: false),
                    FailedMemberCount = table.Column<int>(type: "int", nullable: false),
                    SkippedMemberCount = table.Column<int>(type: "int", nullable: false),
                    CurrentMemberIndex = table.Column<int>(type: "int", nullable: true),
                    LastProgressAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededMembersJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkModelBatchRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkModelBatchMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkModelBatchRunId = table.Column<long>(type: "bigint", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    TestedModelConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    TestedModelSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BenchmarkRunId = table.Column<long>(type: "bigint", nullable: true),
                    BenchmarkRunSeriesId = table.Column<long>(type: "bigint", nullable: true),
                    BenchmarkBatteryRunId = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkModelBatchMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkModelBatchMembers_BenchmarkBatteryRuns_BenchmarkBatteryRunId",
                        column: x => x.BenchmarkBatteryRunId,
                        principalTable: "BenchmarkBatteryRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BenchmarkModelBatchMembers_BenchmarkModelBatchRuns_BenchmarkModelBatchRunId",
                        column: x => x.BenchmarkModelBatchRunId,
                        principalTable: "BenchmarkModelBatchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BenchmarkModelBatchMembers_BenchmarkRunSeries_BenchmarkRunSeriesId",
                        column: x => x.BenchmarkRunSeriesId,
                        principalTable: "BenchmarkRunSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BenchmarkModelBatchMembers_BenchmarkRuns_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalTable: "BenchmarkRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkModelBatchMembers_BenchmarkBatteryRunId",
                table: "BenchmarkModelBatchMembers",
                column: "BenchmarkBatteryRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkModelBatchMembers_BenchmarkModelBatchRunId_OrderIndex",
                table: "BenchmarkModelBatchMembers",
                columns: new[] { "BenchmarkModelBatchRunId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkModelBatchMembers_BenchmarkRunId",
                table: "BenchmarkModelBatchMembers",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkModelBatchMembers_BenchmarkRunSeriesId",
                table: "BenchmarkModelBatchMembers",
                column: "BenchmarkRunSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkModelBatchRuns_CreatedAtUtc",
                table: "BenchmarkModelBatchRuns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkModelBatchRuns_Status",
                table: "BenchmarkModelBatchRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkModelBatchMembers");

            migrationBuilder.DropTable(
                name: "BenchmarkModelBatchRuns");
        }
    }
}
