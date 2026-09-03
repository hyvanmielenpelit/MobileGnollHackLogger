using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkIntegrityAndTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SpeedDifficultyScaling",
                table: "BenchmarkScoringProfiles",
                type: "float",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<int>(
                name: "AdvisoryFlagAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScrubbedArtifactAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ToolOverheadMs",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransportDefectAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScrubbedArtifactCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScrubbedArtifactText",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToolCallBudgetUsed",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ToolTimeMs",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            // Recalibrate the speed constants on the existing default scoring profile.
            // The previous target of 5000 ms with k = 25 drove the score to its floor at
            // roughly 78 s, which tied together every slower answer on an agentic run. The
            // new constants keep the floor unreachable inside Benchmark:PerQuestionTimeoutSeconds
            // at every difficulty. Changing the C# property default alone would not touch a
            // row that already exists.
            migrationBuilder.Sql(@"
                UPDATE BenchmarkScoringProfiles
                   SET SpeedTargetMs = 15000,
                       SpeedDecayK = 20.0,
                       SpeedDifficultyScaling = 1.0,
                       ModifiedAtUtc = GETUTCDATE()
                 WHERE IsDefault = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE BenchmarkScoringProfiles
                   SET SpeedTargetMs = 5000,
                       SpeedDecayK = 25.0,
                       ModifiedAtUtc = GETUTCDATE()
                 WHERE IsDefault = 1;
            ");

            migrationBuilder.DropColumn(
                name: "SpeedDifficultyScaling",
                table: "BenchmarkScoringProfiles");

            migrationBuilder.DropColumn(
                name: "AdvisoryFlagAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ScrubbedArtifactAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ToolOverheadMs",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TransportDefectAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ScrubbedArtifactCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ScrubbedArtifactText",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ToolCallBudgetUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ToolTimeMs",
                table: "BenchmarkRunAnswers");
        }
    }
}
