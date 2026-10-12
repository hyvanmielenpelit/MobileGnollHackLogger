using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProviderAvailabilityLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CandidateOurSideFailureCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CandidateProviderErrorCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CandidateUnattributedErrorCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CandidateValidAttemptCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ProviderLedgerVersion",
                table: "BenchmarkRuns",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderRepairPassCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RepairPassCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "ReplacementCause",
                table: "BenchmarkRuns",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReplacesRunId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureKind",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "FailureOrigin",
                table: "BenchmarkRunAnswers",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReExecutionCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BenchmarkProviderAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkRunId = table.Column<long>(type: "bigint", nullable: false),
                    BenchmarkRunAnswerId = table.Column<long>(type: "bigint", nullable: true),
                    QuestionId = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    GraderRole = table.Column<int>(type: "int", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    EndpointKind = table.Column<byte>(type: "tinyint", nullable: false),
                    Pass = table.Column<int>(type: "int", nullable: false),
                    CallIndex = table.Column<int>(type: "int", nullable: false),
                    AttemptIndex = table.Column<byte>(type: "tinyint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    HttpStatus = table.Column<short>(type: "smallint", nullable: true),
                    ProviderErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Retried = table.Column<bool>(type: "bit", nullable: false),
                    RetryAfterMs = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkProviderAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkProviderAttempts_BenchmarkRuns_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalTable: "BenchmarkRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_ReplacesRunId",
                table: "BenchmarkRuns",
                column: "ReplacesRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkProviderAttempts_BenchmarkRunAnswerId",
                table: "BenchmarkProviderAttempts",
                column: "BenchmarkRunAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkProviderAttempts_BenchmarkRunId_Source",
                table: "BenchmarkProviderAttempts",
                columns: new[] { "BenchmarkRunId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkProviderAttempts_StartedAtUtc",
                table: "BenchmarkProviderAttempts",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkProviderAttempts");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_ReplacesRunId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CandidateOurSideFailureCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CandidateProviderErrorCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CandidateUnattributedErrorCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CandidateValidAttemptCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ProviderLedgerVersion",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ProviderRepairPassCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RepairPassCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ReplacementCause",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ReplacesRunId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "FailureKind",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "FailureOrigin",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ReExecutionCount",
                table: "BenchmarkRunAnswers");
        }
    }
}
