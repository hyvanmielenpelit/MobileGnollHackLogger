using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkBatteries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BenchmarkBatteries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WeightingScheme = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    DefinitionSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkBatteries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkBatteries_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkBatteryRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkBatteryId = table.Column<long>(type: "bigint", nullable: true),
                    BatteryName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefinitionSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RunsPerSuite = table.Column<int>(type: "int", nullable: false),
                    RequestedMemberCount = table.Column<int>(type: "int", nullable: false),
                    CompletedMemberCount = table.Column<int>(type: "int", nullable: false),
                    FailedMemberCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StopReason = table.Column<int>(type: "int", nullable: true),
                    StartRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowCapWait = table.Column<bool>(type: "bit", nullable: false),
                    SuiteFingerprintsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AutoCreatedGroupIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastProgressAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkBatteryRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkBatteryRuns_AspNetUsers_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BenchmarkBatteryRuns_BenchmarkBatteries_BenchmarkBatteryId",
                        column: x => x.BenchmarkBatteryId,
                        principalTable: "BenchmarkBatteries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkBatterySuites",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkBatteryId = table.Column<long>(type: "bigint", nullable: false),
                    BenchmarkSuiteId = table.Column<long>(type: "bigint", nullable: true),
                    SuiteName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    CustomWeight = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkBatterySuites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkBatterySuites_BenchmarkBatteries_BenchmarkBatteryId",
                        column: x => x.BenchmarkBatteryId,
                        principalTable: "BenchmarkBatteries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BenchmarkBatterySuites_BenchmarkSuites_BenchmarkSuiteId",
                        column: x => x.BenchmarkSuiteId,
                        principalTable: "BenchmarkSuites",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkBatteryAnalyses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkBatteryRunId = table.Column<long>(type: "bigint", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MemberRunIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefinitionSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ComparabilityClassSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Complete = table.Column<bool>(type: "bit", nullable: false),
                    HarnessVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ScoringMethodVersion = table.Column<int>(type: "int", nullable: false),
                    ComparedWithBatteryRunId = table.Column<long>(type: "bigint", nullable: true),
                    ComparisonJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComputedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkBatteryAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkBatteryAnalyses_AspNetUsers_ComputedByUserId",
                        column: x => x.ComputedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BenchmarkBatteryAnalyses_BenchmarkBatteryRuns_BenchmarkBatteryRunId",
                        column: x => x.BenchmarkBatteryRunId,
                        principalTable: "BenchmarkBatteryRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkBatteryRunMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkBatteryRunId = table.Column<long>(type: "bigint", nullable: false),
                    BenchmarkRunId = table.Column<long>(type: "bigint", nullable: false),
                    SuiteIndex = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    Superseded = table.Column<bool>(type: "bit", nullable: false),
                    GuardFailure = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkBatteryRunMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkBatteryRunMembers_BenchmarkBatteryRuns_BenchmarkBatteryRunId",
                        column: x => x.BenchmarkBatteryRunId,
                        principalTable: "BenchmarkBatteryRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BenchmarkBatteryRunMembers_BenchmarkRuns_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalTable: "BenchmarkRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteries_CreatedByUserId",
                table: "BenchmarkBatteries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteries_Name",
                table: "BenchmarkBatteries",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryAnalyses_BenchmarkBatteryRunId_ComputedAtUtc",
                table: "BenchmarkBatteryAnalyses",
                columns: new[] { "BenchmarkBatteryRunId", "ComputedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryAnalyses_ComputedByUserId",
                table: "BenchmarkBatteryAnalyses",
                column: "ComputedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryAnalyses_DefinitionSha256_ComputedAtUtc",
                table: "BenchmarkBatteryAnalyses",
                columns: new[] { "DefinitionSha256", "ComputedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryRunMembers_BenchmarkBatteryRunId_BenchmarkRunId",
                table: "BenchmarkBatteryRunMembers",
                columns: new[] { "BenchmarkBatteryRunId", "BenchmarkRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryRunMembers_BenchmarkBatteryRunId_SuiteIndex_Round",
                table: "BenchmarkBatteryRunMembers",
                columns: new[] { "BenchmarkBatteryRunId", "SuiteIndex", "Round" },
                unique: true,
                filter: "[Superseded] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryRunMembers_BenchmarkRunId",
                table: "BenchmarkBatteryRunMembers",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryRuns_BenchmarkBatteryId",
                table: "BenchmarkBatteryRuns",
                column: "BenchmarkBatteryId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryRuns_StartedAtUtc",
                table: "BenchmarkBatteryRuns",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryRuns_StartedByUserId",
                table: "BenchmarkBatteryRuns",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatteryRuns_Status",
                table: "BenchmarkBatteryRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatterySuites_BenchmarkBatteryId_BenchmarkSuiteId",
                table: "BenchmarkBatterySuites",
                columns: new[] { "BenchmarkBatteryId", "BenchmarkSuiteId" },
                unique: true,
                filter: "[BenchmarkSuiteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBatterySuites_BenchmarkSuiteId",
                table: "BenchmarkBatterySuites",
                column: "BenchmarkSuiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkBatteryAnalyses");

            migrationBuilder.DropTable(
                name: "BenchmarkBatteryRunMembers");

            migrationBuilder.DropTable(
                name: "BenchmarkBatterySuites");

            migrationBuilder.DropTable(
                name: "BenchmarkBatteryRuns");

            migrationBuilder.DropTable(
                name: "BenchmarkBatteries");
        }
    }
}
