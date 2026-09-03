using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkSecondOpinionAssessor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionQualityThreshold",
                table: "BenchmarkScoringProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // EF backfills the CLR default, 0, which reads as "score trigger disabled" — while a
            // profile created after this migration gets the entity default of 50. Existing
            // profiles are brought to the same value, so two profiles created a day apart do not
            // behave differently for a reason nobody can see. This is inert until a run is
            // started with a second-opinion assessor selected.
            migrationBuilder.Sql("UPDATE [BenchmarkScoringProfiles] SET [SecondOpinionQualityThreshold] = 50;");

            migrationBuilder.AddColumn<long>(
                name: "SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelDisplayNameUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelIdUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelProviderUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelReasoningModeUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondOpinionAssessorModelThinkingLevelUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns",
                column: "SecondOpinionAssessorModelConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns",
                column: "SecondOpinionAssessorModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionQualityThreshold",
                table: "BenchmarkScoringProfiles");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelDisplayNameUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelIdUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelProviderUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelReasoningModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SecondOpinionAssessorModelThinkingLevelUsed",
                table: "BenchmarkRuns");
        }
    }
}
