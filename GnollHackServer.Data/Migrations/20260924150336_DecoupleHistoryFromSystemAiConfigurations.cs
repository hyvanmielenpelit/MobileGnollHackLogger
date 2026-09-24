using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class DecoupleHistoryFromSystemAiConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkAssessorCalibrations_SystemAiApiConfigurations_AssessorModelConfigurationId",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkQuestions_SystemAiApiConfigurations_AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRubricAdditionAcceptances_SystemAiApiConfigurations_AuthorModelConfigurationId",
                table: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiApiConfigurations_AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_AssessorModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_TestedModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemAiErrorLogs_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                table: "SystemAiErrorLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemAiUsageLogs_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                table: "SystemAiUsageLogs");

            migrationBuilder.DropIndex(
                name: "IX_SystemAiErrorLogs_SystemAiApiConfigurationId",
                table: "SystemAiErrorLogs");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_AssessorModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_TestedModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRubricAdditionAcceptances_AuthorModelConfigurationId",
                table: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkQuestions_AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkAssessorCalibrations_AssessorModelConfigurationId",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.AddColumn<string>(
                name: "ModelDisplayName",
                table: "SystemAiUsageLogs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelDisplayName",
                table: "SystemAiErrorLogs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                table: "SystemAiErrorLogs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "SystemAiErrorLogs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessorEffectiveMaxOutputTokens",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssessorModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerifierEffectiveMaxOutputTokens",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClaimVerifierModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SecondOpinionAssessorModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionEffectiveMaxOutputTokens",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TestedModelSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ComparabilityKeyVersion",
                table: "BenchmarkRunGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClaimVerificationByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EvidenceInformedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReassessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SecondOpinionByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AuthorModelSnapshotId",
                table: "BenchmarkRubricAdditionAcceptances",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssessedDifficultyModelSnapshotId",
                table: "BenchmarkQuestions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssessorModelSnapshotId",
                table: "BenchmarkAssessorCalibrations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemAiConfigurationSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sha256 = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
                    IsComplete = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ThinkingLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReasoningMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReasoningSummary = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ServiceTier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "int", nullable: true),
                    ParallelExecutionMode = table.Column<int>(type: "int", nullable: true),
                    BaseUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ApiVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CustomHeadersJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAiConfigurationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiErrorLogs_SystemAiApiConfigurationId_TimestampUtc",
                table: "SystemAiErrorLogs",
                columns: new[] { "SystemAiApiConfigurationId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_AssessorModelSnapshotId",
                table: "BenchmarkRuns",
                column: "AssessorModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_ClaimVerifierModelSnapshotId",
                table: "BenchmarkRuns",
                column: "ClaimVerifierModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_SecondOpinionAssessorModelSnapshotId",
                table: "BenchmarkRuns",
                column: "SecondOpinionAssessorModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_TestedModelSnapshotId",
                table: "BenchmarkRuns",
                column: "TestedModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_AssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "AssessedByModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_ClaimVerificationByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "ClaimVerificationByModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_EvidenceInformedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "EvidenceInformedByModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_ReassessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "ReassessedByModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_SecondOpinionByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "SecondOpinionByModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRubricAdditionAcceptances_AuthorModelSnapshotId",
                table: "BenchmarkRubricAdditionAcceptances",
                column: "AuthorModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkQuestions_AssessedDifficultyModelSnapshotId",
                table: "BenchmarkQuestions",
                column: "AssessedDifficultyModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkAssessorCalibrations_AssessorModelSnapshotId",
                table: "BenchmarkAssessorCalibrations",
                column: "AssessorModelSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiConfigurationSnapshots_Sha256",
                table: "SystemAiConfigurationSnapshots",
                column: "Sha256",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkAssessorCalibrations_SystemAiConfigurationSnapshots_AssessorModelSnapshotId",
                table: "BenchmarkAssessorCalibrations",
                column: "AssessorModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkQuestions_SystemAiConfigurationSnapshots_AssessedDifficultyModelSnapshotId",
                table: "BenchmarkQuestions",
                column: "AssessedDifficultyModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRubricAdditionAcceptances_SystemAiConfigurationSnapshots_AuthorModelSnapshotId",
                table: "BenchmarkRubricAdditionAcceptances",
                column: "AuthorModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_AssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "AssessedByModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_ClaimVerificationByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "ClaimVerificationByModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_EvidenceInformedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "EvidenceInformedByModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_ReassessedByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "ReassessedByModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_SecondOpinionByModelSnapshotId",
                table: "BenchmarkRunAnswers",
                column: "SecondOpinionByModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_AssessorModelSnapshotId",
                table: "BenchmarkRuns",
                column: "AssessorModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_ClaimVerifierModelSnapshotId",
                table: "BenchmarkRuns",
                column: "ClaimVerifierModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_SecondOpinionAssessorModelSnapshotId",
                table: "BenchmarkRuns",
                column: "SecondOpinionAssessorModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_TestedModelSnapshotId",
                table: "BenchmarkRuns",
                column: "TestedModelSnapshotId",
                principalTable: "SystemAiConfigurationSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(BuildBackfillSql());
        }

        // A legacy role: the table, the new snapshot column, and for each snapshot field the legacy
        // column that recorded it, or null when that role never recorded the field.
        private sealed record LegacyRole(
            string Name, int Kind, string Table, string SnapshotColumn, bool IsComplete,
            string? DisplayName, string Provider, string ModelId, string? ThinkingLevel, string? ReasoningMode,
            string? ReasoningSummary, string? ServiceTier, string? MaxOutputTokens, string? ParallelExecutionMode);

        private static readonly LegacyRole[] LegacyRoles =
        {
            new("Candidate", 1, "BenchmarkRuns", "TestedModelSnapshotId", true,
                "TestedModelDisplayNameUsed", "TestedModelProviderUsed", "TestedModelIdUsed", "TestedModelThinkingLevelUsed",
                "TestedModelReasoningModeUsed", "TestedModelReasoningSummaryUsed", "TestedModelServiceTierUsed",
                "TestedModelMaxOutputTokensUsed", "TestedModelParallelExecutionModeUsed"),
            // The assessor's other four columns were never written, so they are not carried.
            new("Assessor", 2, "BenchmarkRuns", "AssessorModelSnapshotId", false,
                "AssessorModelDisplayNameUsed", "AssessorModelProviderUsed", "AssessorModelIdUsed", "AssessorModelThinkingLevelUsed",
                "AssessorModelReasoningModeUsed", null, null, null, null),
            new("Second opinion", 3, "BenchmarkRuns", "SecondOpinionAssessorModelSnapshotId", false,
                "SecondOpinionAssessorModelDisplayNameUsed", "SecondOpinionAssessorModelProviderUsed", "SecondOpinionAssessorModelIdUsed",
                "SecondOpinionAssessorModelThinkingLevelUsed", "SecondOpinionAssessorModelReasoningModeUsed", null, null, null, null),
            new("Claim verifier", 4, "BenchmarkRuns", "ClaimVerifierModelSnapshotId", false,
                "ClaimVerifierDisplayNameUsed", "ClaimVerifierProviderUsed", "ClaimVerifierModelIdUsed",
                "ClaimVerifierThinkingLevelUsed", "ClaimVerifierReasoningModeUsed", null, null, null, null),
            new("Answer assessor", 5, "BenchmarkRunAnswers", "AssessedByModelSnapshotId", false,
                "AssessedByModelDisplayNameUsed", "AssessedByModelProviderUsed", "AssessedByModelIdUsed",
                null, null, null, null, null, null),
            new("Difficulty assessor", 6, "BenchmarkQuestions", "AssessedDifficultyModelSnapshotId", false,
                "AssessedDifficultyModel", "AssessedDifficultyProviderUsed", "AssessedDifficultyModelIdUsed",
                "AssessedDifficultyThinkingLevelUsed", "AssessedDifficultyReasoningModeUsed", "AssessedDifficultyReasoningSummaryUsed",
                "AssessedDifficultyServiceTierUsed", "AssessedDifficultyMaxOutputTokensUsed", null),
            new("Calibration assessor", 7, "BenchmarkAssessorCalibrations", "AssessorModelSnapshotId", false,
                "AssessorDisplayNameUsed", "AssessorProviderUsed", "AssessorModelIdUsed", "AssessorThinkingLevelUsed",
                "AssessorReasoningModeUsed", null, "AssessorServiceTierUsed", "AssessorMaxOutputTokensUsed", null),
            new("Rubric author", 8, "BenchmarkRubricAdditionAcceptances", "AuthorModelSnapshotId", false,
                "AuthorModelDisplayName", "AuthorProviderUsed", "AuthorModelIdUsed", null, null, null, null, null, null),
        };

        private static IEnumerable<(string Field, string? Column, bool IsString)> FieldsOf(LegacyRole r) => new (string, string?, bool)[]
        {
            ("DisplayName", r.DisplayName, true), ("Provider", r.Provider, true), ("ModelId", r.ModelId, true),
            ("ThinkingLevel", r.ThinkingLevel, true), ("ReasoningMode", r.ReasoningMode, true),
            ("ReasoningSummary", r.ReasoningSummary, true), ("ServiceTier", r.ServiceTier, true),
            ("MaxOutputTokens", r.MaxOutputTokens, false), ("ParallelExecutionMode", r.ParallelExecutionMode, false),
        };

        // One batch, run inside the migration's transaction: any THROW rolls the whole migration back.
        // The canonical form and hash match SystemAiConfigurationSnapshotStore: the SHA-256 of the
        // UTF-16LE text, one "Name=value" line per non-null field in ordinal name order. Every legacy
        // benchmark call went to the official endpoint, so the three endpoint fields are null.
        private static string BuildBackfillSql()
        {
            var sql = new StringBuilder();
            sql.AppendLine("SET NOCOUNT ON;");
            sql.AppendLine("CREATE TABLE #stage (");
            sql.AppendLine("    SourceKind tinyint NOT NULL, SourceId bigint NOT NULL, IsComplete bit NOT NULL,");
            sql.AppendLine("    DisplayName nvarchar(256) COLLATE DATABASE_DEFAULT NULL, Provider nvarchar(64) COLLATE DATABASE_DEFAULT NOT NULL,");
            sql.AppendLine("    ModelId nvarchar(128) COLLATE DATABASE_DEFAULT NOT NULL, ThinkingLevel nvarchar(32) COLLATE DATABASE_DEFAULT NULL,");
            sql.AppendLine("    ReasoningMode nvarchar(32) COLLATE DATABASE_DEFAULT NULL, ReasoningSummary nvarchar(32) COLLATE DATABASE_DEFAULT NULL,");
            sql.AppendLine("    ServiceTier nvarchar(64) COLLATE DATABASE_DEFAULT NULL, MaxOutputTokens int NULL, ParallelExecutionMode int NULL,");
            sql.AppendLine("    Sha256 char(64) COLLATE DATABASE_DEFAULT NULL);");

            // 1. Stage.
            foreach (var r in LegacyRoles)
            {
                var columns = string.Join(", ", FieldsOf(r).Select(f => f.Column == null ? "NULL" : "t.[" + f.Column + "]"));
                sql.AppendLine($"INSERT INTO #stage (SourceKind, SourceId, IsComplete, {string.Join(", ", FieldsOf(r).Select(f => f.Field))})");
                sql.AppendLine($"SELECT {r.Kind}, t.Id, {(r.IsComplete ? 1 : 0)}, {columns} FROM [{r.Table}] t");
                sql.AppendLine($"WHERE t.[{r.Provider}] IS NOT NULL AND t.[{r.ModelId}] IS NOT NULL;");
            }

            // 2. Hash. Lines in ordinal name order; ApiVersion, BaseUrl and CustomHeadersJson are null.
            sql.AppendLine("UPDATE #stage SET Sha256 = LOWER(CONVERT(char(64), HASHBYTES('SHA2_256',");
            sql.AppendLine("    CAST(N'SystemAiConfigurationSnapshot/1' AS nvarchar(max)) + NCHAR(10)");
            sql.AppendLine("    + ISNULL(N'DisplayName=' + DisplayName + NCHAR(10), N'')");
            sql.AppendLine("    + N'IsComplete=' + CONVERT(nvarchar(1), IsComplete) + NCHAR(10)");
            sql.AppendLine("    + ISNULL(N'MaxOutputTokens=' + CONVERT(nvarchar(11), MaxOutputTokens) + NCHAR(10), N'')");
            sql.AppendLine("    + N'ModelId=' + ModelId + NCHAR(10)");
            sql.AppendLine("    + ISNULL(N'ParallelExecutionMode=' + CONVERT(nvarchar(11), ParallelExecutionMode) + NCHAR(10), N'')");
            sql.AppendLine("    + N'Provider=' + Provider + NCHAR(10)");
            sql.AppendLine("    + ISNULL(N'ReasoningMode=' + ReasoningMode + NCHAR(10), N'')");
            sql.AppendLine("    + ISNULL(N'ReasoningSummary=' + ReasoningSummary + NCHAR(10), N'')");
            sql.AppendLine("    + ISNULL(N'ServiceTier=' + ServiceTier + NCHAR(10), N'')");
            sql.AppendLine("    + ISNULL(N'ThinkingLevel=' + ThinkingLevel + NCHAR(10), N'')");
            sql.AppendLine("), 2));");

            // 3. Insert the distinct rows, and link every role.
            sql.AppendLine("INSERT INTO SystemAiConfigurationSnapshots (Sha256, IsComplete, Provider, ModelId, DisplayName, ThinkingLevel,");
            sql.AppendLine("    ReasoningMode, ReasoningSummary, ServiceTier, MaxOutputTokens, ParallelExecutionMode, BaseUrl, ApiVersion, CustomHeadersJson, CreatedAtUtc)");
            sql.AppendLine("SELECT d.Sha256, d.IsComplete, d.Provider, d.ModelId, d.DisplayName, d.ThinkingLevel, d.ReasoningMode, d.ReasoningSummary,");
            sql.AppendLine("    d.ServiceTier, d.MaxOutputTokens, d.ParallelExecutionMode, NULL, NULL, NULL, SYSUTCDATETIME()");
            sql.AppendLine("FROM (SELECT Sha256, IsComplete, Provider, ModelId, DisplayName, ThinkingLevel, ReasoningMode, ReasoningSummary,");
            sql.AppendLine("        ServiceTier, MaxOutputTokens, ParallelExecutionMode,");
            sql.AppendLine("        ROW_NUMBER() OVER (PARTITION BY Sha256 ORDER BY SourceKind, SourceId) AS Rn");
            sql.AppendLine("      FROM #stage) d");
            sql.AppendLine("WHERE d.Rn = 1;");
            foreach (var r in LegacyRoles)
            {
                sql.AppendLine($"UPDATE t SET [{r.SnapshotColumn}] = s.Id FROM [{r.Table}] t");
                sql.AppendLine($"JOIN #stage st ON st.SourceKind = {r.Kind} AND st.SourceId = t.Id");
                sql.AppendLine("JOIN SystemAiConfigurationSnapshots s ON s.Sha256 = st.Sha256;");
            }

            // 4. N3 links, best effort: matched by the display name each legacy writer recorded
            //    (DisplayName ?? ModelId). Anything unmatched stays null.
            sql.AppendLine("UPDATE a SET SecondOpinionByModelSnapshotId = s.Id FROM BenchmarkRunAnswers a");
            sql.AppendLine("JOIN BenchmarkRuns r ON r.Id = a.BenchmarkRunId JOIN SystemAiConfigurationSnapshots s ON s.Id = r.SecondOpinionAssessorModelSnapshotId");
            sql.AppendLine("WHERE a.SecondOpinionByModelDisplayNameUsed = ISNULL(s.DisplayName, s.ModelId);");
            sql.AppendLine("UPDATE a SET ClaimVerificationByModelSnapshotId = s.Id FROM BenchmarkRunAnswers a");
            sql.AppendLine("JOIN BenchmarkRuns r ON r.Id = a.BenchmarkRunId JOIN SystemAiConfigurationSnapshots s ON s.Id = r.ClaimVerifierModelSnapshotId");
            sql.AppendLine("WHERE a.ClaimVerificationByModelDisplayNameUsed = ISNULL(s.DisplayName, s.ModelId);");
            sql.AppendLine("UPDATE a SET ReassessedByModelSnapshotId = s.Id FROM BenchmarkRunAnswers a");
            sql.AppendLine("JOIN SystemAiConfigurationSnapshots s ON s.Id = a.AssessedByModelSnapshotId");
            sql.AppendLine("WHERE a.ReassessedByModelDisplayNameUsed = ISNULL(s.DisplayName, s.ModelId);");
            // An evidence-informed re-grade can run under an override assessor, so the JSON's own key decides.
            sql.AppendLine("UPDATE a SET EvidenceInformedByModelSnapshotId = s.Id FROM BenchmarkRunAnswers a");
            sql.AppendLine("JOIN BenchmarkRuns r ON r.Id = a.BenchmarkRunId JOIN SystemAiConfigurationSnapshots s ON s.Id = r.AssessorModelSnapshotId");
            sql.AppendLine("WHERE CASE WHEN ISJSON(a.EvidenceInformedJson) = 1 THEN JSON_VALUE(a.EvidenceInformedJson, '$.assessor') END = ISNULL(s.DisplayName, s.ModelId);");

            // 5. Log copies, from each row's configuration while it still exists.
            sql.AppendLine("UPDATE l SET ModelDisplayName = c.DisplayName FROM SystemAiUsageLogs l");
            sql.AppendLine("JOIN SystemAiApiConfigurations c ON c.Id = l.SystemAiApiConfigurationId;");
            sql.AppendLine("UPDATE l SET Provider = c.Provider, ModelId = c.ModelId, ModelDisplayName = c.DisplayName FROM SystemAiErrorLogs l");
            sql.AppendLine("JOIN SystemAiApiConfigurations c ON c.Id = l.SystemAiApiConfigurationId;");

            // Self-check.
            sql.AppendLine("DECLARE @n int, @msg nvarchar(2048);");
            foreach (var r in LegacyRoles)
            {
                // 1: legacy data with no snapshot.
                sql.AppendLine($"SELECT @n = COUNT(*) FROM [{r.Table}] t WHERE t.[{r.Provider}] IS NOT NULL AND t.[{r.ModelId}] IS NOT NULL AND t.[{r.SnapshotColumn}] IS NULL;");
                AppendThrow(sql, r.Name + " (missing snapshot)");

                // 2: a snapshot field that differs from its legacy column, compared exactly.
                var equal = FieldsOf(r).Select(f => f.Column == null
                    ? $"s.{f.Field} IS NULL"
                    : f.IsString
                        ? $"(s.{f.Field} = t.[{f.Column}] COLLATE Latin1_General_BIN2 OR (s.{f.Field} IS NULL AND t.[{f.Column}] IS NULL))"
                        : $"(s.{f.Field} = t.[{f.Column}] OR (s.{f.Field} IS NULL AND t.[{f.Column}] IS NULL))")
                    .Append($"s.IsComplete = {(r.IsComplete ? 1 : 0)}")
                    .Append("s.BaseUrl IS NULL AND s.ApiVersion IS NULL AND s.CustomHeadersJson IS NULL");
                sql.AppendLine($"SELECT @n = COUNT(*) FROM [{r.Table}] t JOIN SystemAiConfigurationSnapshots s ON s.Id = t.[{r.SnapshotColumn}]");
                sql.AppendLine($"WHERE CASE WHEN {string.Join(Environment.NewLine + "    AND ", equal)} THEN 0 ELSE 1 END = 1;");
                AppendThrow(sql, r.Name + " (field mismatch)");
            }

            // 3: no hash held by more than one row.
            sql.AppendLine("SELECT @n = COUNT(*) FROM (SELECT Sha256 FROM SystemAiConfigurationSnapshots GROUP BY Sha256 HAVING COUNT(*) > 1) d;");
            AppendThrow(sql, "Snapshots (duplicate hash)");

            sql.AppendLine("DROP TABLE #stage;");
            return sql.ToString();
        }

        private static void AppendThrow(StringBuilder sql, string role)
        {
            sql.AppendLine("IF @n > 0");
            sql.AppendLine("BEGIN");
            sql.AppendLine($"    SET @msg = CONCAT(N'{role}: ', @n, N' rows failed the snapshot backfill check');");
            sql.AppendLine("    THROW 50001, @msg, 1;");
            sql.AppendLine("END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The restored foreign keys need a parent: attribution ids of deleted configurations are
            // cleared, and log rows of deleted configurations are removed.
            migrationBuilder.Sql(@"
UPDATE BenchmarkRuns SET TestedModelConfigurationId = NULL WHERE TestedModelConfigurationId IS NOT NULL AND TestedModelConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
UPDATE BenchmarkRuns SET AssessorModelConfigurationId = NULL WHERE AssessorModelConfigurationId IS NOT NULL AND AssessorModelConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
UPDATE BenchmarkRuns SET SecondOpinionAssessorModelConfigurationId = NULL WHERE SecondOpinionAssessorModelConfigurationId IS NOT NULL AND SecondOpinionAssessorModelConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
UPDATE BenchmarkRuns SET ClaimVerifierModelConfigurationId = NULL WHERE ClaimVerifierModelConfigurationId IS NOT NULL AND ClaimVerifierModelConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
UPDATE BenchmarkRunAnswers SET AssessedByModelConfigurationId = NULL WHERE AssessedByModelConfigurationId IS NOT NULL AND AssessedByModelConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
UPDATE BenchmarkQuestions SET AssessedDifficultyModelConfigurationId = NULL WHERE AssessedDifficultyModelConfigurationId IS NOT NULL AND AssessedDifficultyModelConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
UPDATE BenchmarkAssessorCalibrations SET AssessorModelConfigurationId = NULL WHERE AssessorModelConfigurationId IS NOT NULL AND AssessorModelConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
UPDATE BenchmarkRubricAdditionAcceptances SET AuthorModelConfigurationId = NULL WHERE AuthorModelConfigurationId IS NOT NULL AND AuthorModelConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
DELETE FROM SystemAiUsageLogs WHERE SystemAiApiConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
DELETE FROM SystemAiErrorLogs WHERE SystemAiApiConfigurationId NOT IN (SELECT Id FROM SystemAiApiConfigurations);
");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkAssessorCalibrations_SystemAiConfigurationSnapshots_AssessorModelSnapshotId",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkQuestions_SystemAiConfigurationSnapshots_AssessedDifficultyModelSnapshotId",
                table: "BenchmarkQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRubricAdditionAcceptances_SystemAiConfigurationSnapshots_AuthorModelSnapshotId",
                table: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_AssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_ClaimVerificationByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_EvidenceInformedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_ReassessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiConfigurationSnapshots_SecondOpinionByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_AssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_ClaimVerifierModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_SecondOpinionAssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiConfigurationSnapshots_TestedModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropTable(
                name: "SystemAiConfigurationSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_SystemAiErrorLogs_SystemAiApiConfigurationId_TimestampUtc",
                table: "SystemAiErrorLogs");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_AssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_ClaimVerifierModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_SecondOpinionAssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_TestedModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_AssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_ClaimVerificationByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_EvidenceInformedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_ReassessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_SecondOpinionByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRubricAdditionAcceptances_AuthorModelSnapshotId",
                table: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkQuestions_AssessedDifficultyModelSnapshotId",
                table: "BenchmarkQuestions");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkAssessorCalibrations_AssessorModelSnapshotId",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropColumn(
                name: "ModelDisplayName",
                table: "SystemAiUsageLogs");

            migrationBuilder.DropColumn(
                name: "ModelDisplayName",
                table: "SystemAiErrorLogs");

            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "SystemAiErrorLogs");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "SystemAiErrorLogs");

            migrationBuilder.DropColumn(
                name: "AssessorEffectiveMaxOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierEffectiveMaxOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionEffectiveMaxOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TestedModelSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ComparabilityKeyVersion",
                table: "BenchmarkRunGroups");

            migrationBuilder.DropColumn(
                name: "AssessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "EvidenceInformedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ReassessedByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionByModelSnapshotId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AuthorModelSnapshotId",
                table: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyModelSnapshotId",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessorModelSnapshotId",
                table: "BenchmarkAssessorCalibrations");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAiErrorLogs_SystemAiApiConfigurationId",
                table: "SystemAiErrorLogs",
                column: "SystemAiApiConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_AssessorModelConfigurationId",
                table: "BenchmarkRuns",
                column: "AssessorModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns",
                column: "ClaimVerifierModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns",
                column: "SecondOpinionAssessorModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_TestedModelConfigurationId",
                table: "BenchmarkRuns",
                column: "TestedModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers",
                column: "AssessedByModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRubricAdditionAcceptances_AuthorModelConfigurationId",
                table: "BenchmarkRubricAdditionAcceptances",
                column: "AuthorModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkQuestions_AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions",
                column: "AssessedDifficultyModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkAssessorCalibrations_AssessorModelConfigurationId",
                table: "BenchmarkAssessorCalibrations",
                column: "AssessorModelConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkAssessorCalibrations_SystemAiApiConfigurations_AssessorModelConfigurationId",
                table: "BenchmarkAssessorCalibrations",
                column: "AssessorModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkQuestions_SystemAiApiConfigurations_AssessedDifficultyModelConfigurationId",
                table: "BenchmarkQuestions",
                column: "AssessedDifficultyModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRubricAdditionAcceptances_SystemAiApiConfigurations_AuthorModelConfigurationId",
                table: "BenchmarkRubricAdditionAcceptances",
                column: "AuthorModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiApiConfigurations_AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers",
                column: "AssessedByModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_AssessorModelConfigurationId",
                table: "BenchmarkRuns",
                column: "AssessorModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns",
                column: "ClaimVerifierModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns",
                column: "SecondOpinionAssessorModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_TestedModelConfigurationId",
                table: "BenchmarkRuns",
                column: "TestedModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemAiErrorLogs_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                table: "SystemAiErrorLogs",
                column: "SystemAiApiConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemAiUsageLogs_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                table: "SystemAiUsageLogs",
                column: "SystemAiApiConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
