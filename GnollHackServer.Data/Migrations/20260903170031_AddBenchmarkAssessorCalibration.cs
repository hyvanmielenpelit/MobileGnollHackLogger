using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkAssessorCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionMode",
                table: "BenchmarkScoringProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionOutlierDeltaPoints",
                table: "BenchmarkScoringProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContestedVerdictAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReassessedAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionGradedAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "SecondOpinionMeanAbsDelta",
                table: "BenchmarkRuns",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionModeUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnweightedQualityIndex",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousQualityScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReassessedAtUtc",
                table: "BenchmarkRunAnswers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReassessedByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReassessmentCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionTrigger",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnverifiedClaimCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnverifiedClaimsJson",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BenchmarkAssessorCalibrations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkRunId = table.Column<long>(type: "bigint", nullable: false),
                    AssessorModelConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    AssessorDisplayNameUsed = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AssessorProviderUsed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AssessorModelIdUsed = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AssessorThinkingLevelUsed = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AssessorReasoningModeUsed = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AssessorServiceTierUsed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AssessorMaxOutputTokensUsed = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AnswerCount = table.Column<int>(type: "int", nullable: false),
                    SkippedAnswerCount = table.Column<int>(type: "int", nullable: false),
                    MeanAbsDelta = table.Column<double>(type: "float", nullable: true),
                    DisagreementCount = table.Column<int>(type: "int", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    VerdictsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkAssessorCalibrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkAssessorCalibrations_BenchmarkRuns_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalTable: "BenchmarkRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BenchmarkAssessorCalibrations_SystemAiApiConfigurations_AssessorModelConfigurationId",
                        column: x => x.AssessorModelConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkAssessorCalibrations_AssessorModelConfigurationId",
                table: "BenchmarkAssessorCalibrations",
                column: "AssessorModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkAssessorCalibrations_BenchmarkRunId_CreatedAtUtc",
                table: "BenchmarkAssessorCalibrations",
                columns: new[] { "BenchmarkRunId", "CreatedAtUtc" });

            // EF generates defaultValue: 0 for a new non-nullable int, which for
            // SecondOpinionMode is Off - so every existing profile would silently stop producing
            // second opinions the moment this migration ran. The CLR initializer (Flagged) only
            // applies to newly constructed entities, never to rows already in the table, so the
            // existing rows have to be set explicitly. Flagged is chosen because it reproduces
            // the behaviour those profiles had before this migration; All is the recommended
            // setting for new work, but recommending it is the editor's job, not a migration's.
            migrationBuilder.Sql(
                "UPDATE [BenchmarkScoringProfiles] SET [SecondOpinionMode] = 1, [SecondOpinionOutlierDeltaPoints] = 25;");

            // Historical runs pre-date the mode entirely. A run that named a second-opinion
            // assessor behaved as Flagged; one that did not was effectively Off. Recording which
            // is which keeps a stored run's report honest about the coverage its agreement
            // figures would rest on, rather than labelling every past run Off.
            migrationBuilder.Sql(
                "UPDATE [BenchmarkRuns] SET [SecondOpinionModeUsed] = 1 WHERE [SecondOpinionAssessorModelConfigurationId] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkAssessorCalibrations");

            migrationBuilder.DropColumn(
                name: "SecondOpinionMode",
                table: "BenchmarkScoringProfiles");

            migrationBuilder.DropColumn(
                name: "SecondOpinionOutlierDeltaPoints",
                table: "BenchmarkScoringProfiles");

            migrationBuilder.DropColumn(
                name: "ContestedVerdictAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ReassessedAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionGradedAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionMeanAbsDelta",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "UnweightedQualityIndex",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "PreviousQualityScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ReassessedAtUtc",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ReassessedByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ReassessmentCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionTrigger",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "UnverifiedClaimCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "UnverifiedClaimsJson",
                table: "BenchmarkRunAnswers");
        }
    }
}
