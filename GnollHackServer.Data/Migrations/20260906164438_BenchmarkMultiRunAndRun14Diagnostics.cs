using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class BenchmarkMultiRunAndRun14Diagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionMinimumSample",
                table: "BenchmarkScoringProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RunSeriesId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunSeriesIndex",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionSampleCountUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CompletenessOutOfScope",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BenchmarkRubricAdditionAcceptances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    ItemRevisionAfter = table.Column<int>(type: "int", nullable: false),
                    AcceptedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DraftText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AcceptedVerbatim = table.Column<bool>(type: "bit", nullable: false),
                    Citation = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ClusterClaim = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthorModelConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    AuthorProviderUsed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AuthorModelIdUsed = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AuthorModelDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AcceptedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRubricAdditionAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkRubricAdditionAcceptances_BenchmarkQuestions_BenchmarkQuestionId",
                        column: x => x.BenchmarkQuestionId,
                        principalTable: "BenchmarkQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BenchmarkRubricAdditionAcceptances_SystemAiApiConfigurations_AuthorModelConfigurationId",
                        column: x => x.AuthorModelConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkRunGroups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BenchmarkSuiteId = table.Column<long>(type: "bigint", nullable: true),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    ComparabilityKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TierReasonsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CrossCondition = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedFromSeriesId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRunGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkRunGroups_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BenchmarkRunGroups_BenchmarkSuites_BenchmarkSuiteId",
                        column: x => x.BenchmarkSuiteId,
                        principalTable: "BenchmarkSuites",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkRunSeries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkSuiteId = table.Column<long>(type: "bigint", nullable: true),
                    SuiteName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedRunCount = table.Column<int>(type: "int", nullable: false),
                    CompletedRunCount = table.Column<int>(type: "int", nullable: false),
                    FailedRunCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StopReason = table.Column<int>(type: "int", nullable: true),
                    StartRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowCapWait = table.Column<bool>(type: "bit", nullable: false),
                    FirstMemberCandidateSystemPromptSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FirstMemberToolGuidesSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FirstMemberKnowledgeBaseHeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InstrumentChangeAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    StartedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastProgressAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    AutoCreatedGroupId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRunSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkRunSeries_AspNetUsers_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BenchmarkRunSeries_BenchmarkSuites_BenchmarkSuiteId",
                        column: x => x.BenchmarkSuiteId,
                        principalTable: "BenchmarkSuites",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkGroupAnalyses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkRunGroupId = table.Column<long>(type: "bigint", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MemberRunIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RunCount = table.Column<int>(type: "int", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TierAtComputation = table.Column<int>(type: "int", nullable: false),
                    HarnessVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ScoringMethodVersion = table.Column<int>(type: "int", nullable: false),
                    ComparedWithGroupId = table.Column<long>(type: "bigint", nullable: true),
                    ComparisonJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComputedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkGroupAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkGroupAnalyses_AspNetUsers_ComputedByUserId",
                        column: x => x.ComputedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BenchmarkGroupAnalyses_BenchmarkRunGroups_BenchmarkRunGroupId",
                        column: x => x.BenchmarkRunGroupId,
                        principalTable: "BenchmarkRunGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkRunGroupMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkRunGroupId = table.Column<long>(type: "bigint", nullable: false),
                    BenchmarkRunId = table.Column<long>(type: "bigint", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRunGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkRunGroupMembers_BenchmarkRunGroups_BenchmarkRunGroupId",
                        column: x => x.BenchmarkRunGroupId,
                        principalTable: "BenchmarkRunGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BenchmarkRunGroupMembers_BenchmarkRuns_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalTable: "BenchmarkRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_RunSeriesId_RunSeriesIndex",
                table: "BenchmarkRuns",
                columns: new[] { "RunSeriesId", "RunSeriesIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkGroupAnalyses_BenchmarkRunGroupId_ComputedAtUtc",
                table: "BenchmarkGroupAnalyses",
                columns: new[] { "BenchmarkRunGroupId", "ComputedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkGroupAnalyses_ComputedByUserId",
                table: "BenchmarkGroupAnalyses",
                column: "ComputedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRubricAdditionAcceptances_AuthorModelConfigurationId",
                table: "BenchmarkRubricAdditionAcceptances",
                column: "AuthorModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRubricAdditionAcceptances_BenchmarkQuestionId_AcceptedAtUtc",
                table: "BenchmarkRubricAdditionAcceptances",
                columns: new[] { "BenchmarkQuestionId", "AcceptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunGroupMembers_BenchmarkRunGroupId_BenchmarkRunId",
                table: "BenchmarkRunGroupMembers",
                columns: new[] { "BenchmarkRunGroupId", "BenchmarkRunId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunGroupMembers_BenchmarkRunId",
                table: "BenchmarkRunGroupMembers",
                column: "BenchmarkRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunGroups_BenchmarkSuiteId",
                table: "BenchmarkRunGroups",
                column: "BenchmarkSuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunGroups_CreatedAtUtc",
                table: "BenchmarkRunGroups",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunGroups_CreatedByUserId",
                table: "BenchmarkRunGroups",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunSeries_BenchmarkSuiteId",
                table: "BenchmarkRunSeries",
                column: "BenchmarkSuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunSeries_StartedAtUtc",
                table: "BenchmarkRunSeries",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunSeries_StartedByUserId",
                table: "BenchmarkRunSeries",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunSeries_Status",
                table: "BenchmarkRunSeries",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_BenchmarkRunSeries_RunSeriesId",
                table: "BenchmarkRuns",
                column: "RunSeriesId",
                principalTable: "BenchmarkRunSeries",
                principalColumn: "Id");

            // Data step: put the *default* profile on the sampled second-opinion regime, and only
            // that profile.
            //
            // This is deliberately a data step and never a column default. The column above defaults
            // to 0, so every existing profile — and every profile created later — keeps behaving
            // exactly as it does today, and the single profile whose grading regime is meant to
            // change is changed by name, here, where the change is visible in a diff.
            //
            // A non-zero column default would have silently altered the grading regime of every
            // profile in the database, including ones created for unrelated experiments, and nothing
            // in a later run's report would have said so. That is the run-11 F1 defect, and this
            // shape is the lesson from it.
            //
            // SecondOpinionMode 4 is BenchmarkSecondOpinionMode.FlaggedPlusSample.
            migrationBuilder.Sql(@"
UPDATE [BenchmarkScoringProfiles]
SET [SecondOpinionMode] = 4,
    [SecondOpinionMinimumSample] = 4
WHERE [IsDefault] = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the data step before the column it depends on disappears. Mode 1 is
            // BenchmarkSecondOpinionMode.Flagged, which is what the default profile carried before
            // this migration — leaving mode 4 behind on a rolled-back schema would name a regime the
            // database can no longer describe, because SecondOpinionMinimumSample is dropped below.
            migrationBuilder.Sql(@"
UPDATE [BenchmarkScoringProfiles]
SET [SecondOpinionMode] = 1
WHERE [IsDefault] = 1 AND [SecondOpinionMode] = 4;");

            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_BenchmarkRunSeries_RunSeriesId",
                table: "BenchmarkRuns");

            migrationBuilder.DropTable(
                name: "BenchmarkGroupAnalyses");

            migrationBuilder.DropTable(
                name: "BenchmarkRubricAdditionAcceptances");

            migrationBuilder.DropTable(
                name: "BenchmarkRunGroupMembers");

            migrationBuilder.DropTable(
                name: "BenchmarkRunSeries");

            migrationBuilder.DropTable(
                name: "BenchmarkRunGroups");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_RunSeriesId_RunSeriesIndex",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionMinimumSample",
                table: "BenchmarkScoringProfiles");

            migrationBuilder.DropColumn(
                name: "RunSeriesId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RunSeriesIndex",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionSampleCountUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CompletenessOutOfScope",
                table: "BenchmarkRunAnswers");
        }
    }
}
