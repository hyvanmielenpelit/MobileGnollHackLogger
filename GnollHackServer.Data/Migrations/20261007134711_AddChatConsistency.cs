using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CallTelemetryVersion",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsConsistencyAnchor",
                table: "BenchmarkRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ServedModelIdsJson",
                table: "BenchmarkRuns",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BackoffWaitMs",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "BenchmarkRunAnswers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PermitWaitMs",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryAttemptCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServedModelId",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAtUtc",
                table: "BenchmarkRunAnswers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChatConsistencyAnalysisId",
                table: "BenchmarkReportDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatConsistencyAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubjectModelKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SubjectConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    BaselineStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BaselineEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ComparisonStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ComparisonEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TargetRunIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ControlRunIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProtocolVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProtocolJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelaxedPooling = table.Column<bool>(type: "bit", nullable: false),
                    CommonGraderSnapshotId = table.Column<long>(type: "bigint", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AnalysisCodeVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConsistencyAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatConsistencyAnnotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConsistencyAnnotations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelCallTelemetry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<int>(type: "int", nullable: false),
                    GraderRole = table.Column<int>(type: "int", nullable: true),
                    BenchmarkRunAnswerId = table.Column<long>(type: "bigint", nullable: true),
                    BenchmarkRunId = table.Column<long>(type: "bigint", nullable: true),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ThinkingLevelSent = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReasoningSummarySent = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ServiceTierRequested = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MaxOutputTokensSent = table.Column<int>(type: "int", nullable: true),
                    EndpointKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ServedModelId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ResponseId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ServedServiceTier = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ServedSpeed = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    FinishReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsRefusal = table.Column<bool>(type: "bit", nullable: false),
                    FallbackModelId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    HttpVersion = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CallIndex = table.Column<int>(type: "int", nullable: false),
                    PermitWaitMs = table.Column<int>(type: "int", nullable: false),
                    BackoffWaitMs = table.Column<int>(type: "int", nullable: false),
                    FailedAttemptMs = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<byte>(type: "tinyint", nullable: false),
                    Http429Count = table.Column<byte>(type: "tinyint", nullable: false),
                    Http5xxCount = table.Column<byte>(type: "tinyint", nullable: false),
                    StreamErrorRetryCount = table.Column<byte>(type: "tinyint", nullable: false),
                    FinalHttpStatus = table.Column<int>(type: "int", nullable: true),
                    HeadersMs = table.Column<int>(type: "int", nullable: true),
                    ServerProcessingMs = table.Column<int>(type: "int", nullable: true),
                    FirstEventMs = table.Column<int>(type: "int", nullable: true),
                    FirstReasoningMs = table.Column<int>(type: "int", nullable: true),
                    FirstOutputMs = table.Column<int>(type: "int", nullable: true),
                    FirstToolCallMs = table.Column<int>(type: "int", nullable: true),
                    LastDeltaMs = table.Column<int>(type: "int", nullable: true),
                    CompletedMs = table.Column<int>(type: "int", nullable: true),
                    StreamEndMs = table.Column<int>(type: "int", nullable: true),
                    OutputDeltaCount = table.Column<int>(type: "int", nullable: false),
                    VisibleOutputChars = table.Column<int>(type: "int", nullable: false),
                    Last80DecodeSpanMs = table.Column<int>(type: "int", nullable: true),
                    Last80VisibleChars = table.Column<int>(type: "int", nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    CachedInputTokens = table.Column<int>(type: "int", nullable: true),
                    CacheWriteTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    ReasoningTokens = table.Column<int>(type: "int", nullable: true),
                    RateLimitJson = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ErrorKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCallTelemetry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelCallTelemetry_BenchmarkRunAnswers_BenchmarkRunAnswerId",
                        column: x => x.BenchmarkRunAnswerId,
                        principalTable: "BenchmarkRunAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkReportDocuments_ChatConsistencyAnalysisId",
                table: "BenchmarkReportDocuments",
                column: "ChatConsistencyAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConsistencyAnalyses_SubjectModelKey_CreatedAtUtc",
                table: "ChatConsistencyAnalyses",
                columns: new[] { "SubjectModelKey", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConsistencyAnnotations_AtUtc",
                table: "ChatConsistencyAnnotations",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCallTelemetry_BenchmarkRunAnswerId",
                table: "ModelCallTelemetry",
                column: "BenchmarkRunAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCallTelemetry_Source_Provider_RequestedModelId_StartedAtUtc",
                table: "ModelCallTelemetry",
                columns: new[] { "Source", "Provider", "RequestedModelId", "StartedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkReportDocuments_ChatConsistencyAnalyses_ChatConsistencyAnalysisId",
                table: "BenchmarkReportDocuments",
                column: "ChatConsistencyAnalysisId",
                principalTable: "ChatConsistencyAnalyses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkReportDocuments_ChatConsistencyAnalyses_ChatConsistencyAnalysisId",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropTable(
                name: "ChatConsistencyAnalyses");

            migrationBuilder.DropTable(
                name: "ChatConsistencyAnnotations");

            migrationBuilder.DropTable(
                name: "ModelCallTelemetry");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkReportDocuments_ChatConsistencyAnalysisId",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropColumn(
                name: "CallTelemetryVersion",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "IsConsistencyAnchor",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ServedModelIdsJson",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "BackoffWaitMs",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "PermitWaitMs",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "RetryAttemptCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ServedModelId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ChatConsistencyAnalysisId",
                table: "BenchmarkReportDocuments");
        }
    }
}
