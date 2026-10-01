using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotAttemptedAndCriticalErrorResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NotAttemptedScore",
                table: "BenchmarkScoringProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CoAssessmentNotAttempted",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CriticalErrorResolution",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotAttempted",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.Sql("UPDATE [BenchmarkScoringProfiles] SET [NotAttemptedScore] = 50");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotAttemptedScore",
                table: "BenchmarkScoringProfiles");

            migrationBuilder.DropColumn(
                name: "CoAssessmentNotAttempted",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "CriticalErrorResolution",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "NotAttempted",
                table: "BenchmarkRunAnswers");
        }
    }
}
