using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkReportDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BenchmarkReportDocuments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Audience = table.Column<int>(type: "int", nullable: false),
                    SubjectKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SubjectLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubjectRunIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ComparisonRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuiteId = table.Column<long>(type: "bigint", nullable: true),
                    SuiteName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WriterConfigId = table.Column<long>(type: "bigint", nullable: true),
                    WriterModelSnapshotId = table.Column<long>(type: "bigint", nullable: true),
                    WriterDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    WriterProvider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WriterModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WriterThinkingLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SameProviderAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    ReportFormatVersion = table.Column<int>(type: "int", nullable: false),
                    WriterPromptSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AnswerExcerptChars = table.Column<int>(type: "int", nullable: false),
                    FactsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WriterOutputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationNotesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    PricingSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkReportDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkReportDocuments_SystemAiConfigurationSnapshots_WriterModelSnapshotId",
                        column: x => x.WriterModelSnapshotId,
                        principalTable: "SystemAiConfigurationSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkReportDocumentRuns",
                columns: table => new
                {
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    FinalScore = table.Column<int>(type: "int", nullable: true),
                    QualityIndex = table.Column<int>(type: "int", nullable: true),
                    SpeedIndex = table.Column<int>(type: "int", nullable: true),
                    ScoringMethodVersion = table.Column<int>(type: "int", nullable: false),
                    RerunCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SynthesisSha256 = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkReportDocumentRuns", x => new { x.DocumentId, x.RunId });
                    table.ForeignKey(
                        name: "FK_BenchmarkReportDocumentRuns_BenchmarkReportDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "BenchmarkReportDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkReportDocumentRuns_RunId",
                table: "BenchmarkReportDocumentRuns",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkReportDocuments_PackId",
                table: "BenchmarkReportDocuments",
                column: "PackId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkReportDocuments_SuiteId_CreatedAtUtc",
                table: "BenchmarkReportDocuments",
                columns: new[] { "SuiteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkReportDocuments_WriterModelSnapshotId",
                table: "BenchmarkReportDocuments",
                column: "WriterModelSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkReportDocumentRuns");

            migrationBuilder.DropTable(
                name: "BenchmarkReportDocuments");
        }
    }
}
