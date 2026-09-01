using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkScoringProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DifficultyFallbackUsed",
                table: "BenchmarkRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxParallelQuestionsUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QualityIndex",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoringMethodVersion",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ScoringProfileId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoringProfileSnapshotJson",
                table: "BenchmarkRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeedIndex",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SpeedMeasurementDegraded",
                table: "BenchmarkRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "TotalAnswerDurationMs",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "AccuracyLevel",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccuracyScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessedDifficulty",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessmentError",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessmentStatus",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CompletenessLevel",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompletenessScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConcisenessLevel",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConcisenessScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CriticalError",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "QualityScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadabilityLevel",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadabilityScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeedScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessedDifficulty",
                table: "BenchmarkQuestions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssessedDifficultyAtUtc",
                table: "BenchmarkQuestions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedDifficultyModel",
                table: "BenchmarkQuestions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAtUtc",
                table: "BenchmarkQuestions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "BenchmarkScoringProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    WeightAccuracy = table.Column<double>(type: "float", nullable: false),
                    WeightCompleteness = table.Column<double>(type: "float", nullable: false),
                    WeightConciseness = table.Column<double>(type: "float", nullable: false),
                    WeightReadability = table.Column<double>(type: "float", nullable: false),
                    LevelScoresJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriticalErrorCeiling = table.Column<int>(type: "int", nullable: false),
                    SpeedTargetMs = table.Column<int>(type: "int", nullable: false),
                    SpeedDecayK = table.Column<double>(type: "float", nullable: false),
                    MaxParallelQuestions = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkScoringProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_ScoringProfileId",
                table: "BenchmarkRuns",
                column: "ScoringProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkScoringProfiles_IsDefault",
                table: "BenchmarkScoringProfiles",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkScoringProfiles_Name",
                table: "BenchmarkScoringProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_BenchmarkScoringProfiles_ScoringProfileId",
                table: "BenchmarkRuns",
                column: "ScoringProfileId",
                principalTable: "BenchmarkScoringProfiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_BenchmarkScoringProfiles_ScoringProfileId",
                table: "BenchmarkRuns");

            migrationBuilder.DropTable(
                name: "BenchmarkScoringProfiles");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_ScoringProfileId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "DifficultyFallbackUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "MaxParallelQuestionsUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "QualityIndex",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ScoringMethodVersion",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ScoringProfileId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ScoringProfileSnapshotJson",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SpeedIndex",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SpeedMeasurementDegraded",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalAnswerDurationMs",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AccuracyLevel",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AccuracyScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedDifficulty",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessmentError",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessmentStatus",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CompletenessLevel",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CompletenessScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ConcisenessLevel",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ConcisenessScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CriticalError",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "QualityScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ReadabilityLevel",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ReadabilityScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SpeedScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedDifficulty",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyAtUtc",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "AssessedDifficultyModel",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                table: "BenchmarkQuestions");
        }
    }
}
