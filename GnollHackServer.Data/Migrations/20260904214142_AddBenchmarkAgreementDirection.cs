using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkAgreementDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CandidatePromptOptionsJson",
                table: "BenchmarkRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CandidatePromptSourceUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionCriticalErrorSplitCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "SecondOpinionMeanSignedDelta",
                table: "BenchmarkRuns",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerificationRawText",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            // Harness 11 shipped blind second opinions with the entity and seeder defaulting to true, but
            // AddBenchmarkGraderFidelity added the column with defaultValue: false and backfilled nothing.
            // The default profile predates the column, so it kept grading anchored — run 11 printed
            // "Prompt Protocol: Anchored" at harness version 11. Changing the C# property default alone
            // does not touch a row that already exists.
            migrationBuilder.Sql(@"
                UPDATE BenchmarkScoringProfiles
                   SET SecondOpinionBlind = 1,
                       ModifiedAtUtc = GETUTCDATE()
                 WHERE IsDefault = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE BenchmarkScoringProfiles
                   SET SecondOpinionBlind = 0,
                       ModifiedAtUtc = GETUTCDATE()
                 WHERE IsDefault = 1;
            ");

            migrationBuilder.DropColumn(
                name: "CandidatePromptOptionsJson",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CandidatePromptSourceUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionCriticalErrorSplitCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionMeanSignedDelta",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationRawText",
                table: "BenchmarkRunAnswers");
        }
    }
}
