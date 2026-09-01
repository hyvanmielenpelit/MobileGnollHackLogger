using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiBenchmarkSuites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BenchmarkSuites",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkSuites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkQuestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkSuiteId = table.Column<long>(type: "bigint", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    ExpectedPoints = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkQuestions_BenchmarkSuites_BenchmarkSuiteId",
                        column: x => x.BenchmarkSuiteId,
                        principalTable: "BenchmarkSuites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkSuiteId = table.Column<long>(type: "bigint", nullable: true),
                    SuiteName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TestedModelConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    TestedModelProviderUsed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TestedModelIdUsed = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TestedModelDisplayNameUsed = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TestedModelThinkingLevelUsed = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TestedModelReasoningModeUsed = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TestedModelReasoningSummaryUsed = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TestedModelServiceTierUsed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TestedModelMaxOutputTokensUsed = table.Column<int>(type: "int", nullable: true),
                    TestedModelParallelExecutionModeUsed = table.Column<int>(type: "int", nullable: false),
                    AssessorModelConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    AssessorModelProviderUsed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AssessorModelIdUsed = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AssessorModelDisplayNameUsed = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AssessorModelThinkingLevelUsed = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AssessorModelReasoningModeUsed = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AssessorModelReasoningSummaryUsed = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AssessorModelServiceTierUsed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AssessorModelMaxOutputTokensUsed = table.Column<int>(type: "int", nullable: true),
                    AssessorModelParallelExecutionModeUsed = table.Column<int>(type: "int", nullable: false),
                    StartedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    FinalScore = table.Column<int>(type: "int", nullable: true),
                    ComputedScore = table.Column<int>(type: "int", nullable: true),
                    AnsweredQuestionCount = table.Column<int>(type: "int", nullable: false),
                    TotalQuestionCount = table.Column<int>(type: "int", nullable: false),
                    AssessmentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssessmentText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssessmentParseFailed = table.Column<bool>(type: "bit", nullable: false),
                    TotalInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    TotalOutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    TotalCacheReadTokens = table.Column<long>(type: "bigint", nullable: false),
                    TotalCacheCreationTokens = table.Column<long>(type: "bigint", nullable: false),
                    TotalDurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkRuns_AspNetUsers_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BenchmarkRuns_BenchmarkSuites_BenchmarkSuiteId",
                        column: x => x.BenchmarkSuiteId,
                        principalTable: "BenchmarkSuites",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BenchmarkRuns_SystemAiApiConfigurations_AssessorModelConfigurationId",
                        column: x => x.AssessorModelConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BenchmarkRuns_SystemAiApiConfigurations_TestedModelConfigurationId",
                        column: x => x.TestedModelConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BenchmarkRunAnswers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkRunId = table.Column<long>(type: "bigint", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThoughtText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    TimeToFirstTokenMs = table.Column<long>(type: "bigint", nullable: true),
                    ActualServiceTierUsed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ToolCallSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    CacheReadInputTokens = table.Column<int>(type: "int", nullable: true),
                    CacheCreationInputTokens = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRunAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkRunAnswers_BenchmarkRuns_BenchmarkRunId",
                        column: x => x.BenchmarkRunId,
                        principalTable: "BenchmarkRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkQuestions_BenchmarkSuiteId",
                table: "BenchmarkQuestions",
                column: "BenchmarkSuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_BenchmarkRunId_OrderIndex",
                table: "BenchmarkRunAnswers",
                columns: new[] { "BenchmarkRunId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_AssessorModelConfigurationId",
                table: "BenchmarkRuns",
                column: "AssessorModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_BenchmarkSuiteId",
                table: "BenchmarkRuns",
                column: "BenchmarkSuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_StartedByUserId",
                table: "BenchmarkRuns",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_TestedModelConfigurationId",
                table: "BenchmarkRuns",
                column: "TestedModelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkSuites_Name",
                table: "BenchmarkSuites",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkQuestions");

            migrationBuilder.DropTable(
                name: "BenchmarkRunAnswers");

            migrationBuilder.DropTable(
                name: "BenchmarkRuns");

            migrationBuilder.DropTable(
                name: "BenchmarkSuites");
        }
    }
}
